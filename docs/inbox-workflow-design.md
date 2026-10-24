# Inbox：邮件驱动的账单数据接入工作流

> 状态：Phase 1–5 已实现（手动触发 + 待办介入 + 标准导入、IMAP 取附件、加密解压与工作目录清理、Quartz 定时与失败重试、SignalR 实时刷新、敏感数据加密与审计/历史保留）。
> 本文档描述整体设计。

## 1. 背景与目标

账单文件通常通过邮件送达，附件往往带密码（如支付宝 ZIP 的身份证后六位、微信支付 ZIP 在公众号查看的密码）。下载、解压、定位文件、上传导入这一串动作需要人工参与一两次，但其余环节应当自动化。

本功能引入一个**声明式工作流引擎**：用户用 YAML 描述“从哪个邮箱取什么邮件 → 下载附件 → 需要哪些人工输入 → 如何解压/定位文件 → 调用标准导入流程”。引擎按 GitHub Actions 的哲学实现：

- 工作流是仓库里的 YAML 文件，而不是写死的代码；
- 触发（`on`）与执行（`jobs` / `steps` / `uses`）分离；
- 每个步骤是独立、可组合的 Action，通过 `${{ steps.x.outputs.y }}` 传递数据；
- 人工介入是**一等公民**（`human/input`），但不以“用户正盯着页面”为前提；
- 每次运行都有完整的步骤级追踪（状态、输入输出、日志、错误）。

目标：

1. 新增接入源只需新增/修改 YAML，不写 C#；
2. 人工介入通过**待办事项 + 应用通知**完成，用户可以任意时间处理，进程重启不丢状态；
3. 流程最终衔接到现有 `IBillImportService` / 处理管道，不重复实现解析、去重、分类逻辑。

## 2. 非目标

- 不做 7z / RAR 等非 ZIP 归档格式（`archive/extract` 仅支持 ZIP，含 ZipCrypto 与 AES）；
- 不做多实例/集群调度：Quartz 使用内存 JobStore，进程启动后由目录同步器重建定时任务；
- 不做错过触发的补跑（沿用 Quartz 默认 misfire 策略）；
- 不做外部金库（Vault/KMS）集成或多实例密钥共享：Secret 使用 ASP.NET Core Data Protection 落盘加密，密钥环保存在本地 `data/keys`，需随数据库一并备份；
- 不做多 Job 并行、`needs` 依赖图、条件表达式（`if`）、矩阵构建；
- 不做多用户权限体系（当前应用为单用户本地部署）。

Phase 1 交付的是**可运行的引擎骨架**：手动触发 → 可选上传种子文件 → `human/input` 生成待办并挂起 → 用户完成待办 → 自动恢复 → `bill/import` → `bill/process` → 完成。
Phase 2 在此基础上加入 `email/fetch-attachments`，打通“邮件 → 附件 → 导入”链路；账号与密码仍来自配置，不引入数据库 CRUD。
Phase 3 补齐账单 ZIP 常见的加密场景：`archive/extract` 解压、`file/detect` 定位，并加入运行目录保留策略与清理入口。
Phase 4 加入 `on.schedule`（Quartz cron，服务器本地时区）、步骤级失败重试与“从失败步骤恢复”的手动重试，并把页面刷新切换为 SignalR 实时事件（轮询仅作断线兜底）。
Phase 5 做安全收口：Secret 与密码类待办/步骤数据落盘加密、启动时自动回填历史明文、审计事件与数据库历史保留策略，并在 `/inbox` 提供安全状态与审计视图。

## 3. 总体架构

