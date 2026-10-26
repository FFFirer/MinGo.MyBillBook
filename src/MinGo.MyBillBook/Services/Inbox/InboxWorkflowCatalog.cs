using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MinGo.MyBillBook.Core.Inbox;
using Quartz;

namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>单个工作流文件的加载结果（解析失败时保留原始 YAML 与错误信息，不影响其他文件）。</summary>
public sealed class InboxWorkflowFile
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required string Yaml { get; init; }
    /// <summary>YAML 内容的 SHA-256 前缀，用于在线编辑的乐观并发校验。</summary>
    public string Revision { get; init; } = string.Empty;
    public WorkflowDefinition? Definition { get; init; }
    public string? Error { get; init; }

    public string Name => Definition?.Name ?? Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>在线保存工作流时的业务错误类型。</summary>
public enum InboxWorkflowWriteError
{
    InvalidFileName,
    NameConflict,
    FileConflict,
    NotFound,
    RevisionMismatch
}

public sealed class InboxWorkflowWriteException(InboxWorkflowWriteError error, string message) : Exception(message)
{
    public InboxWorkflowWriteError Error { get; } = error;
}

/// <summary>YAML 校验结果；Definition 为空时 Error 给出可读原因。</summary>
public sealed record InboxWorkflowValidation(WorkflowDefinition? Definition, string? Error)
{
    public bool Valid => Definition is not null;
}

public sealed record InboxWorkflowTemplate(string Id, string Name, string Yaml);

public sealed record InboxWorkflowDeleteResult(string Name, string FileName, string? BackupFile);

/// <summary>
/// 工作流目录读取器。YAML 文件即工作流定义（参考 GitHub Actions 的 .github/workflows）：
/// 新增接入源只需在目录中新增文件，无需改代码。
/// 在线管理通过临时文件 + 原子替换写入，并在更新/删除前把旧版本备份到 .history/。
/// </summary>
public sealed class InboxWorkflowCatalog
{
    private static readonly string[] Extensions = [".yaml", ".yml"];
    private static readonly Regex FileNamePattern = new(
        @"^[A-Za-z0-9][A-Za-z0-9._-]{0,63}$", RegexOptions.Compiled);
    private const int MaxBackupsPerWorkflow = 20;

    private readonly string _workflowsPath;
    private readonly ILogger<InboxWorkflowCatalog> _logger;

    public InboxWorkflowCatalog(IConfiguration configuration, ILogger<InboxWorkflowCatalog> logger)
    {
        _logger = logger;
        var configured = configuration.GetValue<string>("Inbox:WorkflowsPath") ?? "data/inboxes";
        _workflowsPath = Path.GetFullPath(Path.IsPathRooted(configured)
            ? configured
            : Path.Combine(Directory.GetCurrentDirectory(), configured));
    }

    public string WorkflowsPath => _workflowsPath;

    public IReadOnlyList<InboxWorkflowFile> LoadAll()
    {
        if (!Directory.Exists(_workflowsPath))
            return [];

        return Directory.EnumerateFiles(_workflowsPath, "*", SearchOption.TopDirectoryOnly)
            .Where(f => Extensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .Select(LoadFile)
            .ToList();
    }

    /// <summary>按 YAML 中的 name 查找工作流（大小写不敏感）。</summary>
    public InboxWorkflowFile? Find(string name)
        => LoadAll().FirstOrDefault(f =>
            string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>校验 YAML 语法、语义与 cron；不写入磁盘。</summary>
    public InboxWorkflowValidation Validate(string yaml)
    {
        try
        {
            return new InboxWorkflowValidation(ParseDefinition(yaml), null);
        }
        catch (WorkflowParseException ex)
        {
            return new InboxWorkflowValidation(null, ex.Message);
        }
    }

    /// <summary>新建工作流文件；fileName 为空时自动生成。</summary>
    public InboxWorkflowFile Create(string yaml, string? fileName)
    {
        var definition = ParseDefinition(yaml);
        EnsureNameAvailable(definition.Name, excludePath: null);

        var path = Path.Combine(_workflowsPath, BuildFileName(fileName));
        if (File.Exists(path))
            throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.FileConflict, $"文件已存在: {Path.GetFileName(path)}");

        WriteAtomic(path, yaml);
        _logger.LogInformation("已创建工作流 {Workflow}: {Path}", definition.Name, path);
        return LoadFile(path);
    }

    /// <summary>更新已有工作流；expectedRevision 不匹配时拒绝覆盖。</summary>
    public InboxWorkflowFile Update(string currentName, string yaml, string? expectedRevision)
    {
        var current = Find(currentName)
            ?? throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.NotFound, $"工作流不存在: {currentName}");

        if (!string.IsNullOrEmpty(expectedRevision)
            && !string.Equals(expectedRevision, current.Revision, StringComparison.OrdinalIgnoreCase))
        {
            throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.RevisionMismatch,
                "工作流文件已被外部修改，请关闭编辑器后重新加载再保存");
        }

        var definition = ParseDefinition(yaml);
        EnsureNameAvailable(definition.Name, current.FilePath);

