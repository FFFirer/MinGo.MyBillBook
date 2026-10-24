using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Tests;

public class InboxWorkflowParserTests
{
    private const string ValidYaml = """
        name: 支付宝月度账单
        description: 下载邮件附件，人工输入解压密码后导入

        on:
          manual: true
          schedule: "0 0 9 1 * ?"

        jobs:
          - name: import
            steps:
              - id: password
                name: 输入解压密码
                uses: human/input
                with:
                  title: 解压密码
                  prompt: 请输入本次 ZIP 的解压密码
                  input_type: password
                  expires_hours: 72

              - id: import
                name: 导入账单
                uses: bill/import
                with:
                  file: extracted/*.csv
                  platform: alipay
        """;

    [Fact]
    public void Parse_WithValidYaml_ShouldMapAllProperties()
    {
        var definition = WorkflowYamlParser.Parse(ValidYaml);

        Assert.Equal("支付宝月度账单", definition.Name);
        Assert.Equal("下载邮件附件，人工输入解压密码后导入", definition.Description);
        Assert.True(definition.On.Manual);
        Assert.Equal("0 0 9 1 * ?", definition.On.Schedule);

        var job = Assert.Single(definition.Jobs);
        Assert.Equal("import", job.Name);
        Assert.Equal(2, job.Steps.Count);

        var passwordStep = job.Steps[0];
        Assert.Equal("password", passwordStep.Id);
        Assert.Equal("输入解压密码", passwordStep.Name);
        Assert.Equal("human/input", passwordStep.Uses);
        Assert.Equal("解压密码", passwordStep.With["title"]);
        Assert.Equal("password", passwordStep.With["input_type"]);
        Assert.Equal("72", passwordStep.With["expires_hours"]);
    }

    [Fact]
    public void Parse_WithoutOn_ShouldDefaultToManualTrigger()
    {
        const string yaml = """
            name: 简单工作流
            jobs:
              - name: main
                steps:
                  - id: run
                    uses: bill/process
            """;

        var definition = WorkflowYamlParser.Parse(yaml);

        Assert.True(definition.On.Manual);
        Assert.Null(definition.On.Schedule);
    }

    [Fact]
    public void Parse_ShouldDefaultStepNameToId_AndLowercaseWithKeys()
    {
        const string yaml = """
            name: 测试
            jobs:
              - name: main
                steps:
                  - id: step_one
                    uses: bill/import
                    with:
                      FILE: input/*.csv
            """;

        var definition = WorkflowYamlParser.Parse(yaml);
        var step = definition.Jobs[0].Steps[0];

        Assert.Equal("step_one", step.Name);
        Assert.Equal("input/*.csv", step.With["file"]);
    }

    [Fact]
    public void Parse_WithDuplicateStepId_ShouldThrow()
    {
        const string yaml = """
            name: 重复步骤
            jobs:
              - name: main
                steps:
                  - id: same
                    uses: bill/import
                  - id: same
                    uses: bill/process
            """;

        var ex = Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
        Assert.Contains("重复", ex.Message);
    }

    [Fact]
    public void Parse_WithMissingName_ShouldThrow()
    {
        const string yaml = """
            jobs:
              - name: main
                steps:
                  - id: run
                    uses: bill/process
            """;

        var ex = Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Parse_WithMissingUses_ShouldThrow()
    {
        const string yaml = """
            name: 缺少动作
            jobs:
              - name: main
                steps:
                  - id: run
                    name: 没有 uses
            """;

        var ex = Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
        Assert.Contains("uses", ex.Message);
    }

    [Fact]
    public void Parse_WithUnknownField_ShouldThrow()
    {
        const string yaml = """
            name: 拼写错误
            jobs:
              - name: main
                steps:
                  - id: run
                    use: bill/import
            """;

        Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
    }

    [Fact]
    public void Parse_WithInvalidStepId_ShouldThrow()
    {
        const string yaml = """
            name: 非法 id
            jobs:
              - name: main
                steps:
                  - id: 1-invalid
                    uses: bill/import
            """;

        var ex = Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
        Assert.Contains("非法", ex.Message);
    }

    [Fact]
    public void Parse_WithEmptyRetryBlock_ShouldUseDefaults()
    {
        const string yaml = """
            name: 默认重试
            jobs:
              - name: main
                steps:
                  - id: fetch
                    uses: email/fetch-attachments
                    retry: {}
            """;

        var retry = WorkflowYamlParser.Parse(yaml).Jobs[0].Steps[0].Retry;

        Assert.NotNull(retry);
        Assert.Equal(2, retry.Attempts);
        Assert.Equal(5, retry.DelaySeconds);
        Assert.Equal(2, retry.Backoff);
        Assert.Equal(TimeSpan.FromSeconds(5), retry.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(10), retry.GetDelay(2));
    }

    [Fact]
    public void Parse_WithCustomRetry_ShouldMapAndComputeBackoff()
    {
        const string yaml = """
            name: 自定义重试
            jobs:
              - name: main
                steps:
                  - id: fetch
                    uses: email/fetch-attachments
                    retry:
                      attempts: 3
                      delay_seconds: 4
                      backoff: 3
            """;

        var retry = WorkflowYamlParser.Parse(yaml).Jobs[0].Steps[0].Retry;

        Assert.NotNull(retry);
        Assert.Equal(3, retry.Attempts);
        Assert.Equal(4, retry.DelaySeconds);
        Assert.Equal(3, retry.Backoff);
        Assert.Equal(TimeSpan.FromSeconds(4), retry.GetDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(12), retry.GetDelay(2));
    }

    [Fact]
    public void GetDelay_ShouldCapAtMaxDelay()
    {
        var retry = new WorkflowRetryDefinition { DelaySeconds = 120, Backoff = 10 };

        Assert.Equal(TimeSpan.FromSeconds(WorkflowRetryDefinition.MaxDelaySeconds), retry.GetDelay(2));
    }

    [Theory]
    [InlineData("attempts: 0")]
    [InlineData("attempts: 6")]
    [InlineData("delay_seconds: -1")]
    [InlineData("delay_seconds: 301")]
    [InlineData("backoff: 0.5")]
    [InlineData("backoff: 11")]
    public void Parse_WithInvalidRetry_ShouldThrow(string retryLine)
    {
        var yaml = $"""
            name: 非法重试
            jobs:
              - name: main
                steps:
                  - id: fetch
                    uses: email/fetch-attachments
                    retry:
                      {retryLine}
            """;

        var ex = Assert.Throws<WorkflowParseException>(() => WorkflowYamlParser.Parse(yaml));
        Assert.Contains("retry.", ex.Message);
    }

    [Fact]
    public void Parse_ShouldTrimSchedule()
    {
        const string yaml = """
            name: 定时工作流
            on:
              manual: false
              schedule: "  0 0 9 * * ?  "
            jobs:
              - name: main
                steps:
                  - id: run
                    uses: bill/process
            """;

        var definition = WorkflowYamlParser.Parse(yaml);

        Assert.Equal("0 0 9 * * ?", definition.On.Schedule);
        Assert.False(definition.On.Manual);
    }

    [Fact]
    public void FlattenSteps_ShouldPreserveJobAndStepOrder()
    {
        var definition = WorkflowYamlParser.Parse(ValidYaml);
        var steps = WorkflowYamlParser.FlattenSteps(definition);

        Assert.Equal(["password", "import"], steps.Select(s => s.Id));
    }
}