```
┌───────────────────────────────────────────────────────────┐
│ Blazor WASM Client                                        │
│  /inbox  待办输入 · 工作流触发 · 运行记录 · 步骤日志         │
└───────────────┬───────────────────────────────────────────┘
                │ REST (/api/inbox/*) + SignalR 通知
┌───────────────▼───────────────────────────────────────────┐
│ Server                                                    │
│                                                           │
│  InboxController                                          │
│    ├─ InboxWorkflowCatalog   (读取/解析 data/inboxes/*.yaml)│
│    ├─ InboxEmailAccounts     (Inbox:EmailAccounts 只读配置)  │
│    └─ IInboxWorkflowRunner   (创建运行、推进步骤)            │
│                                                           │
│  InboxWorkflowWorker (BackgroundService, 2s 轮询)          │
│    ├─ 恢复 WaitingInput 且待办已完成的运行                   │
│    ├─ 领取 Queued 运行，顺序执行 steps（含失败重试）         │
│    └─ 按计划运行 InboxWorkspaceCleaner（清理历史运行目录）    │
│                                                           │
│  InboxScheduleSynchronizer (BackgroundService, 30s 扫描)   │
│    └─ on.schedule → Quartz Job/Trigger（增删改自动同步）     │
│                                                           │
│  Security                                                 │
│    InboxDataProtection   (enc:v1: 敏感值加解密)             │
│    InboxSecrets          (Inbox:Secrets 透明解密)           │
│    InboxAuditLog         (运行/待办/清理审计事件)            │
│    InboxHistoryCleaner   (数据库历史保留与清理)              │
│                                                           │
│  Actions                                                  │
│    human/input   → 创建 InboxTodo + 通知 + 挂起             │
│    email/fetch-attachments → IMAP 检索 + 附件落盘（MailKit） │
│    archive/extract → ZIP 解压（密码、路径与大小限制）        │
│    file/detect   → 按扩展名定位文件                          │
│    bill/import   → 复用 IBillImportService                 │
│    bill/process  → 复用 IBillProcessingService             │
└───────────────┬───────────────────────────────────────────┘
                │
        SQLite (EF Core) + data/inbox-workspace/runs/{id}
```

设计上与现有 `PipelineJobWorker` 保持一致：**HTTP 只负责入队/查询，后台 Worker 轮询数据库执行**，避免长任务占用请求线程，也天然获得进程重启后的恢复能力。

## 4. YAML DSL

工作流文件存放于 `Inbox:WorkflowsPath`（默认 `data/inboxes`），每个文件一个工作流。

```yaml
name: 支付宝月度账单
description: 下载邮件附件，人工输入解压密码后导入

on:
  manual: true
  schedule: "0 0 9 1 * ?"   # Quartz cron：每月 1 日 09:00（服务器本地时区）

jobs:
  - name: import
    steps:
      - id: password
        name: 输入解压密码
        uses: human/input
        with:
          title: 支付宝账单解压密码
          prompt: 请输入本 ZIP 的解压密码（通常是身份证后六位）
          input_type: password
          expires_hours: 72

      - id: extract
        name: 解压附件
        uses: archive/extract
        with:
          source: attachments/*.zip
          password: ${{ steps.password.outputs.value }}
          target: extracted/

      - id: detect
        name: 定位账单文件
        uses: file/detect
        with:
          source: extracted/**/*
          extensions: csv,xlsx

      - id: import
        name: 导入账单
        uses: bill/import
        with:
          file: ${{ steps.detect.outputs.first_file }}
          platform: alipay
        retry:
          attempts: 3        # 最多执行 3 次（含首次）
          delay_seconds: 5   # 首次重试前等待 5 秒
          backoff: 2         # 后续等待翻倍：5s、10s

      - id: process
        name: 运行清洗管道
        uses: bill/process
        with:
          batch_id: ${{ steps.import.outputs.batch_id }}
```

Phase 2 可直接运行的邮件接入示例（`POST /api/inbox/workflows/sample` 也会生成同名模板）：

```yaml
name: 支付宝邮件账单
description: 从邮箱下载未读账单附件，确认后导入

on:
  manual: true

jobs:
  - name: import
    steps:
      - id: fetch
        name: 从邮箱获取附件
        uses: email/fetch-attachments
        with:
          account: inbox                 # Inbox:EmailAccounts 中的键名
          search: "subject:账单 since:60d unseen"
          attachments: "*.csv,*.xlsx"
          mark_seen: true
          limit: 10

      - id: confirm
        name: 确认导入
        uses: human/input
        with:
          title: 确认导入邮件账单
          prompt: 已下载附件，确认后继续导入。
          input_type: confirm

      - id: import
        name: 导入账单
        uses: bill/import
        with:
          file: attachments/*.csv
          platform: auto

      - id: process
        name: 运行清洗管道
        uses: bill/process
        with:
          batch_id: ${{ steps.import.outputs.batch_id }}
```

### 4.1 邮件检索语法（`search`）

空格分隔的多个条件是 AND 关系，含空格的文本用双引号包裹：

| 条件 | 说明 |
|------|------|
| `from:alipay` | 发件人包含文本 |
| `subject:"月度 账单"` | 主题包含文本 |
| `since:30d` / `since:2026-09-01` | 起始日期，支持 `Nd` / `Nw` / `Nm` / `today` / `yyyy-MM-dd` 等 |
| `before:2026-10-01` | 截止日期（不含），格式同上 |
| `unseen` | 仅未读（**默认**） |
| `all` | 包含已读邮件；不可与 `unseen` 同时出现 |

