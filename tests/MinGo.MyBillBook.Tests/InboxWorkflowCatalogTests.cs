using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using MinGo.MyBillBook.Services.Inbox;

namespace MinGo.MyBillBook.Tests;

public sealed class InboxWorkflowCatalogTests : IDisposable
{
    private readonly string _directory;
    private readonly InboxWorkflowCatalog _catalog;

    public InboxWorkflowCatalogTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(), "mbb-inbox-workflow-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Inbox:WorkflowsPath"] = _directory
            })
            .Build();

        _catalog = new InboxWorkflowCatalog(
            configuration, NullLogger<InboxWorkflowCatalog>.Instance);
    }

    [Fact]
    public void Create_ValidYaml_WritesFileAndRevision()
    {
        var file = _catalog.Create(ValidYaml("测试工作流"), "my-flow.yaml");

        Assert.Equal("my-flow.yaml", file.FileName);
        Assert.Equal("测试工作流", file.Name);
        Assert.NotNull(file.Definition);
        Assert.True(File.Exists(file.FilePath));
        Assert.False(string.IsNullOrWhiteSpace(file.Revision));
        Assert.Single(_catalog.LoadAll());
    }

    [Fact]
    public void Create_DuplicateName_ThrowsNameConflict()
    {
        _catalog.Create(ValidYaml("重复名称"), "first.yaml");

        var error = Assert.Throws<InboxWorkflowWriteException>(
            () => _catalog.Create(ValidYaml("重复名称"), "second.yaml"));

        Assert.Equal(InboxWorkflowWriteError.NameConflict, error.Error);
    }

    [Fact]
    public void Create_FilePathTraversal_ThrowsInvalidFileName()
    {
        var error = Assert.Throws<InboxWorkflowWriteException>(
            () => _catalog.Create(ValidYaml("穿越"), "../evil.yaml"));

        Assert.Equal(InboxWorkflowWriteError.InvalidFileName, error.Error);
    }

    [Fact]
    public void Create_ExistingFile_ThrowsFileConflict()
    {
        File.WriteAllText(Path.Combine(_directory, "taken.yaml"), ValidYaml("已存在"));

        var error = Assert.Throws<InboxWorkflowWriteException>(
            () => _catalog.Create(ValidYaml("新名称"), "taken.yaml"));

        Assert.Equal(InboxWorkflowWriteError.FileConflict, error.Error);
    }

    [Fact]
    public void Validate_InvalidCron_ReturnsError()
    {
        const string yaml = """
            name: 定时工作流
            on:
              schedule: not-a-cron
            jobs:
              - name: main
                steps:
                  - id: confirm
                    uses: human/input
            """;

        var result = _catalog.Validate(yaml);

        Assert.False(result.Valid);
        Assert.Contains("cron", result.Error);
    }

    [Fact]
    public void Update_StaleRevision_ThrowsRevisionMismatch()
    {
        var created = _catalog.Create(ValidYaml("原名称"), "flow.yaml");

        var error = Assert.Throws<InboxWorkflowWriteException>(
            () => _catalog.Update(created.Name, ValidYaml("新名称"), "deadbeefdeadbeef"));

        Assert.Equal(InboxWorkflowWriteError.RevisionMismatch, error.Error);
        Assert.Equal("原名称", _catalog.LoadAll().Single().Name);
    }

    [Fact]
    public void Update_WithRevision_BacksUpOldVersion()
    {
        var created = _catalog.Create(ValidYaml("原名称"), "flow.yaml");

        var updated = _catalog.Update(created.Name, ValidYaml("新名称"), created.Revision);

        Assert.Equal("新名称", updated.Name);
        Assert.NotEqual(created.Revision, updated.Revision);

        var backups = Directory.GetFiles(
            Path.Combine(_directory, ".history", "flow"), "*.yaml");
        var backup = Assert.Single(backups);
        Assert.Contains("原名称", File.ReadAllText(backup));
    }

    [Fact]
    public void Delete_BacksUpAndRemovesFile()
    {
        var created = _catalog.Create(ValidYaml("待删除"), "flow.yaml");

        var result = _catalog.Delete(created.Name);

        Assert.False(File.Exists(created.FilePath));
        Assert.Empty(_catalog.LoadAll());
        Assert.NotNull(result.BackupFile);
        Assert.True(File.Exists(Path.Combine(_directory, result.BackupFile!)));
    }

    [Fact]
    public void LoadAll_BrokenYaml_KeepsRawContentForEditing()
    {
        File.WriteAllText(Path.Combine(_directory, "broken.yaml"), "name: [");

        var file = Assert.Single(_catalog.LoadAll());

        Assert.NotNull(file.Error);
        Assert.Equal("name: [", file.Yaml);
        Assert.False(string.IsNullOrWhiteSpace(file.Revision));
        Assert.Null(file.Definition);
    }

    private static string ValidYaml(string name) => $"""
        name: {name}
        description: 测试工作流
        on:
          manual: true
        jobs:
          - name: main
            steps:
              - id: confirm
                name: 确认
                uses: human/input
                with:
                  title: 确认
        """;

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
                Directory.Delete(_directory, recursive: true);
        }
        catch
        {
            // 测试清理失败不影响断言结果。
        }
    }
}
