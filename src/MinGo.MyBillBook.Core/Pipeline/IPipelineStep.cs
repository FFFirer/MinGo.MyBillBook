namespace MinGo.MyBillBook.Core.Pipeline;

/// <summary>
/// 管道步骤抽象。每个步骤只负责一项转换职责（Input → Step → Output），
/// 通过 <see cref="Order"/> 决定执行顺序，由 Runner 统一编排与追踪。
/// </summary>
public interface IPipelineStep<TContext> where TContext : IPipelineContext
{
    /// <summary>步骤名称，用于追踪与展示。</summary>
    string Name { get; }

    /// <summary>执行顺序，数值越小越先执行。</summary>
    int Order { get; }

    /// <summary>执行步骤逻辑，返回本步骤的输入/输出/错误/告警计数。</summary>
    Task<StepResult> ExecuteAsync(TContext context, CancellationToken ct);
}

/// <summary>单个步骤的执行结果统计。</summary>
public record StepResult(int InputCount, int OutputCount, int ErrorCount = 0, int WarningCount = 0);