非法条件（未知键、缺少值、引号未闭合、日期越界、`since >= before`）会让步骤直接失败，并在日志中给出原因。

### 4.2 邮箱账号配置

账号是基础设施配置，通过 `Inbox:EmailAccounts` 提供（appsettings、环境变量或用户机密），不落数据库、不提供运行时 CRUD。密码优先通过 `PasswordSecret` 引用 `Inbox:Secrets`，避免把明文写在主配置里：

```json
{
  "Inbox": {
    "EmailAccounts": {
      "inbox": {
        "Host": "imap.example.com",
        "Port": 993,
        "Security": "ssl",
        "Username": "me@example.com",
        "PasswordSecret": "inbox-password",
        "Folder": "INBOX"
      }
    },
    "Secrets": {
      "inbox-password": "应用专用密码或授权码"
    }
  }
}
```

- `Security`：`ssl`（默认 993）、`starttls`、`starttls-when-available`、`auto`、`none`；未填写时按端口推断（143 → `starttls`，其他 → `ssl`）。
- 未配置 `PasswordSecret` 时也兼容直接写 `Password`；两者都缺失视为配置不完整。
- 配置在应用启动时加载，修改后需重启应用生效。
- `/api/inbox/email-accounts` 与 `/inbox` 只展示主机、端口、用户名、文件夹与密码来源，**永不返回密码**。

约定：

| 元素 | 说明 |
|------|------|
| `name` | 工作流唯一名称（必填） |
| `on.manual` | 是否允许手动触发，默认 `true` |
| `on.schedule` | Quartz cron（6/7 段），服务器本地时区；每 30 秒同步为定时任务 |
| `jobs[].name` | Job 名称，按声明顺序串行执行 |
| `steps[].id` | 步骤唯一标识（全工作流内唯一），用于 `steps.<id>` 引用 |
| `steps[].uses` | Action 名称，如 `bill/import` |
| `steps[].with` | Action 参数，值支持表达式 |
| `steps[].retry` | 失败重试：`attempts`（1–5）、`delay_seconds`（0–300）、`backoff`（1–10） |

表达式（`${{ ... }}`）当前支持：

- `${{ secrets.KEY }}` → `Inbox:Secrets:KEY` 配置；
- `${{ steps.<id>.outputs.<key> }}` → 前序步骤输出；
- `${{ steps.<id>.status }}` → 前序步骤状态；
- `${{ workspace }}` → 当前运行的工作目录（绝对路径）；
- `${{ run.id }}`；
- `${{ inputs.<key> }}` → 触发参数（预留给后续扩展）。

未知路径解析为空字符串并记入日志，与 GitHub Actions 的宽容行为一致。

### 4.3 定时触发与失败重试

定时触发：

- cron 使用 Quartz 格式（秒 分 时 日 月 周 [年]），按**服务器本地时区**计算；`on.schedule` 语法错误会作为工作流解析错误显示在列表/详情中，不影响其他工作流；
- `InboxScheduleSynchronizer` 启动 3 秒后首次扫描，之后每 `Inbox:ScheduleSyncSeconds`（默认 30 秒）对比目录与 Quartz 任务：新增 YAML、修改 cron、删除文件都会自动生效，无需重启；
- 定时触发的运行 `TriggerType=Schedule`、`TriggeredBy=schedule`，输入中带有 `trigger=schedule` 与 `scheduled_at`（本地时间 ISO 8601）；
- 若同名工作流已有 `Queued` / `Running` / `WaitingInput` 的运行，本次触发直接跳过并记录日志，避免重复下载/导入；需要“补跑”时手动触发即可；
- 定时任务与工作流名称一一对应；同名 YAML 只保留第一个并记录警告。

失败重试：

- 仅为**步骤返回 Failed 或抛出异常**时重试；`human/input` 挂起、运行取消不触发重试；
- 未配置 `retry` 时等价于 `attempts: 1`（不重试）；配置为空块时默认 `attempts=2`、`delay_seconds=5`、`backoff=2`；
- 第 n 次失败后的等待时间为 `delay_seconds × backoff^(n-1)`，上限 300 秒；等待期间 Worker 不阻塞其他运行（当前 Worker 串行，等待只阻塞当前步骤）；
- 每次失败都会写入步骤日志（`第 1/3 次尝试失败: ...；5 秒后重试`），最终失败时保留最后一次错误；
- 运行失败后可在 `/inbox` 或 `POST /api/inbox/runs/{id}/retry` 手动重试：从**第一个失败步骤**恢复，已完成步骤与运行目录产物保留；若失败步骤是 `human/input`（例如待办过期），过期待办会被移除并在恢复时重新创建。

