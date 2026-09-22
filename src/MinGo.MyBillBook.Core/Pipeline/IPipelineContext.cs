namespace MinGo.MyBillBook.Core.Pipeline;

/// <summary>
/// 管道执行上下文。承载一次 Pipeline 运行的状态、统计与问题，
/// 各步骤通过上下文传递工作集，步骤之间不直接强依赖。
/// </summary>
public interface IPipelineContext
{
    /// <summary>本次运行对应的 PipelineRun.Id，由 Runner 在执行前写入。</summary>
    int RunId { get; set; }

    /// <summary>关联的导入批次（如适用）。</summary>
    int? ImportBatchId { get; set; }

    /// <summary>数据来源标识，例如 "import" / "rebuild"。</summary>
    string Source { get; set; }

    /// <summary>运行期可选项。</summary>
    Dictionary<string, object?> Options { get; }

    /// <summary>累计统计（键值对，如 processed / skipped）。</summary>
    Dictionary<string, int> Statistics { get; }

    /// <summary>执行过程中收集的告警/错误。</summary>
    List<PipelineIssue> Issues { get; }
}

/// <summary>管道执行过程中记录的问题。</summary>
public record PipelineIssue(string StepName, string Message, IssueSeverity Severity);

public enum IssueSeverity
{
    Warning = 0,
    Error = 1
}
