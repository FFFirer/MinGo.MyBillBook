namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 一次管道运行的追踪记录。对应设计中的 PipelineRun，
/// 记录运行类型、状态、起止时间与版本，支持规则变更后重跑历史数据。
/// </summary>
public class PipelineRun
{
    public int Id { get; set; }
    public PipelineType PipelineType { get; set; }
    public PipelineStatus Status { get; set; } = PipelineStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public string Version { get; set; } = "1.0";
    public int? ImportBatchId { get; set; }

    public BillImportBatch? ImportBatch { get; set; }
    public ICollection<PipelineStepRun> Steps { get; set; } = [];
}

/// <summary>
/// 单个管道步骤的运行追踪记录。对应设计中的 PipelineStepRun，
/// 记录每步的输入/输出/错误/告警计数，供前端展示步骤级进度。
/// </summary>
public class PipelineStepRun
{
    public int Id { get; set; }
    public int PipelineRunId { get; set; }
    public string StepName { get; set; } = string.Empty;
    public PipelineStepStatus Status { get; set; } = PipelineStepStatus.Running;
    public DateTime StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public int InputCount { get; set; }
    public int OutputCount { get; set; }
    public int ErrorCount { get; set; }
    public int WarningCount { get; set; }

    public PipelineRun Run { get; set; } = null!;
}

/// <summary>
/// 持久化后台任务（设计第 16 节 Job 队列，MVP 不用 Redis）。
/// 导入/重建请求入队后立即返回，由 Worker 轮询 Pending 任务异步执行，避免 HTTP 同步跑完整 Pipeline。
/// </summary>
public class PipelineJob
{
    public int Id { get; set; }
    public PipelineJobType Type { get; set; }
    /// <summary>任务参数（JSON），如 {"from":"ResolveMerchant","batchId":3}。</summary>
    public string Payload { get; set; } = "{}";
    public PipelineJobStatus Status { get; set; } = PipelineJobStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    /// <summary>失败时的错误信息。</summary>
    public string? Error { get; set; }
}