### 4.4 敏感数据与 Secret 加密

落盘加密统一走 ASP.NET Core Data Protection（应用名 `MinGo.MyBillBook`，Purpose `MinGo.MyBillBook.Inbox.Sensitive.v1`），密文以 `enc:v1:` 前缀标识；密钥环保存在 `Inbox:DataProtectionKeysPath`（默认 `data/keys`）。读取历史明文时直接兼容，写入时幂等（已加密的值不会重复加密）。

覆盖范围：

| 位置 | 处理方式 |
|------|----------|
| `Inbox:Secrets:*` | 可用明文，也可写 `enc:v1:` 密文；使用时透明解密，单个 Secret 解密失败只记录日志并跳过 |
| `InboxTodo.InputValue` | `IsSensitive`（`input_type=password`）的待办加密存储；`human/input` 读回时解密 |
| `InboxRun.InputsJson` | 键名命中敏感片段（`password` / `passphrase` / `secret` / `token` / `apikey` / `api_key` / `credential` / `private_key`）的值加密 |
| `InboxStepRun.InputsJson` | 同上；解析表达式前先解密，写入已解析参数时再按敏感键加密 |
| `InboxStepRun.OutputsJson` | `human/input` 且待办敏感的步骤整体加密，避免密码出现在步骤输出中 |

- 应用启动时执行一次回填（`InboxSensitiveDataBackfill`）：扫描历史待办、运行参数、步骤输入输出，把尚未加密的敏感值就地加密；日志只输出各表处理条数，不输出值；
- 回填跳过表达式（`${{ ... }}`）与已加密值，可重复执行；
- 生成密文可离线完成，新机器首次配置时用 CLI 输出后写入配置：

```bash
dotnet run --project src/MinGo.MyBillBook -- --protect-secret "<应用专用密码>"
# enc:v1:CfDJ8...
```

- **备份要求**：`data/keys` 与数据库必须一起备份/迁移，丢失密钥环后已加密的 Secret、待办输入与步骤数据将无法解密；
- API 与日志永不返回 Secret 或敏感值；运行详情中密码类待办与 `human/input` 输出统一掩码为 `******`。

### 4.5 审计事件

`InboxAuditEvents` 表记录关键操作，只保存操作类型、对象 ID、触发者与不含敏感值的说明：

| 事件 | 触发点 |
|------|--------|
| `RunCreated` | 手动/定时创建运行（Detail 含触发方式与步骤数） |
| `RunCompleted` / `RunFailed` / `RunCancelled` / `RunRetried` | 运行终态、取消与手动重试 |
| `TodoCompleted` | 提交待办（Detail 含输入类型与是否加密存储） |
| `WorkspaceCleanup` / `HistoryCleanup` | 运行目录清理与数据库历史清理 |

审计写入失败不会影响工作流执行（仅记录日志）；`GET /api/inbox/audit` 按时间倒序返回最近事件，`/inbox` 的“安全与审计”卡片展示最近 20 条。

## 5. 领域模型

```csharp
InboxRun            // 一次运行；保存 WorkflowYaml 快照，避免运行中改文件导致语义漂移
  Id, WorkflowName, WorkflowYaml, TriggerType(Manual/Schedule), TriggeredBy,
  InputsJson, Status(Queued/Running/WaitingInput/Completed/Failed/Cancelled),
  CreatedAt, StartedAt, CompletedAt, Error
  Steps[]

InboxStepRun        // 步骤级追踪
  Id, RunId, JobName, StepId, StepName, Action, Order,
  Status(Pending/Running/WaitingInput/Completed/Failed/Skipped),
  InputsJson(已解析参数), OutputsJson, Log, Error,
  StartedAt, CompletedAt

InboxTodo           // 人工介入待办
  Id, RunId, StepRunId, Title, Prompt, InputType(text/password/confirm),
  Placeholder, IsSensitive, Status(Pending/Completed/Expired),
  InputValue, CreatedAt, CompletedAt, ExpiresAt

InboxAuditEvent     // 安全审计
  Id, OccurredAt, EventType, RunId?, TodoId?, WorkflowName?, Actor?, Detail?
```

`InputsJson` / `OutputsJson` / `InputValue` 中的敏感值以 `enc:v1:` 密文存储（见 4.4），序列化边界之外统一为明文；`InboxAuditEvent` 不保存任何密码或待办输入值。

