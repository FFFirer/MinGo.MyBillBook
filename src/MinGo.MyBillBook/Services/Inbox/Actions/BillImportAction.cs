using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// bill/import：把工作目录中的账单文件交给现有账单导入服务，
/// 与“导入对账单”页面手工上传走同一套解析、原始文件存储与去重逻辑。
/// </summary>
public sealed class BillImportAction(
    InboxWorkspace workspace,
    IBillImportService importService,
    ILogger<BillImportAction> logger) : IInboxAction
{
    public string Uses => "bill/import";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        var pattern = context.Get("file");
        if (string.IsNullOrWhiteSpace(pattern))
            return InboxActionResult.Failed("缺少参数 file（相对工作目录的文件路径或 glob，如 input/*.csv）");

        IReadOnlyList<string> files;
        try
        {
            files = workspace.ResolveFiles(context.RunId, pattern);
        }
        catch (Exception ex)
        {
            return InboxActionResult.Failed($"文件路径无效: {ex.Message}");
        }

        if (files.Count == 0)
            return InboxActionResult.Failed(
                $"未找到匹配文件: {pattern}（工作目录: {context.WorkspacePath}）");

        if (files.Count > 1)
            context.Log($"匹配到 {files.Count} 个文件，仅导入第一个: {Path.GetFileName(files[0])}");

        var filePath = files[0];
        var fileName = Path.GetFileName(filePath);
        var platform = (context.Get("platform") ?? "auto").Trim();
        var platformId = ResolvePlatformId(platform, fileName);

        if (platformId == 0)
            return InboxActionResult.Failed(
                $"无法判断账单平台: platform={platform}, file={fileName}（可选 alipay / wechat / auto）");

        context.Log($"导入文件: {fileName}（platformId={platformId}）");
        logger.LogInformation("运行 #{RunId} 导入文件 {File}", context.RunId, filePath);

        await using var stream = File.OpenRead(filePath);
        var result = await importService.ImportAsync(stream, fileName, platformId, ct);

        if (result.Errors.Count > 0)
        {
            context.Log($"解析告警 {result.Errors.Count} 条: {string.Join("; ", result.Errors.Take(3))}");
        }

        context.Log(
            $"批次 #{result.BatchId}: 总数 {result.TotalCount}, 新增 {result.SuccessCount}, " +
            $"更新 {result.UpdatedCount}, 重复 {result.DuplicateCount}");

        return InboxActionResult.Completed(
            ("batch_id", result.BatchId.ToString()),
            ("total", result.TotalCount.ToString()),
            ("success", result.SuccessCount.ToString()),
            ("updated", result.UpdatedCount.ToString()),
            ("duplicate", result.DuplicateCount.ToString()),
            ("file_name", result.FileName),
            ("platform_id", platformId.ToString()));
    }

    private static int ResolvePlatformId(string platform, string fileName)
    {
        switch (platform.ToLowerInvariant())
        {
            case "1" or "alipay" or "支付宝":
                return 1;
            case "2" or "wechat" or "wechatpay" or "微信":
                return 2;
            case "auto":
                var lower = fileName.ToLowerInvariant();
                if (lower.Contains("alipay") || lower.Contains("支付宝"))
                    return 1;
                if (lower.Contains("wechat") || lower.Contains("微信")
                    || lower.EndsWith(".xlsx") || lower.EndsWith(".xls"))
                    return 2;
                return 0;
            default:
                return 0;
        }
    }
}
