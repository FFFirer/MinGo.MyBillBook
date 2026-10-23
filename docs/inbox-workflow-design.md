# Inbox：邮件驱动的账单数据接入工作流

> 状态：Phase 1（手动触发 + 待办介入 + 标准导入衔接）与 Phase 2（IMAP 取附件）已实现。
> 本文档描述整体设计；Phase 3+（加密解压、定时调度、安全增强）按文末计划推进。

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

- 不做加密 ZIP 解压与文件识别（`archive/extract`、`file/detect`，Phase 3）；
- 不做 Quartz 定时触发（Phase 4）；
- 不做 Secret 落盘加密与金库管理（Phase 5）；Phase 2 采用配置引用 + 只读展示 + API 掩码；
- 不做多 Job 并行、`needs` 依赖图、条件表达式（`if`）、矩阵构建；
- 不做多用户权限体系（当前应用为单用户本地部署）。

Phase 1 交付的是**可运行的引擎骨架**：手动触发 → 可选上传种子文件 → `human/input` 生成待办并挂起 → 用户完成待办 → 自动恢复 → `bill/import` → `bill/process` → 完成。
Phase 2 在此基础上加入 `email/fetch-attachments`，打通“邮件 → 附件 → 导入”链路；账号与密码仍来自配置，不引入数据库 CRUD。

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
│    └─ 领取 Queued 运行，顺序执行 steps                      │
│                                                           │
│  Actions                                                  │
│    human/input   → 创建 InboxTodo + 通知 + 挂起             │
│    email/fetch-attachments → IMAP 检索 + 附件落盘（MailKit） │
│    bill/import   → 复用 IBillImportService                 │
│    bill/process  → 复用 IBillProcessingService             │
│    (Phase 3) archive/extract, file/detect                 │
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
  # schedule: "0 0 9 1 * ?"   # Quartz cron，Phase 4 生效

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
        uses: archive/extract            # Phase 3
        with:
          source: attachments/*.zip
          password: ${{ steps.password.outputs.value }}
          target: extracted/

      - id: import
        name: 导入账单
        uses: bill/import
        with:
          file: extracted/*.csv
          platform: alipay

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
| `on.schedule` | Quartz cron，Phase 4 生效 |
| `jobs[].name` | Job 名称，按声明顺序串行执行 |
| `steps[].id` | 步骤唯一标识（全工作流内唯一），用于 `steps.<id>` 引用 |
| `steps[].uses` | Action 名称，如 `bill/import` |
| `steps[].with` | Action 参数，值支持表达式 |

表达式（`${{ ... }}`）当前支持：

- `${{ secrets.KEY }}` → `Inbox:Secrets:KEY` 配置；
- `${{ steps.<id>.outputs.<key> }}` → 前序步骤输出；
- `${{ steps.<id>.status }}` → 前序步骤状态；
- `${{ workspace }}` → 当前运行的工作目录（绝对路径）；
- `${{ run.id }}`；
- `${{ inputs.<key> }}` → 触发参数（预留给后续扩展）。

未知路径解析为空字符串并记入日志，与 GitHub Actions 的宽容行为一致。

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
```

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
| `bill/import` | `file`（相对 workspace，支持通配符）、`platform`（`alipay` / `wechat` / `auto`） | `batch_id`、`total`、`success`、`updated`、`duplicate` | 调用现有 `IBillImportService`，原始文件进入对象存储 |
| `bill/process` | `batch_id` | `processed` | 调用现有 `IBillProcessingService`，运行 9 步清洗管道 |

`email/fetch-attachments` 的行为约定：

- 默认只取未读邮件，按 UID 从新到旧处理，`limit` 默认 10（上限 100）；
- `attachments` 为逗号/分号分隔的通配符列表（匹配文件名），为空表示全部；内联图片默认跳过；
- 附件名经 `WorkspaceFileName.Sanitize` 清洗，重名自动追加 `-2`、`-3` 后缀；
- `max_file_mb` 默认 50，超限文件删除并记录日志；
- 至少成功保存一个附件且 `mark_seen: true` 时，把对应邮件标记为已读；单封邮件或单个附件失败不会中断整个步骤；
- 只有“没有任何附件保存成功且发生过下载错误”时步骤才失败；否则以 `count=0` 完成，便于由后续人工步骤判断。

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
| POST | `/api/inbox/workflows/sample` | 创建演示工作流文件（手动导入 + 邮件导入，已存在则不覆盖） |
| GET | `/api/inbox/email-accounts` | 邮箱账号只读列表（含配置状态与错误，密码不返回） |
| GET | `/api/inbox/runs` | 运行记录（状态/工作流筛选 + 分页） |
| GET | `/api/inbox/runs/{id}` | 运行详情：步骤输入输出、日志、关联待办 |
| POST | `/api/inbox/runs/{id}/cancel` | 取消 Queued/WaitingInput 运行，相关待办置 Expired |
| GET | `/api/inbox/todos` | 待办列表（默认 Pending） |
| POST | `/api/inbox/todos/{id}/complete` | 提交待办输入 |
| GET | `/api/inbox/health` | 引擎自检（工作流目录、邮箱账号数量、待处理数） |

敏感值处理：`input_type=password` 的待办在 API 返回中掩码为 `******`；数据库中保留原值供后续步骤使用（本地单用户部署；后续可接入加密存储）。

## 8. UI（/inbox）

- **待办事项**：页面顶部展示 Pending 待办，直接内联输入并提交（密码框 / 文本框）；提交后提示“已提交，工作流将在数秒内继续”。
- **邮箱连接**：只读卡片展示 `Inbox:EmailAccounts` 中的账号（可用 / 配置不完整、主机端口、文件夹、密码来源）；未配置时给出 JSON 示例。
- **工作流**：卡片列表，展示描述、触发方式、步骤链；点击“运行”弹出确认框，可附加一个种子文件（Phase 1 演示用），提交后轮询运行状态。
- **运行记录**：表格 + 分页；状态徽标；展开显示每个步骤的状态、耗时、错误与日志；运行中每 3 秒自动刷新。
- 通知：服务端在运行状态变化、待办创建时通过 SignalR 广播（`InboxRunUpdated` / `InboxTodoCreated`）；页面同时使用轮询兜底。

## 9. 工作目录

```
data/inboxes/                     # YAML 定义（运行时可编辑）
data/inbox-workspace/runs/{id}/   # 每次运行的隔离目录
    input/                        # 触发时上传的种子文件（Phase 1）
    attachments/                  # 邮件附件（Phase 2，可由 download_dir 改名）
    extracted/                    # 解压结果（Phase 3）
```

文件访问通过 `InboxWorkspace` 统一做规范化与防路径穿越校验；从邮件附件名或上传文件名进入磁盘前，还会经过 `WorkspaceFileName.Sanitize`（剥离路径、替换非法字符、截断超长名）。运行目录当前不自动清理，后续可加保留策略。

## 10. 分阶段计划

| 阶段 | 内容 | 状态 |
|------|------|------|
| Phase 1 | YAML 解析、表达式、运行器、待办、Worker、`human/input`、`bill/import`、`bill/process`、API、UI、单测 | ✅ |
| Phase 2 | `email/fetch-attachments`（MailKit IMAP）、邮箱连接配置、检索语法、只读账号 UI | ✅ |
| Phase 3 | `archive/extract`（加密 ZIP）、`file/detect`（glob）、工作目录清理 | 待办 |
| Phase 4 | Quartz `schedule` 触发、SignalR 前端实时订阅、失败重试策略 | 待办 |
| Phase 5 | 安全增强：Secret 加密存储、敏感待办加密、审计与保留策略 | 待办 |

Secret 加密（Phase 2 原计划项）已拆分到 Phase 5：本阶段先实现 `PasswordSecret` 间接引用、账号只读展示与 API 掩码，避免把明文密码暴露给前端；落盘加密与密钥管理在 Phase 5 统一处理。

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