「待办驱动」的关键点：`human/input` 不在内存里阻塞等待。它创建 `InboxTodo` 后直接返回 `WaitingInput`，Worker 结束本轮。用户提交待办后只改数据库；Worker 下一次轮询发现待办已完成，就把步骤重置为 `Pending`、运行重置为 `Queued`，重新执行该步骤（Action 检测到待办已完成，直接读取值并返回输出），随后继续后续步骤。

状态机：

```
Run:   Queued → Running → WaitingInput → Running → … → Completed
                   └──────────── Failed / Cancelled

Step:  Pending → Running → WaitingInput → Pending → Running → Completed
                    └──────────── Failed

Todo:  Pending → Completed
           └──→ Expired（超时）
```

## 6. 内置 Action

| Action | 参数 | 输出 | 说明 |
|--------|------|------|------|
| `human/input` | `title`、`prompt`、`input_type`、`placeholder`、`expires_hours` | `value` | 创建待办并挂起；恢复时读取已完成待办 |
| `email/fetch-attachments` | `account`（必填）、`search`、`folder`、`download_dir`、`attachments`、`mark_seen`、`include_inline`、`limit`、`max_file_mb` | `count`、`messages`、`files`（JSON 数组）、`first_file`、`first_name`、`folder` | 通过 MailKit 连接 IMAP，检索邮件并把附件写入运行目录（默认 `attachments/`） |
| `archive/extract` | `source`（必填，zip glob）、`target`、`password`、`flatten`、`max_file_mb`、`max_total_mb`、`max_entries` | `count`、`files`（JSON 数组）、`first_file`、`first_name`、`target`、`bytes`、`warnings` | 解压 ZIP（支持 ZipCrypto 与 AES），默认扁平化到 `extracted/` |
| `file/detect` | `source`（必填，glob）、`extensions`、`required` | `count`、`files`（JSON 数组）、`first_file`、`first_name`、`extensions` | 按扩展名从工作目录中筛选文件，供导入步骤定位 |
| `bill/import` | `file`（相对 workspace，支持通配符）、`platform`（`alipay` / `wechat` / `auto`） | `batch_id`、`total`、`success`、`updated`、`duplicate` | 调用现有 `IBillImportService`，原始文件进入对象存储 |
| `bill/process` | `batch_id` | `processed` | 调用现有 `IBillProcessingService`，运行 9 步清洗管道 |

`email/fetch-attachments` 的行为约定：

- 默认只取未读邮件，按 UID 从新到旧处理，`limit` 默认 10（上限 100）；
- `attachments` 为逗号/分号分隔的通配符列表（匹配文件名），为空表示全部；内联图片默认跳过；
- 附件名经 `WorkspaceFileName.Sanitize` 清洗，重名自动追加 `-2`、`-3` 后缀；
- `max_file_mb` 默认 50，超限文件删除并记录日志；
- 至少成功保存一个附件且 `mark_seen: true` 时，把对应邮件标记为已读；单封邮件或单个附件失败不会中断整个步骤；
- 只有“没有任何附件保存成功且发生过下载错误”时步骤才失败；否则以 `count=0` 完成，便于由后续人工步骤判断。

`archive/extract` 的行为约定：

- `source` 支持 glob（如 `attachments/*.zip`），可一次解压多个压缩包；
- `password` 通常来自前序 `human/input`，也可用 `${{ secrets.KEY }}`；日志只记录“已提供/无”，**不输出密码**；
- 默认 `flatten: true`，只保留文件名；设为 `false` 时保留目录结构，含 `.`/`..` 的路径片段直接跳过；
- 每个条目都做目标目录包含性校验（防 zip-slip），并限制单文件 `max_file_mb`（默认 200）、总量 `max_total_mb`（默认 500）与条目数 `max_entries`（默认 500）；
- 重名文件自动追加 `-2`、`-3` 后缀；单个条目失败只记入 `warnings`，不中断其他文件；
- 缺密码或密码错误时失败，并给出“密码错误或加密方式不支持”等可操作提示；只有所有压缩包都没有解出文件时步骤才失败。

`file/detect` 的行为约定：

- `extensions` 为逗号分隔的扩展名列表（默认 `csv,xlsx`），大小写不敏感；
- `required: true`（默认）时找不到匹配文件即失败，避免导入步骤拿到空输入；
- 输出 `files` JSON 数组与 `first_file`，后者适合直接传给 `bill/import`。

失败重试由运行器统一处理，Action 无需感知：抛出异常与返回 `Failed` 走同一套重试逻辑，重试耗尽后才把运行标记为失败。

Action 抽象：

