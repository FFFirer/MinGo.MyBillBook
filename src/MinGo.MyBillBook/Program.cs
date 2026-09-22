using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.BackgroundJobs;
using MinGo.MyBillBook.Components;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Core.Parsing;
using MinGo.MyBillBook.Data;
using MinGo.MyBillBook.Data.DuckDb;
using MinGo.MyBillBook.Hubs;
using MinGo.MyBillBook.Services;
using MinGo.MyBillBook.Services.Pipeline;
using MinGo.MyBillBook.Services.Pipeline.Steps;
using Quartz;

var builder = WebApplication.CreateBuilder(args);

// Logging - SimpleConsole 单行格式
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
});

// EF Core + SQLite
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")
        ?? "Data Source=mybillbook.db"));

// DuckDB
builder.Services.AddSingleton<DuckDbContext>(_ => new DuckDbContext("analysis.duckdb"));

// Quartz.NET
builder.Services.AddQuartz(q =>
{
    // Bill processing job - runs every 30 minutes
    q.AddJob<BillProcessingJob>(opts => opts.WithIdentity(BillProcessingJob.JobKey))
     .AddTrigger(opts => opts
        .ForJob(BillProcessingJob.JobKey)
        .WithIdentity(BillProcessingJob.TriggerKey)
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(30)).RepeatForever()));

    // DuckDB sync job - runs every 15 minutes
    q.AddJob<DuckDbSyncJob>(opts => opts.WithIdentity(DuckDbSyncJob.JobKey))
     .AddTrigger(opts => opts
        .ForJob(DuckDbSyncJob.JobKey)
        .WithIdentity(DuckDbSyncJob.TriggerKey)
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromMinutes(15)).RepeatForever()));
});
builder.Services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);

// Application services
builder.Services.AddScoped<IBillImportService, BillImportService>();
builder.Services.AddScoped<IBillProcessingService, BillProcessingService>();
builder.Services.AddScoped<IBillQueryService, BillQueryService>();
builder.Services.AddScoped<ICategoryRuleEngine, CategoryRuleEngine>();
builder.Services.AddScoped<ICategoryClassifier, NullCategoryClassifier>();
builder.Services.AddScoped<IMerchantResolver, MerchantResolver>();
builder.Services.AddScoped<IAnalysisService, AnalysisService>();
builder.Services.AddScoped<DuckDbSyncService>();
builder.Services.AddScoped<CsvExportService>();
builder.Services.AddSingleton<IBillParserFactory, BillParserFactory>();
builder.Services.AddSingleton<IBillParser, AlipayCsvParser>();
builder.Services.AddSingleton<IBillParser, WechatCsvParser>();

// Pipeline engine
builder.Services.AddScoped<IPipelineRunner, PipelineRunner>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, NormalizeStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, ResolveMerchantStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, DeduplicateStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, ClassifyCategoryStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, DetectTransferStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, ApplyTagsStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, PublishCanonicalStep>();
builder.Services.AddScoped<IPipelineStep<BillImportContext>, ReconcileStep>();

// Phase 9: 局部重跑 + Job 队列（BackgroundService 轮询 Pending 任务异步执行）
builder.Services.AddScoped<IRebuildService, RebuildService>();
builder.Services.AddHostedService<PipelineJobWorker>();

// Add controllers for Web API
builder.Services.AddControllers();
builder.Services.AddSignalR();

// Blazor
builder.Services.AddRazorComponents()
    .AddInteractiveWebAssemblyComponents();

var app = builder.Build();

// Auto migration + seed data
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();
    SeedData.Initialize(db);
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();
app.UseAntiforgery();
app.MapStaticAssets();

// Map API controllers
app.MapControllers();

// Map SignalR hub
app.MapHub<NotificationHub>("/hubs/notifications");

// Map Blazor
app.MapRazorComponents<App>()
    .AddInteractiveWebAssemblyRenderMode()
    .AddAdditionalAssemblies(typeof(MinGo.MyBillBook.Client._Imports).Assembly);

app.Run();
