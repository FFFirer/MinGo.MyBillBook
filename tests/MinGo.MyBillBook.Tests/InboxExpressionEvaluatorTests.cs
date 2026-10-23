using MinGo.MyBillBook.Core.Inbox;

namespace MinGo.MyBillBook.Tests;

public class InboxExpressionEvaluatorTests
{
    private static WorkflowEvaluationContext CreateContext()
    {
        var steps = new Dictionary<string, StepEvaluation>(StringComparer.OrdinalIgnoreCase)
        {
            ["fetch"] = new StepEvaluation
            {
                Status = "Completed",
                Outputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["files"] = "attachments/bill.zip",
                    ["count"] = "2"
                }
            },
            ["password"] = new StepEvaluation
            {
                Status = "WaitingInput",
                Outputs = new Dictionary<string, string>()
            }
        };

        return new WorkflowEvaluationContext
        {
            Secrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["IMAP_USER"] = "user@example.com"
            },
            Steps = steps,
            Inputs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["platform"] = "alipay"
            },
            WorkspacePath = "/workspace/runs/7",
            RunId = 7
        };
    }

    [Fact]
    public void Evaluate_Secret_ShouldResolve()
    {
        var result = ExpressionEvaluator.Evaluate("${{ secrets.IMAP_USER }}", CreateContext());
        Assert.Equal("user@example.com", result);
    }

    [Fact]
    public void Evaluate_StepOutput_ShouldResolve()
    {
        var result = ExpressionEvaluator.Evaluate("${{ steps.fetch.outputs.files }}", CreateContext());
        Assert.Equal("attachments/bill.zip", result);
    }

    [Fact]
    public void Evaluate_StepStatus_ShouldResolve()
    {
        var result = ExpressionEvaluator.Evaluate("${{ steps.password.status }}", CreateContext());
        Assert.Equal("WaitingInput", result);
    }

    [Fact]
    public void Evaluate_WithSurroundingText_ShouldInterpolate()
    {
        var result = ExpressionEvaluator.Evaluate(
            "file=${{ steps.fetch.outputs.files }}&run=${{ run.id }}", CreateContext());
        Assert.Equal("file=attachments/bill.zip&run=7", result);
    }

    [Fact]
    public void Evaluate_WorkspaceAndInputs_ShouldResolve()
    {
        var context = CreateContext();
        Assert.Equal("/workspace/runs/7/input", ExpressionEvaluator.Evaluate("${{ workspace }}/input", context));
        Assert.Equal("alipay", ExpressionEvaluator.Evaluate("${{ inputs.platform }}", context));
    }

    [Fact]
    public void Evaluate_UnknownPath_ShouldReturnEmptyAndRecordWarning()
    {
        var context = CreateContext();
        var result = ExpressionEvaluator.Evaluate("${{ steps.missing.outputs.value }}", context);

        Assert.Equal(string.Empty, result);
        Assert.Contains(context.Warnings, w => w.Contains("steps.missing.outputs.value"));
    }

    [Fact]
    public void EvaluateDictionary_ShouldEvaluateAllValues()
    {
        var context = CreateContext();
        var evaluated = ExpressionEvaluator.EvaluateDictionary(new Dictionary<string, string>
        {
            ["file"] = "${{ steps.fetch.outputs.files }}",
            ["platform"] = "alipay"
        }, context);

        Assert.Equal("attachments/bill.zip", evaluated["file"]);
        Assert.Equal("alipay", evaluated["platform"]);
    }

    [Fact]
    public void Evaluate_NullOrEmptyTemplate_ShouldReturnEmpty()
    {
        Assert.Equal(string.Empty, ExpressionEvaluator.Evaluate(null, CreateContext()));
        Assert.Equal(string.Empty, ExpressionEvaluator.Evaluate("", CreateContext()));
    }
}