```csharp
public interface IInboxAction
{
    string Uses { get; }
    Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct);
}

public enum InboxActionOutcome { Completed, WaitingInput, Failed }
```

新增 Action = 实现接口 + DI 注册，无需改引擎。

## 7. API

| 方法 | 路径 | 说明 |
|------|------|------|
| GET | `/api/inbox/workflows` | 工作流列表（含解析错误） |
| GET | `/api/inbox/workflows/{name}` | 工作流详情 + 原始 YAML |
| POST | `/api/inbox/workflows/{name}/runs` | 手动触发；可 multipart 上传种子文件到 `input/`；202 返回 runId |
| POST | `/api/inbox/workflows/sample` | 创建演示工作流文件（手动导入 + 邮件导入 + 加密 ZIP 导入，已存在则不覆盖） |
| GET | `/api/inbox/email-accounts` | 邮箱账号只读列表（含配置状态与错误，密码不返回） |
| GET | `/api/inbox/workspace` | 运行目录概况：数量、占用、活动运行数、保留策略 |
| POST | `/api/inbox/workspace/cleanup?dryRun=` | 按保留策略清理运行目录；`dryRun=true` 仅预览；活动运行目录始终保留 |
| GET | `/api/inbox/runs` | 运行记录（状态/工作流筛选 + 分页） |
| GET | `/api/inbox/runs/{id}` | 运行详情：步骤输入输出、日志、关联待办 |
| POST | `/api/inbox/runs/{id}/cancel` | 取消 Queued/WaitingInput 运行，相关待办置 Expired |
| POST | `/api/inbox/runs/{id}/retry` | 从首个失败步骤恢复执行；仅 Failed 运行可用，已完成步骤与工作目录保留 |
| GET | `/api/inbox/todos` | 待办列表（默认 Pending） |
| POST | `/api/inbox/todos/{id}/complete` | 提交待办输入；敏感值加密后落盘 |
| GET | `/api/inbox/security` | 安全状态：加密可用性、密钥数量、Secret 加密统计、敏感待办、历史保留与审计数量 |
| GET | `/api/inbox/audit?take=&eventType=` | 最近审计事件（`take` 1–200，默认 50） |
| POST | `/api/inbox/history/cleanup?dryRun=` | 按 `HistoryRetentionDays` 清理已完成/失败/取消的运行、步骤、待办与审计事件；`dryRun=true` 仅预览 |
| GET | `/api/inbox/health` | 引擎自检（工作流目录、邮箱账号数量、保留策略、待处理数、活动运行数） |

敏感值处理：`input_type=password` 的待办在 API 返回中掩码为 `******`，步骤输出中的密码同样掩码；数据库以 Data Protection 密文保存（`enc:v1:`），仅在工作流执行时于内存中解密。历史明文由启动回填自动加密。

## 8. UI（/inbox）

- **待办事项**：页面顶部展示 Pending 待办，直接内联输入并提交（密码框 / 文本框）；提交后提示“已提交，工作流将在数秒内继续”。
- **邮箱连接**：只读卡片展示 `Inbox:EmailAccounts` 中的账号（可用 / 配置不完整、主机端口、文件夹、密码来源）；未配置时给出 JSON 示例。
- **工作目录**：展示运行目录数量、占用空间、活动运行与保留策略；提供“预览清理”（dry run）与“立即清理”（二次确认）。清理活动运行目录会被跳过。
- **安全与审计**：展示加密是否可用、Secret 总数与已加密数、待加密的敏感待办、数据库历史保留天数、密钥目录与审计事件数；提供“预览清理历史”与“立即清理历史”（二次确认，说明只清理已完成/失败/取消的运行及其步骤、待办和审计事件），并列出最近 20 条审计事件。
- **工作流**：卡片列表，展示描述、触发方式（手动 / `on.schedule` cron）、步骤链；点击“运行”弹出确认框，可附加一个种子文件（Phase 1 演示用）。
- **运行记录**：表格 + 分页；状态徽标；展开显示每个步骤的状态、耗时、错误与日志；失败运行提供“重试”（二次确认，说明从失败步骤继续）；定时触发的运行在详情标题中显示 `Schedule`。
- 通知：服务端在运行状态变化、待办创建时通过 SignalR 广播（`InboxRunUpdated` / `InboxTodoCreated`）；`/inbox` 订阅这两个事件并立即刷新待办、运行列表与展开中的详情，同时弹出简短提示。断线时自动重连，并以 10 秒轮询兜底；SignalR 连接失败不影响页面其他功能。

## 9. 工作目录

