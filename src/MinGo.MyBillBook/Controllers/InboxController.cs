using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.DTOs;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Services.Inbox;
using MinGo.MyBillBook.Services.Inbox.Email;
using MinGo.MyBillBook.Services.Inbox.Security;

namespace MinGo.MyBillBook.Controllers;

/// <summary>
/// Inbox 工作流 API：工作流定义（YAML）、手动触发、运行记录、人工待办。
/// 触发只入队并立即返回，实际执行由 InboxWorkflowWorker 在后台推进。
/// </summary>
[ApiController]
[Route("api/inbox")]
public class InboxController(
    InboxWorkflowCatalog catalog,
    InboxEmailAccounts emailAccounts,
    InboxWorkspaceCleaner workspaceCleaner,
    InboxHistoryCleaner historyCleaner,
    InboxSecurityStatusProvider securityStatus,
    IInboxSensitiveDataProtector protector,
    InboxAuditLog audit,
    IInboxWorkflowRunner runner,
    AppDbContext db,
    ILogger<InboxController> logger) : ControllerBase
{
    private static readonly JsonSerializerOptions IndentedJson = new() { WriteIndented = true };
    private const string MaskedValue = "******";

    // ---------- 工作流定义 ----------

    [HttpGet("workflows")]
    public ActionResult<List<InboxWorkflowDto>> GetWorkflows()
    {
        var files = catalog.LoadAll();
        var result = files.Select(f => new InboxWorkflowDto(
            f.Name,
            f.FileName,
            f.Definition?.Description,
            f.Definition?.On.Manual ?? false,
            f.Definition?.On.Schedule,
            f.Definition is null ? 0 : WorkflowYamlParser.FlattenSteps(f.Definition).Count,
            f.Error)).ToList();

        return Ok(result);
    }

    [HttpGet("workflows/{name}")]
    public ActionResult<InboxWorkflowDetailDto> GetWorkflow(string name)
    {
        var file = catalog.Find(name);
        if (file is null)
            return NotFound(new { message = $"工作流不存在: {name}" });

        var steps = file.Definition is null
            ? []
            : file.Definition.Jobs
                .SelectMany(j => j.Steps.Select(s => new InboxWorkflowStepDto(
                    j.Name, s.Id, s.Name ?? s.Id, s.Uses, s.With)))
                .ToList();

        return Ok(new InboxWorkflowDetailDto(
            file.Name,
            file.FileName,
            file.Definition?.Description,
            file.Definition?.On.Manual ?? false,
            file.Definition?.On.Schedule,
            file.Yaml,
            steps,
            file.Error));
    }

    /// <summary>创建演示工作流文件（已存在则不覆盖）。</summary>
    [HttpPost("workflows/sample")]
    public ActionResult CreateSampleWorkflows()
    {
        var files = catalog.CreateSampleWorkflows();
        return Ok(new { files, workflowsPath = catalog.WorkflowsPath });
    }

    // ---------- 邮箱连接（只读） ----------

    /// <summary>列出配置中的 IMAP 账号（绝不返回密码）。</summary>
    [HttpGet("email-accounts")]
    public ActionResult<List<InboxEmailAccountDto>> GetEmailAccounts()
    {
        var result = emailAccounts.List()
            .Select(a => new InboxEmailAccountDto(
                a.Name, a.Host, a.Port, a.Security, a.Username, a.Folder,
                a.Configured, a.HasPassword, a.PasswordSource, a.Error))
            .ToList();
        return Ok(result);
    }

    /// <summary>手动触发一次运行；可选上传一个种子文件到运行目录 input/。</summary>
    [HttpPost("workflows/{name}/runs")]
    [RequestSizeLimit(50 * 1024 * 1024)]
    public async Task<ActionResult> TriggerRun(string name, CancellationToken ct)
    {
        var file = catalog.Find(name);
        if (file is null)
            return NotFound(new { message = $"工作流不存在: {name}" });
        if (file.Definition is null)
            return BadRequest(new { message = $"工作流解析失败: {file.Error}" });
        if (!file.Definition.On.Manual)
            return Conflict(new { message = "该工作流不允许手动触发" });

        IFormFile? upload = null;
        if (Request.HasFormContentType)
        {
            var form = await Request.ReadFormAsync(ct);
            upload = form.Files.FirstOrDefault();
        }

        Stream? stream = null;
        try
        {
            if (upload is { Length: > 0 })
                stream = upload.OpenReadStream();

            var run = await runner.CreateRunAsync(
                file.Definition,
                file.Yaml,
                InboxTriggerType.Manual,
                "manual",
                inputs: null,
                stream,
                upload?.FileName,
                ct);

            logger.LogInformation("手动触发工作流 {Workflow}，运行 #{RunId}", file.Name, run.Id);
            return Accepted(new { runId = run.Id, workflow = run.WorkflowName });
        }
        finally
        {
            if (stream is not null)
                await stream.DisposeAsync();
        }
    }

    // ---------- 运行记录 ----------

    [HttpGet("runs")]
    public async Task<ActionResult<PagedResult<InboxRunSummaryDto>>> GetRuns(
        [FromQuery] string? status = null,
        [FromQuery] string? workflow = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        page = page < 1 ? 1 : page;
        pageSize = pageSize is < 1 or > 100 ? 20 : pageSize;

        var query = db.InboxRuns.AsNoTracking().AsQueryable();

        if (ParseEnum<InboxRunStatus>(status) is { } runStatus)
            query = query.Where(r => r.Status == runStatus);
        if (!string.IsNullOrWhiteSpace(workflow))
            query = query.Where(r => r.WorkflowName == workflow);

        var totalCount = await query.CountAsync(ct);
        var runs = await query
            .Include(r => r.Steps)
            .OrderByDescending(r => r.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var items = runs.Select(r => new InboxRunSummaryDto(
            r.Id,
            r.WorkflowName,
            r.Status.ToString(),
            r.TriggerType.ToString(),
            r.TriggeredBy,
            r.CreatedAt,
            r.StartedAt,
            r.CompletedAt,
            r.Error,
            r.Steps.OrderBy(s => s.Order)
                .Select(s => new InboxStepSummaryDto(
                    s.StepId, s.StepName, s.Action, s.Status.ToString(),
                    s.StartedAt, s.CompletedAt, s.Error))
                .ToList())).ToList();

        return Ok(new PagedResult<InboxRunSummaryDto>(items, totalCount, page, pageSize));
    }

    [HttpGet("runs/{id:int}")]
    public async Task<ActionResult<InboxRunDetailDto>> GetRun(int id, CancellationToken ct)
    {
        var run = await db.InboxRuns.AsNoTracking()
            .Include(r => r.Steps)
            .FirstOrDefaultAsync(r => r.Id == id, ct);
        if (run is null)
            return NotFound(new { message = $"运行不存在: {id}" });

        var todos = await db.InboxTodos.AsNoTracking()
            .Where(t => t.RunId == id)
            .OrderBy(t => t.Id)
            .ToListAsync(ct);

        var todoByStep = todos.ToDictionary(t => t.StepRunId);
        var steps = run.Steps.OrderBy(s => s.Order).Select(s =>
        {
            todoByStep.TryGetValue(s.Id, out var todo);
            var outputs = PrepareStepOutputs(s);

            return new InboxStepDetailDto(
                s.Id, s.JobName, s.StepId, s.StepName, s.Action, s.Status.ToString(),
                MaskSensitiveInputs(s.InputsJson), outputs, s.Log, s.Error,
                s.StartedAt, s.CompletedAt, todo?.Id, todo?.Status.ToString());
        }).ToList();

        return Ok(new InboxRunDetailDto(
            run.Id, run.WorkflowName, run.Status.ToString(), run.TriggerType.ToString(),
            run.TriggeredBy, run.CreatedAt, run.StartedAt, run.CompletedAt, run.Error,
            run.WorkflowYaml, steps, todos.Select(t => ToTodoDto(t, run)).ToList()));
    }

    [HttpPost("runs/{id:int}/cancel")]
    public async Task<ActionResult> CancelRun(int id, CancellationToken ct)
    {
        var cancelled = await runner.CancelRunAsync(id, ct);
        return cancelled
            ? Accepted(new { cancelled = id })
            : Conflict(new { message = "运行不存在或已结束" });
    }

    /// <summary>重试失败的运行：从失败步骤恢复，保留已完成步骤与运行目录。</summary>
    [HttpPost("runs/{id:int}/retry")]
    public async Task<ActionResult> RetryRun(int id, CancellationToken ct)
    {
        var retried = await runner.RetryRunAsync(id, ct);
        return retried
            ? Accepted(new { retried = id })
            : Conflict(new { message = "只有包含失败步骤的运行可以重试" });
    }

    // ---------- 人工待办 ----------

    [HttpGet("todos")]
    public async Task<ActionResult<List<InboxTodoDto>>> GetTodos(
        [FromQuery] string? status = "Pending",
        CancellationToken ct = default)
    {
        var query = db.InboxTodos.AsNoTracking().Include(t => t.Run).AsQueryable();

        if (!string.Equals(status, "all", StringComparison.OrdinalIgnoreCase)
            && ParseEnum<InboxTodoStatus>(status) is { } todoStatus)
        {
            query = query.Where(t => t.Status == todoStatus);
        }

        var todos = await query
            .OrderByDescending(t => t.CreatedAt)
            .Take(200)
            .ToListAsync(ct);

        return Ok(todos.Select(t => ToTodoDto(t, t.Run)).ToList());
    }

    /// <summary>提交待办输入。Worker 将在下一次轮询发现并恢复运行。</summary>
    [HttpPost("todos/{id:int}/complete")]
    public async Task<ActionResult> CompleteTodo(int id, [FromBody] CompleteTodoRequest request, CancellationToken ct)
    {
        var todo = await db.InboxTodos
            .Include(t => t.Run)
            .FirstOrDefaultAsync(t => t.Id == id, ct);
        if (todo is null)
            return NotFound(new { message = $"待办不存在: {id}" });
        if (todo.Status != InboxTodoStatus.Pending)
            return Conflict(new { message = $"待办状态为 {todo.Status}，无法提交" });

        if (todo.ExpiresAt is { } expiresAt && expiresAt < DateTime.Now)
        {
            todo.Status = InboxTodoStatus.Expired;
            await db.SaveChangesAsync(ct);
            return Conflict(new { message = "待办已过期" });
        }

        if (request.Value is { Length: > 4000 })
            return BadRequest(new { message = "输入内容过长（最多 4000 字符）" });

        todo.InputValue = todo.IsSensitive && !string.IsNullOrEmpty(request.Value)
            ? protector.Protect(request.Value)
            : request.Value;
        todo.Status = InboxTodoStatus.Completed;
        todo.CompletedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        await audit.RecordAsync(
            "TodoCompleted",
            todo.RunId,
            todo.Id,
            todo.Run.WorkflowName,
            actor: "manual",
            detail: $"{todo.InputType}{(todo.IsSensitive ? " · 加密存储" : string.Empty)}",
            ct: ct);

        logger.LogInformation("待办 #{TodoId} 已提交，运行 #{RunId} 将被恢复", todo.Id, todo.RunId);
        return Accepted(new { todoId = todo.Id, runId = todo.RunId, message = "已提交，工作流将在数秒内继续" });
    }

    // ---------- 工作目录 ----------

    /// <summary>运行目录概况（数量、占用、保留策略）。</summary>
    [HttpGet("workspace")]
    public async Task<ActionResult<InboxWorkspaceInfo>> GetWorkspace(CancellationToken ct)
        => Ok(await workspaceCleaner.GetInfoAsync(ct));

    /// <summary>按保留策略清理运行目录；dryRun=true 只预览不删除。活动运行目录永远保留。</summary>
    [HttpPost("workspace/cleanup")]
    public async Task<ActionResult<InboxCleanupResult>> CleanupWorkspace(
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var result = await workspaceCleaner.CleanupAsync(dryRun, ct);
        if (!dryRun && result.Deleted > 0)
        {
            logger.LogInformation("手动清理运行目录：删除 {Count} 个", result.Deleted);
            await audit.RecordAsync(
                "WorkspaceCleanup",
                actor: "manual",
                detail: $"删除 {result.Deleted} 个运行目录（{result.DeletedBytes} 字节）",
                ct: ct);
        }
        return Ok(result);
    }

    /// <summary>按历史保留策略清理已结束的运行、步骤、待办与审计事件；dryRun=true 只预览。</summary>
    [HttpPost("history/cleanup")]
    public async Task<ActionResult<InboxHistoryCleanupResult>> CleanupHistory(
        [FromQuery] bool dryRun = false,
        CancellationToken ct = default)
    {
        var result = await historyCleaner.CleanupAsync(dryRun, ct);
        if (!dryRun && (result.Runs > 0 || result.AuditEvents > 0))
        {
            logger.LogInformation("手动清理历史数据：运行 {Runs}，审计 {AuditEvents}", result.Runs, result.AuditEvents);
            await audit.RecordAsync(
                "HistoryCleanup",
                actor: "manual",
                detail: $"运行 {result.Runs}，步骤 {result.Steps}，待办 {result.Todos}，审计 {result.AuditEvents}",
                ct: ct);
        }
        return Ok(result);
    }

    // ---------- 安全与审计 ----------

    /// <summary>只读安全状态：加密组件、Secret 加密比例、敏感待办与历史保留。</summary>
    [HttpGet("security")]
    public async Task<ActionResult<InboxSecurityStatus>> GetSecurity(CancellationToken ct)
        => Ok(await securityStatus.GetAsync(ct));

    /// <summary>最近的安全审计事件。Detail 不含密码、Secret 或待办输入值。</summary>
    [HttpGet("audit")]
    public async Task<ActionResult<List<InboxAuditEventDto>>> GetAudit(
        [FromQuery] int take = 50,
        [FromQuery] string? eventType = null,
        CancellationToken ct = default)
    {
        take = take is < 1 or > 200 ? 50 : take;
        var query = db.InboxAuditEvents.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(eventType))
            query = query.Where(e => e.EventType == eventType);

        var events = await query
            .OrderByDescending(e => e.Id)
            .Take(take)
            .Select(e => new InboxAuditEventDto(
                e.Id, e.OccurredAt, e.EventType, e.RunId, e.TodoId,
                e.WorkflowName, e.Actor, e.Detail))
            .ToListAsync(ct);

        return Ok(events);
    }

    // ---------- 自检 ----------

    [HttpGet("health")]
    public async Task<ActionResult> Health(CancellationToken ct)
    {
        var workflows = catalog.LoadAll();
        var pendingTodos = await db.InboxTodos.CountAsync(t => t.Status == InboxTodoStatus.Pending, ct);
        var activeRuns = await db.InboxRuns.CountAsync(r =>
            r.Status == InboxRunStatus.Queued
            || r.Status == InboxRunStatus.Running
            || r.Status == InboxRunStatus.WaitingInput, ct);

        return Ok(new
        {
            workflowsPath = catalog.WorkflowsPath,
            workflowCount = workflows.Count,
            workflowErrors = workflows.Count(w => w.Error is not null),
            emailAccountCount = emailAccounts.List().Count,
            emailAccountsConfigured = emailAccounts.List().Count(a => a.Configured),
            workspaceRetentionDays = workspaceCleaner.RetentionDays,
            workspaceMaxRuns = workspaceCleaner.MaxRuns,
            historyRetentionDays = historyCleaner.RetentionDays,
            pendingTodos,
            activeRuns
        });
    }

    // ---------- 映射与脱敏 ----------

    private static InboxTodoDto ToTodoDto(InboxTodo todo, InboxRun run)
    {
        var value = todo.IsSensitive && todo.Status == InboxTodoStatus.Completed
            ? MaskedValue
            : todo.InputValue;

        return new InboxTodoDto(
            todo.Id, todo.RunId, run.WorkflowName, todo.StepRunId,
            todo.Title, todo.Prompt, todo.InputType, todo.Placeholder,
            todo.Status.ToString(), value, todo.IsSensitive,
            todo.CreatedAt, todo.CompletedAt, todo.ExpiresAt, run.Status.ToString());
    }

    /// <summary>human/input 输出整体敏感：先透明解密（如有），再掩码，绝不下发密文。</summary>
    private string PrepareStepOutputs(InboxStepRun step)
    {
        if (!step.Action.Equals("human/input", StringComparison.OrdinalIgnoreCase))
            return step.OutputsJson;

        try
        {
            return MaskHumanOutputs(
                InboxSensitiveData.UnprotectJson(step.OutputsJson, protector));
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "解密步骤输出失败: StepRunId={StepRunId}", step.Id);
            return "{\"value\":\"******\"}";
        }
    }

    private static string MaskHumanOutputs(string json)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null || map.Count == 0)
                return json;
            foreach (var key in map.Keys.ToList())
            {
                if (key.Equals("value", StringComparison.OrdinalIgnoreCase))
                    map[key] = MaskedValue;
            }
            return JsonSerializer.Serialize(map, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static string MaskSensitiveInputs(string json)
    {
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null || map.Count == 0)
                return json;
            foreach (var key in map.Keys.ToList())
            {
                if (IsSensitiveKey(key))
                    map[key] = MaskedValue;
            }
            return JsonSerializer.Serialize(map, IndentedJson);
        }
        catch (JsonException)
        {
            return json;
        }
    }

    private static bool IsSensitiveKey(string key) => InboxSensitiveKeys.IsSensitive(key);

    private static TEnum? ParseEnum<TEnum>(string? value) where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        return Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) && Enum.IsDefined(parsed)
            ? parsed
            : null;
    }
}

