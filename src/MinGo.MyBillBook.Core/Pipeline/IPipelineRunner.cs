namespace MinGo.MyBillBook.Core.Pipeline;

/// <summary>
/// 管道运行器：按 Order 顺序执行步骤，逐步写入 PipelineRun / PipelineStepRun 追踪记录，
/// 捕获异常并记录到上下文 Issues。支持后续从指定步骤起跑（局部重跑）。
/// </summary>
public interface IPipelineRunner
{
    /// <summary>
    /// 运行一条管道。
    /// </summary>
    /// <param name="context">执行上下文。</param>
    /// <param name="steps">本次运行的步骤集合（由调用方从 DI 注入）。</param>
    /// <param name="pipelineType">管道类型（Import / Rebuild）。</param>
    /// <param name="fromStep">局部重跑起点步骤名（Order &lt; 起点的步骤标记为 Skipped 不执行）；为空则全部执行。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>最终输出计数（最后一个成功步骤的 OutputCount）。</returns>
    Task<int> RunAsync<TContext>(
        TContext context,
        IEnumerable<IPipelineStep<TContext>> steps,
        Models.PipelineType pipelineType = Models.PipelineType.Import,
        string? fromStep = null,
        CancellationToken ct = default)
        where TContext : IPipelineContext;
}