```
data/inboxes/                     # YAML 定义（运行时可编辑）
data/inbox-workspace/runs/{id}/   # 每次运行的隔离目录
    input/                        # 触发时上传的种子文件（Phase 1）
    attachments/                  # 邮件附件（可由 download_dir 改名）
    extracted/                    # archive/extract 解压结果（可由 target 改名）
```

文件访问通过 `InboxWorkspace` 统一做规范化与防路径穿越校验；从邮件附件名、上传文件名或 ZIP 条目名进入磁盘前，还会经过 `WorkspaceFileName.Sanitize`（剥离路径、替换非法字符、去掉结尾点/空格）。解压还会对最终路径做目标根目录包含性检查，拒绝 `../` 越界条目。

### 9.1 保留与清理

```json
{
  "Inbox": {
    "WorkspaceRetentionDays": 30,   // 0 表示不按天数清理
    "WorkspaceMaxRuns": 200,        // 0 表示不按数量清理
    "CleanupIntervalHours": 6,      // 0 表示关闭自动清理
    "ScheduleSyncSeconds": 30,      // 定时任务目录扫描间隔，最小 5 秒
    "HistoryRetentionDays": 180,    // 数据库历史保留天数，0 表示不清理
    "DataProtectionKeysPath": "data/keys"  // 加密密钥环目录，需与数据库一起备份
  }
}
```

- `InboxWorkflowWorker` 启动时先清理一次，之后每 `CleanupIntervalHours` 小时执行一次；
- 处于 `Queued` / `Running` / `WaitingInput` 的运行目录永远保留；
- 删除判定：目录最后写入时间早于 `UtcNow - WorkspaceRetentionDays`，或在非活动目录中超出最新 `WorkspaceMaxRuns` 个；
- 单个目录删除失败只记录日志，不阻塞 Worker；
- `/inbox` 的“预览清理”调用 `dryRun=true` 展示将删除的数量与占用，“立即清理”二次确认后执行；
- 清理只删运行目录，不动数据库中的运行记录与导入批次，二者仍可在运行记录与账单明细中查看。

### 9.2 数据库历史保留

- `Inbox:HistoryRetentionDays`（默认 180）：Worker 每个清理周期删除 `Completed` / `Failed` / `Cancelled` 且 `CompletedAt ?? CreatedAt` 早于截止时间的运行，及其步骤、待办和超过截止时间的审计事件；`0` 表示关闭；
- 活动运行（`Queued` / `Running` / `WaitingInput`）与其待办永不删除；
- 清理按待办 → 步骤 → 运行 → 审计事件逐表 `ExecuteDeleteAsync`，避免外键残留；批量删除失败只记录日志，下个周期重试；
- 手动入口：`POST /api/inbox/history/cleanup?dryRun=true` 预览数量，真实执行会写入 `HistoryCleanup` 审计事件；
- 与 9.1 的区别：9.1 清理的是**磁盘运行目录**，历史保留清理的是**数据库记录**；二者互不影响，可分别配置。

## 10. 分阶段计划

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 1 | YAML 解析、表达式、运行器、待办、Worker、`human/input`、`bill/import`、`bill/process`、API、UI、单测 | ✅ |
| Phase 2 | `email/fetch-attachments`（MailKit IMAP）、邮箱连接配置、检索语法、只读账号 UI | ✅ |
| Phase 3 | `archive/extract`（加密 ZIP）、`file/detect`（glob）、工作目录保留策略与清理 API/UI | ✅ |
| Phase 4 | Quartz `schedule` 触发、SignalR 前端实时订阅、失败重试策略 | ✅ |
| Phase 5 | 安全增强：Secret 与敏感待办/步骤数据落盘加密、启动回填、审计事件、数据库历史保留与安全状态 UI | ✅ |

Secret 加密（Phase 2 原计划项）已在 Phase 5 完成：`PasswordSecret` 仍作为配置间接引用，但 `Inbox:Secrets` 中的值可用 CLI 生成 `enc:v1:` 密文；账号只读展示与 API 掩码保持不变，明文配置继续兼容。

## 11. 验收标准

Phase 1：

1. `data/inboxes/` 下放置合法 YAML 后，`/inbox` 能看到工作流，`/api/inbox/workflows` 返回解析结果；
2. 非法 YAML 在列表/详情中显示明确错误，不影响其他工作流；
3. 手动触发后 run 进入 `Queued`，Worker 自动执行；遇到 `human/input` 时变为 `WaitingInput` 并生成待办，进程重启后状态仍保留；
4. 待办提交后 3 秒内运行恢复并完成；密码类输入在 API 中不回显；
5. `bill/import` 产生的新批次与在“导入对账单”页面手工上传的批次走同一套表与管道，`bill/process` 后可在账单明细中看到数据；
6. 步骤级日志、输入输出、错误信息可在运行详情中查看；
7. `dotnet build` 与 `dotnet test` 通过。