public record CompleteTodoRequest(string? Value);

public record InboxWorkflowDto(
    string Name,
    string FileName,
    string? Description,
    bool Manual,
    string? Schedule,
    int StepCount,
    string? Error);

public record InboxWorkflowDetailDto(
    string Name,
    string FileName,
    string? Description,
    bool Manual,
    string? Schedule,
    string Yaml,
    List<InboxWorkflowStepDto> Steps,
    string? Error);

public record InboxWorkflowStepDto(
    string JobName,
    string Id,
    string Name,
    string Uses,
    Dictionary<string, string> With);

public record InboxRunSummaryDto(
    int Id,
    string WorkflowName,
    string Status,
    string TriggerType,
    string TriggeredBy,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Error,
    List<InboxStepSummaryDto> Steps);

public record InboxStepSummaryDto(
    string StepId,
    string StepName,
    string Action,
    string Status,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Error);

public record InboxRunDetailDto(
    int Id,
    string WorkflowName,
    string Status,
    string TriggerType,
    string TriggeredBy,
    DateTime CreatedAt,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    string? Error,
    string WorkflowYaml,
    List<InboxStepDetailDto> Steps,
    List<InboxTodoDto> Todos);

public record InboxStepDetailDto(
    int Id,
    string JobName,
    string StepId,
    string StepName,
    string Action,
    string Status,
    string InputsJson,
    string OutputsJson,
    string Log,
    string? Error,
    DateTime? StartedAt,
    DateTime? CompletedAt,
    int? TodoId,
    string? TodoStatus);

public record InboxTodoDto(
    int Id,
    int RunId,
    string WorkflowName,
    int StepRunId,
    string Title,
    string Prompt,
    string InputType,
    string? Placeholder,
    string Status,
    string? Value,
    bool IsSensitive,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    DateTime? ExpiresAt,
    string RunStatus);

/// <summary>邮箱账号只读视图：只暴露连接所需元信息，密码永不返回。</summary>
public record InboxEmailAccountDto(
    string Name,
    string Host,
    int Port,
    string Security,
    string Username,
    string Folder,
    bool Configured,
    bool HasPassword,
    string PasswordSource,
    string? Error);

/// <summary>审计事件只读视图。</summary>
public record InboxAuditEventDto(
    int Id,
    DateTime OccurredAt,
    string EventType,
    int? RunId,
    int? TodoId,
    string? WorkflowName,
    string? Actor,
    string? Detail);
