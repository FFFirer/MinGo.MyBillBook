using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline;

/// <summary>
/// 默认管道运行器：按 Order 顺序执行步骤，逐步写入 PipelineRun / PipelineStepRun 追踪记录。
/// 步骤异常会被记录到上下文 Issues 与追踪表，并向上传播以保留原有失败语义。
/// </summary>
public class PipelineRunner(AppDbContext db, ILogger<PipelineRunner> logger) : IPipelineRunner
{
    public async Task<int> RunAsync<TContext>(
        TContext context,
        IEnumerable<IPipelineStep<TContext>> steps,
        PipelineType pipelineType = PipelineType.Import,
        string? fromStep = null,
        CancellationToken ct = default)
        where TContext : IPipelineContext
    {
        var run = new PipelineRun
        {
            PipelineType = pipelineType,
            Status = PipelineStatus.Running,
            StartedAt = DateTime.Now,
            ImportBatchId = context.ImportBatchId,
            Version = "1.0"
        };
        db.PipelineRuns.Add(run);
        await db.SaveChangesAsync(ct);
        context.RunId = run.Id;

        var orderedSteps = steps.OrderBy(s => s.Order).ToList();

        // 局部重跑：解析起点步骤的 Order，Order 小于起点的步骤标记为 Skipped 不执行。
        var startOrder = int.MinValue;
        if (!string.IsNullOrWhiteSpace(fromStep))
        {
            var startStep = orderedSteps.FirstOrDefault(s =>
                string.Equals(s.Name, fromStep, StringComparison.OrdinalIgnoreCase));
            if (startStep is null)
                throw new InvalidOperationException($"未知的重跑起点步骤：{fromStep}");
            startOrder = startStep.Order;
        }

        int lastOutput = 0;

        foreach (var step in orderedSteps)
        {
            var stepRun = new PipelineStepRun
            {
                PipelineRunId = run.Id,
                StepName = step.Name,
                Status = PipelineStepStatus.Running,
                StartedAt = DateTime.Now
            };

            // 起点之前的步骤：跳过（不执行、记为 Skipped），保留追踪可见性。
            if (step.Order < startOrder)
            {
                stepRun.Status = PipelineStepStatus.Skipped;
                stepRun.CompletedAt = DateTime.Now;
                db.PipelineStepRuns.Add(stepRun);
                await db.SaveChangesAsync(ct);
                continue;
            }

            db.PipelineStepRuns.Add(stepRun);
            await db.SaveChangesAsync(ct);

            try
            {
                var result = await step.ExecuteAsync(context, ct);
                stepRun.InputCount = result.InputCount;
                stepRun.OutputCount = result.OutputCount;
                stepRun.ErrorCount = result.ErrorCount;
                stepRun.WarningCount = result.WarningCount;
                stepRun.Status = PipelineStepStatus.Completed;
                stepRun.CompletedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
                lastOutput = result.OutputCount;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "管道步骤 {StepName} 执行失败 (RunId={RunId})", step.Name, run.Id);
                stepRun.Status = PipelineStepStatus.Failed;
                stepRun.ErrorCount++;
                stepRun.CompletedAt = DateTime.Now;
                context.Issues.Add(new PipelineIssue(step.Name, ex.Message, IssueSeverity.Error));

                run.Status = PipelineStatus.Failed;
                run.CompletedAt = DateTime.Now;
                await db.SaveChangesAsync(ct);
                throw;
            }
        }

        run.Status = PipelineStatus.Completed;
        run.CompletedAt = DateTime.Now;
        await db.SaveChangesAsync(ct);

        return lastOutput;
    }
}