Phase 2：

8. 在 `Inbox:EmailAccounts` 配置账号后，`/api/inbox/email-accounts` 与 `/inbox` 能看到账号状态，且任何接口都不返回密码；
9. `email/fetch-attachments` 能按 `search` / `attachments` 条件检索邮件并把附件写入 `attachments/`，输出 `files` 供后续步骤使用；
10. `mark_seen: true` 时重复运行不会再次命中同一封已读邮件；`limit`、`max_file_mb`、`include_inline` 生效；
11. 账号缺失、检索语法错误、IMAP 连接/认证失败都会让步骤失败并给出可操作的错误信息；
12. 使用本地 IMAP 服务（GreenMail）完成“取附件 → 人工确认 → 导入 → 清洗”端到端验证；`dotnet test` 覆盖检索语法、附件过滤与文件名清洗。

Phase 3：

13. 上传带密码的 ZIP 并输入正确密码后，`archive/extract` 解压出 CSV，`file/detect` 定位文件，`bill/import` + `bill/process` 完成导入与清洗；
14. 密码错误时步骤失败并提示“密码错误或加密方式不支持”，不会留下半解压文件；缺密码时提示通过 `human/input` 提供 password；
15. 解压具备防 zip-slip、单文件/总量/条目数限制与重名后缀；日志与输出中**不出现密码**；
16. `GET /api/inbox/workspace` 返回目录数量、占用、活动运行与保留策略；`POST .../cleanup?dryRun=true` 只预览，真实清理会删除过期目录；
17. 清理时 `Queued` / `Running` / `WaitingInput` 运行的目录不会被删除；`/inbox` 可预览并二次确认清理；
18. `dotnet build` 与 `dotnet test` 通过，新增解压单测覆盖明文、ZipCrypto、AES、错误密码、路径穿越、重名、限额与缺文件。

Phase 4：

19. 带 `on.schedule` 的 YAML 在启动后（或修改后 30 秒内）被同步为 Quartz 任务；修改 cron、删除文件会同步更新/移除任务，无需重启；
20. 定时触发创建 `TriggerType=Schedule` 的运行并正常推进；同名工作流已有活动运行时跳过本次触发，不会重复创建运行；
21. 非法 cron 在工作流列表/详情中显示明确错误，且不会注册到 Quartz；
22. 配置 `retry` 的步骤失败后按 `attempts` / `delay_seconds` / `backoff` 重试，日志记录每次失败与等待时间；耗尽后运行失败；
23. `POST /api/inbox/runs/{id}/retry` 从失败步骤恢复，已完成步骤不重复执行、运行目录文件保留；补齐缺失输入后可重试成功；
24. `/inbox` 通过 SignalR 在 1 秒级刷新运行状态与待办并提示，断线后自动重连；10 秒轮询可兜底；`dotnet build` 与 `dotnet test` 通过。

Phase 5：

25. `input_type=password` 的待办输入、运行参数与步骤输入中命中敏感键的值，以及密码类 `human/input` 的步骤输出，均以 `enc:v1:` 密文落盘；API 仍只返回 `******`；
26. 应用启动时自动回填历史明文（待办、运行参数、步骤输入输出），回填幂等、跳过表达式，日志不包含任何敏感值；全新克隆数据库后旧运行仍可正常重试与继续；
27. `--protect-secret` 能输出可直接写入 `Inbox:Secrets` 的密文；`PasswordSecret` / `Password` 指向密文时邮箱账号显示可用，密码来源正确且接口不返回密码；
28. 丢失或更换 `data/keys` 时，加密探针报告不可用，敏感数据解密给出明确错误而不是静默失败；
29. 运行创建/完成/失败/取消/重试、待办提交、工作目录清理与历史清理都会产生审计事件，事件中不含密码或待办输入值；
30. `Inbox:HistoryRetentionDays` 到期后自动删除终态运行及其步骤/待办/审计事件，活动运行不受影响；`POST /api/inbox/history/cleanup?dryRun=true` 只预览，真实清理写入 `HistoryCleanup` 审计；`/inbox` 可预览并二次确认；
31. `GET /api/inbox/security` 返回加密状态、密钥环路径、Secret 加密统计、敏感待办统计、历史保留与审计数量；`dotnet build` 与 `dotnet test` 通过（当前 120 个测试）。
