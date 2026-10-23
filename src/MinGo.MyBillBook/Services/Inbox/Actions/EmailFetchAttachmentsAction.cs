using System.Text.Json;
using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using MimeKit;
using MinGo.MyBillBook.Core.Inbox;
using MinGo.MyBillBook.Services.Inbox.Email;

namespace MinGo.MyBillBook.Services.Inbox.Actions;

/// <summary>
/// email/fetch-attachments：按配置连接 IMAP 邮箱，检索邮件并把附件下载到
/// 本次运行目录的 attachments/（可配置子目录、扩展名过滤、大小上限、已读标记）。
/// 只负责取件，解压与导入由后续步骤完成。
/// </summary>
public sealed class EmailFetchAttachmentsAction(
    InboxWorkspace workspace,
    InboxEmailAccounts accounts,
    ILogger<EmailFetchAttachmentsAction> logger) : IInboxAction
{
    public string Uses => "email/fetch-attachments";

    public async Task<InboxActionResult> ExecuteAsync(InboxActionContext context, CancellationToken ct)
    {
        var accountName = context.Get("account");
        if (string.IsNullOrWhiteSpace(accountName))
            return InboxActionResult.Failed("缺少参数 account（请在 Inbox:EmailAccounts 中配置邮箱账号）");

        var account = accounts.Find(accountName);
        if (account is null)
        {
            var known = accounts.List().Select(a => a.Name).ToList();
            return InboxActionResult.Failed(
                $"未找到可用的邮箱账号: {accountName}" +
                (known.Count > 0 ? $"（已配置: {string.Join(", ", known)}）" : "（尚未配置任何邮箱账号）"));
        }

        EmailSearchQuery query;
        try
        {
            query = EmailSearchQuery.Parse(context.Get("search"));
        }
        catch (ArgumentException ex)
        {
            return InboxActionResult.Failed($"search 无效: {ex.Message}");
        }

        var folderName = context.Get("folder") ?? account.Folder;
        var downloadDir = InboxWorkspace.NormalizeRelativeDirectory(context.Get("download_dir"), "attachments");
        var attachmentPatterns = context.Get("attachments");
        var markSeen = ParseBool(context.Get("mark_seen"), defaultValue: true);
        var includeInline = ParseBool(context.Get("include_inline"), defaultValue: false);
        var limit = ParseInt(context.Get("limit"), defaultValue: 10, min: 1, max: 100);
        var maxFileMb = ParseInt(context.Get("max_file_mb"), defaultValue: 50, min: 1, max: 500);

        var runPath = workspace.EnsureRunWorkspace(context.RunId);
        string targetDir;
        try
        {
            targetDir = workspace.ResolvePath(runPath, downloadDir);
        }
        catch (ArgumentException ex)
        {
            return InboxActionResult.Failed($"download_dir 无效: {ex.Message}");
        }
        Directory.CreateDirectory(targetDir);

        context.Log(
            $"邮箱 {account.Name}: {account.Host}:{account.Port}（{account.SecurityText}），文件夹 {folderName}，密码来源 {account.PasswordSource}");
        context.Log($"检索条件: {query.Describe()}；最多处理 {limit} 封，附件过滤: {attachmentPatterns ?? "全部"}");

        using var client = new ImapClient();
        client.Timeout = account.TimeoutSeconds * 1000;

        try
        {
            try
            {
                await client.ConnectAsync(account.Host, account.Port, account.Security, ct);
                await client.AuthenticateAsync(account.Username, account.Password, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "IMAP 连接/认证失败: {Account}", account.Name);
                return InboxActionResult.Failed(
                    $"IMAP 连接或认证失败（{account.Host}:{account.Port}）: {ex.Message}");
            }

            IMailFolder folder;
            IList<UniqueId> uids;
            try
            {
                folder = string.Equals(folderName, "INBOX", StringComparison.OrdinalIgnoreCase)
                    ? client.Inbox
                    : await client.GetFolderAsync(folderName, ct);
                await folder.OpenAsync(markSeen ? FolderAccess.ReadWrite : FolderAccess.ReadOnly, ct);
                uids = await folder.SearchAsync(BuildSearchQuery(query), ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "打开邮箱文件夹或检索失败: {Account}/{Folder}", account.Name, folderName);
                return InboxActionResult.Failed($"打开文件夹或检索失败（{folderName}）: {ex.Message}");
            }

            var targets = uids.OrderByDescending(u => u.Id).Take(limit).ToList();
            context.Log($"命中 {uids.Count} 封邮件，本次处理 {targets.Count} 封");

            var saved = new List<string>();
            var processedMessages = 0;
            var downloadErrors = 0;

            foreach (var uid in targets)
            {
                ct.ThrowIfCancellationRequested();

                MimeMessage message;
                try
                {
                    message = await folder.GetMessageAsync(uid, ct);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    downloadErrors++;
                    context.Log($"跳过邮件 UID {uid}: 读取失败（{ex.Message}）");
                    logger.LogWarning(ex, "读取邮件失败: {Account} UID={Uid}", account.Name, uid);
                    continue;
                }

                processedMessages++;
                var subject = string.IsNullOrWhiteSpace(message.Subject) ? "(无主题)" : message.Subject.Trim();
                var savedInMessage = 0;

                foreach (var entity in message.Attachments)
                {
                    ct.ThrowIfCancellationRequested();

                    if (!includeInline && string.Equals(entity.ContentDisposition?.Disposition,
                            ContentDisposition.Inline, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (entity is not MimePart part)
                    {
                        context.Log($"跳过邮件《{subject}》中的非标准附件（嵌套邮件等）");
                        continue;
                    }

                    var fileName = part.FileName;
                    if (string.IsNullOrWhiteSpace(fileName))
                        continue;
                    if (!EmailAttachmentFilter.Matches(fileName, attachmentPatterns))
                    {
                        context.Log($"跳过附件 {fileName}（不匹配过滤条件）");
                        continue;
                    }

                    var safeName = WorkspaceFileName.Sanitize(fileName, "attachment");
                    var fullPath = GetUniquePath(targetDir, safeName);

                    try
                    {
                        var content = part.Content;
                        if (content is null)
                        {
                            context.Log($"跳过附件 {fileName}: 内容为空");
                            continue;
                        }

                        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write,
                                         FileShare.None, 81920, useAsync: true))
                        {
                            await content.DecodeToAsync(fs, ct);
                        }

                        var size = new FileInfo(fullPath).Length;
                        if (size > maxFileMb * 1024L * 1024L)
                        {
                            File.Delete(fullPath);
                            context.Log($"跳过附件 {fileName}: 超过 {maxFileMb}MB");
                            continue;
                        }

                        var relative = Path.GetRelativePath(runPath, fullPath).Replace('\\', '/');
                        saved.Add(relative);
                        savedInMessage++;
                        context.Log($"保存附件 {relative}（{FormatSize(size)}）");
                    }
                    catch (OperationCanceledException)
                    {
                        TryDelete(fullPath);
                        throw;
                    }
                    catch (Exception ex)
                    {
                        downloadErrors++;
                        TryDelete(fullPath);
                        context.Log($"附件 {fileName} 下载失败: {ex.Message}");
                        logger.LogWarning(ex, "下载附件失败: {Account} UID={Uid} File={File}",
                            account.Name, uid, fileName);
                    }
                }

                if (savedInMessage > 0)
                {
                    context.Log($"邮件《{subject}》: 保存 {savedInMessage} 个附件");
                    if (markSeen)
                    {
                        try
                        {
                            await folder.AddFlagsAsync([uid], MessageFlags.Seen,
                                silent: true, cancellationToken: ct);
                        }
                        catch (Exception ex)
                        {
                            context.Log($"标记邮件《{subject}》为已读失败: {ex.Message}");
                        }
                    }
                }
                else
                {
                    context.Log($"邮件《{subject}》: 无可导入附件");
                }
            }

            if (saved.Count == 0 && downloadErrors > 0)
                return InboxActionResult.Failed($"共 {downloadErrors} 个附件下载失败，请查看步骤日志");

            context.Log($"完成: 处理 {processedMessages} 封邮件，保存 {saved.Count} 个附件到 {downloadDir}");

            return InboxActionResult.Completed(
                ("count", saved.Count.ToString()),
                ("messages", processedMessages.ToString()),
                ("files", JsonSerializer.Serialize(saved)),
                ("first_file", saved.Count > 0 ? saved[0] : string.Empty),
                ("first_name", saved.Count > 0 ? Path.GetFileName(saved[0]) : string.Empty),
                ("folder", folder.FullName));
        }
        finally
        {
            if (client.IsConnected)
            {
                try
                {
                    await client.DisconnectAsync(quit: true, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    logger.LogDebug(ex, "IMAP 断开连接失败");
                }
            }
        }
    }

    private static SearchQuery BuildSearchQuery(EmailSearchQuery query)
    {
        var terms = new List<SearchQuery>();
        if (query.UnreadOnly)
            terms.Add(SearchQuery.NotSeen);
        if (!string.IsNullOrWhiteSpace(query.FromContains))
            terms.Add(SearchQuery.FromContains(query.FromContains));
        if (!string.IsNullOrWhiteSpace(query.SubjectContains))
            terms.Add(SearchQuery.SubjectContains(query.SubjectContains));
        if (query.Since is { } since)
            terms.Add(SearchQuery.SentSince(since));
        if (query.Before is { } before)
            terms.Add(SearchQuery.SentBefore(before));

        return terms.Count switch
        {
            0 => SearchQuery.All,
            1 => terms[0],
            _ => terms.Aggregate(SearchQuery.And)
        };
    }

    private static string GetUniquePath(string directory, string fileName)
    {
        var path = Path.Combine(directory, fileName);
        if (!File.Exists(path))
            return path;

        var stem = Path.GetFileNameWithoutExtension(fileName);
        var extension = Path.GetExtension(fileName);
        for (var index = 2; index < 1000; index++)
        {
            path = Path.Combine(directory, $"{stem}-{index}{extension}");
            if (!File.Exists(path))
                return path;
        }

        return Path.Combine(directory, $"{stem}-{Guid.NewGuid():N}{extension}");
    }

    private static bool ParseBool(string? value, bool defaultValue)
        => string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : value.Trim().ToLowerInvariant() switch
            {
                "true" or "1" or "yes" or "on" => true,
                "false" or "0" or "no" or "off" => false,
                _ => defaultValue
            };

    private static int ParseInt(string? value, int defaultValue, int min, int max)
        => int.TryParse(value, out var parsed)
            ? Math.Clamp(parsed, min, max)
            : defaultValue;

    private static string FormatSize(long bytes)
        => bytes >= 1024 * 1024
            ? $"{bytes / 1024d / 1024d:0.#}MB"
            : $"{Math.Max(1, bytes / 1024d):0.#}KB";

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch
        {
            // 清理失败不影响主流程
        }
    }
}
