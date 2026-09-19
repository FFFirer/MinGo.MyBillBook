using Microsoft.Extensions.DependencyInjection;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Data.DuckDb;
using MinGo.MyBillBook.Hubs;
using Microsoft.AspNetCore.SignalR;
using Quartz;

namespace MinGo.MyBillBook.BackgroundJobs;

public class BillProcessingJob(IHubContext<NotificationHub> hubContext, IServiceScopeFactory scopeFactory) : IJob
{
    public static readonly JobKey JobKey = new("bill-processing-job");
    public static readonly TriggerKey TriggerKey = new("bill-processing-trigger");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var processingService = scope.ServiceProvider.GetRequiredService<IBillProcessingService>();
        var analysisService = scope.ServiceProvider.GetRequiredService<IAnalysisService>();

        var processed = await processingService.ProcessUnprocessedAsync(stoppingToken);
        if (processed > 0)
        {
            await analysisService.SyncToDuckDbAsync(stoppingToken);
            await hubContext.Clients.All.SendAsync("BatchProcessed", 0, processed, stoppingToken);
            await hubContext.Clients.All.SendAsync("DataSynced", stoppingToken);
        }
    }
}

public class DuckDbSyncJob(IHubContext<NotificationHub> hubContext, IServiceScopeFactory scopeFactory) : IJob
{
    public static readonly JobKey JobKey = new("duckdb-sync-job");
    public static readonly TriggerKey TriggerKey = new("duckdb-sync-trigger");

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var analysisService = scope.ServiceProvider.GetRequiredService<IAnalysisService>();
        await analysisService.SyncToDuckDbAsync(stoppingToken);
        await hubContext.Clients.All.SendAsync("DataSynced", stoppingToken);
    }
}
