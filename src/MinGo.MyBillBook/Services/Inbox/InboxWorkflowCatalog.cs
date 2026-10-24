using MinGo.MyBillBook.Core.Inbox;
using Quartz;

namespace MinGo.MyBillBook.Services.Inbox;

/// <summary>单个工作流文件的加载结果（解析失败时保留错误信息，不影响其他文件）。</summary>
public sealed class InboxWorkflowFile
{
    public required string FileName { get; init; }
    public required string FilePath { get; init; }
    public required string Yaml { get; init; }
    public WorkflowDefinition? Definition { get; init; }
    public string? Error { get; init; }

    public string Name => Definition?.Name ?? Path.GetFileNameWithoutExtension(FileName);
}

/// <summary>
/// 工作流目录读取器。YAML 文件即工作流定义（参考 GitHub Actions 的 .github/workflows）：
/// 新增接入源只需在目录中新增文件，无需改代码。
/// </summary>
public sealed class InboxWorkflowCatalog
{
    private static readonly string[] Extensions = [".yaml", ".yml"];

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
        try
        {
            var yaml = File.ReadAllText(filePath);
            var definition = WorkflowYamlParser.Parse(yaml);

            // cron 由 Quartz 执行，语法校验放到服务端（Core 不依赖 Quartz）。
            if (definition.On.Schedule is { } cron && !CronExpression.TryParse(cron, out _))
                throw new WorkflowParseException($"on.schedule 不是有效的 Quartz cron 表达式: {cron}");

            return new InboxWorkflowFile
            {
                FileName = fileName,
                FilePath = filePath,
                Yaml = yaml,
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
                Yaml = string.Empty,
                Error = ex.Message
            };
        }
    }

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