        if (string.Equals(current.Yaml, yaml, StringComparison.Ordinal))
            return current;

        Backup(current);
        WriteAtomic(current.FilePath, yaml);
        _logger.LogInformation("已更新工作流 {Workflow}: {Path}", definition.Name, current.FilePath);
        return LoadFile(current.FilePath);
    }

    /// <summary>删除工作流文件，删除前先备份到 .history/。</summary>
    public InboxWorkflowDeleteResult Delete(string name)
    {
        var current = Find(name)
            ?? throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.NotFound, $"工作流不存在: {name}");

        var backup = Backup(current);
        File.Delete(current.FilePath);
        _logger.LogInformation("已删除工作流 {Workflow}: {Path}", current.Name, current.FilePath);

        return new InboxWorkflowDeleteResult(
            current.Name,
            current.FileName,
            Path.GetRelativePath(_workflowsPath, backup));
    }

    /// <summary>内置模板；用于在线新建工作流。</summary>
    public IReadOnlyList<InboxWorkflowTemplate> GetTemplates() =>
    [
        new("manual", "手动导入账单",
            SampleWorkflowYaml.Replace("name: 示例：手动导入账单", "name: 我的手动导入工作流", StringComparison.Ordinal)),
        new("email", "从邮箱导入账单",
            SampleEmailWorkflowYaml.Replace("name: 示例：从邮箱导入 CSV 账单", "name: 我的邮件导入工作流", StringComparison.Ordinal)),
        new("archive", "解压加密压缩包",
            SampleArchiveWorkflowYaml.Replace("name: 示例：解压加密账单压缩包", "name: 我的加密压缩包导入工作流", StringComparison.Ordinal))
    ];

    /// <summary>在目录中创建演示工作流（已存在则不覆盖），返回本次新建的文件名。</summary>
    public IReadOnlyList<string> CreateSampleWorkflows()
    {
        Directory.CreateDirectory(_workflowsPath);
        var created = new List<string>();

        created.Add(WriteIfMissing("sample-manual-import.yaml", SampleWorkflowYaml));
        created.Add(WriteIfMissing("sample-email-import.yaml", SampleEmailWorkflowYaml));
        created.Add(WriteIfMissing("sample-archive-import.yaml", SampleArchiveWorkflowYaml));

        return created;
    }

    private string WriteIfMissing(string fileName, string yaml)
    {
        var path = Path.Combine(_workflowsPath, fileName);
        if (!File.Exists(path))
        {
            File.WriteAllText(path, yaml);
            _logger.LogInformation("已创建演示工作流: {Path}", path);
        }

        return fileName;
    }

    private InboxWorkflowFile LoadFile(string filePath)
    {
        var fileName = Path.GetFileName(filePath);
        var yaml = string.Empty;
        var revision = string.Empty;
        try
        {
            yaml = File.ReadAllText(filePath);
            revision = ComputeRevision(yaml);
            var definition = ParseDefinition(yaml);

            return new InboxWorkflowFile
            {
                FileName = fileName,
                FilePath = filePath,
                Yaml = yaml,
                Revision = revision,
                Definition = definition
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "工作流文件解析失败: {FileName}", fileName);
            return new InboxWorkflowFile
            {
                FileName = fileName,
                FilePath = filePath,
                // 保留原始内容，便于在线编辑器修正；空文件时自然为空。
                Yaml = yaml,
                Revision = revision,
                Error = ex.Message
            };
        }
    }

    /// <summary>解析并校验 cron；cron 由 Quartz 执行，语法校验放在服务端（Core 不依赖 Quartz）。</summary>
    private static WorkflowDefinition ParseDefinition(string yaml)
    {
        var definition = WorkflowYamlParser.Parse(yaml);
        if (definition.On.Schedule is { } cron && !CronExpression.TryParse(cron, out _))
            throw new WorkflowParseException($"on.schedule 不是有效的 Quartz cron 表达式: {cron}");
        return definition;
    }

    private void EnsureNameAvailable(string name, string? excludePath)
    {
        var normalized = name.Trim();
        foreach (var file in LoadAll())
        {
            if (excludePath is not null
                && string.Equals(file.FilePath, excludePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var stem = Path.GetFileNameWithoutExtension(file.FileName);
            if (string.Equals(file.Name, normalized, StringComparison.OrdinalIgnoreCase)
                || string.Equals(stem, normalized, StringComparison.OrdinalIgnoreCase))
            {
                throw new InboxWorkflowWriteException(
                    InboxWorkflowWriteError.NameConflict, $"工作流名称已存在: {normalized}");
            }
        }
    }

    private static string BuildFileName(string? fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return $"workflow-{DateTime.Now:yyyyMMddHHmmssfff}.yaml";

        var trimmed = fileName.Trim();
        var nameOnly = Path.GetFileName(trimmed);
        if (!string.Equals(nameOnly, trimmed, StringComparison.Ordinal))
        {
            throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.InvalidFileName,
                "文件名不能包含路径分隔符，仅支持字母、数字、点、下划线和连字符");
        }

        var extension = Path.GetExtension(nameOnly);
        if (extension.Length > 0
            && !Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.InvalidFileName, "文件扩展名必须是 .yaml 或 .yml");
        }

        var stem = Path.GetFileNameWithoutExtension(nameOnly);
        if (!FileNamePattern.IsMatch(stem) || stem.StartsWith('.'))
        {
            throw new InboxWorkflowWriteException(
                InboxWorkflowWriteError.InvalidFileName,
                "文件名仅支持字母、数字、点、下划线和连字符，必须以字母或数字开头且不超过 64 个字符");
        }

        return stem + ".yaml";
    }

    /// <summary>把当前版本复制到 .history/{stem}/ 并只保留最近 20 份。</summary>
    private string Backup(InboxWorkflowFile file)
    {
        var stem = Path.GetFileNameWithoutExtension(file.FileName);
        var directory = Path.Combine(_workflowsPath, ".history", stem);
        Directory.CreateDirectory(directory);

        var revisionPart = file.Revision.Length >= 8 ? file.Revision[..8] : "unknown";
        var target = Path.Combine(
            directory,
            $"{DateTime.Now:yyyyMMddHHmmssfff}-{revisionPart}.yaml");

        File.Copy(file.FilePath, target, overwrite: true);
        PruneBackups(directory);
        return target;
    }

    private static void PruneBackups(string directory)
    {
        foreach (var stale in Directory.EnumerateFiles(directory, "*.yaml")
                     .OrderByDescending(f => f, StringComparer.Ordinal)
                     .Skip(MaxBackupsPerWorkflow))
        {
            try
            {
                File.Delete(stale);
            }
            catch
            {
                // 备份清理失败不影响本次保存。
            }
        }
    }

    private static void WriteAtomic(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = path + ".tmp";
        try
        {
            File.WriteAllText(temp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            File.Move(temp, path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
            {
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    // 临时文件残留不影响加载（LoadAll 只识别 .yaml/.yml）。
                }
            }
        }
    }

    private static string ComputeRevision(string yaml)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(yaml)))
            .ToLowerInvariant()[..16];

    private const string SampleWorkflowYaml = """
        name: 示例：手动导入账单
        description: 手动触发并可附加一个账单文件；工作流先创建待办等待确认，再调用标准导入与清洗流程。

        on:
          manual: true

        jobs:
          - name: import
            steps:
              - id: confirm
                name: 确认本次导入
                uses: human/input
                with:
                  title: 确认导入
                  prompt: 触发文件已放入工作目录。请在待办中输入“确认”后继续。
                  input_type: text
                  placeholder: 输入“确认”
                  expires_hours: 72

              - id: import
                name: 导入账单文件
                uses: bill/import
                with:
                  file: input/*
                  platform: auto

              - id: process
                name: 运行清洗管道
                uses: bill/process
                with:
                  batch_id: ${{ steps.import.outputs.batch_id }}
        """;

    private const string SampleEmailWorkflowYaml = """
        name: 示例：从邮箱导入 CSV 账单
        description: 从配置的 IMAP 邮箱下载账单附件，人工确认后导入并运行清洗管道。

        on:
          manual: true

        jobs:
          - name: import
            steps:
              - id: fetch
                name: 下载邮件附件
                uses: email/fetch-attachments
                with:
                  account: inbox
                  search: subject:账单 since:60d unseen
                  attachments: "*.csv,*.xlsx"
                  mark_seen: true

              - id: confirm
                name: 确认导入
                uses: human/input
                with:
                  title: 确认导入邮件账单
                  prompt: 附件已下载到本次运行目录，请在待办中输入“确认”后继续。
                  input_type: text
                  expires_hours: 72

              - id: import
                name: 导入账单
                uses: bill/import
                with:
                  file: attachments/*.csv
                  platform: auto

              - id: process
                name: 运行清洗管道
                uses: bill/process
                with:
                  batch_id: ${{ steps.import.outputs.batch_id }}
        """;

    private const string SampleArchiveWorkflowYaml = """
        name: 示例：解压加密账单压缩包
        description: 手动上传带密码的 ZIP，输入解压密码后自动解压、定位账单文件、导入并清洗。

        on:
          manual: true

        jobs:
          - name: import
            steps:
              - id: password
                name: 输入解压密码
                uses: human/input
                with:
                  title: 压缩包解压密码
                  prompt: 请输入本次 ZIP 的解压密码（例如身份证后六位或公众号中查看的密码）。
                  input_type: password
                  expires_hours: 72

              - id: extract
                name: 解压附件
                uses: archive/extract
                with:
                  source: input/*.zip
                  target: extracted
                  password: ${{ steps.password.outputs.value }}

              - id: detect
                name: 定位账单文件
                uses: file/detect
                with:
                  source: extracted/**/*
                  extensions: csv,xlsx

              - id: import
                name: 导入账单
                uses: bill/import
                with:
                  file: ${{ steps.detect.outputs.first_file }}
                  platform: auto

              - id: process
                name: 运行清洗管道
                uses: bill/process
                with:
                  batch_id: ${{ steps.import.outputs.batch_id }}
        """;
}
