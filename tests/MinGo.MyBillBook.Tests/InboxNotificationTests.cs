using MinGo.MyBillBook.Core.Models;
using Xunit;

namespace MinGo.MyBillBook.Tests;

public class InboxNotificationTests
{
    [Fact]
    public void ForTodo_ContainsTodoMetadataButNotInputValue()
    {
        var now = new DateTime(2026, 10, 24, 12, 0, 0);
        var todo = new InboxTodo
        {
            Id = 7,
            RunId = 3,
            Title = "压缩包解压密码",
            Prompt = "请输入本次 ZIP 的解压密码。",
            InputType = "password",
            IsSensitive = true,
            Status = InboxTodoStatus.Completed,
            InputValue = "top-secret-value"
        };

        var notification = InboxNotification.ForTodo(todo, "信用卡账单", now);

        Assert.Equal(InboxNotification.TypeTodoPending, notification.Type);
        Assert.Equal(InboxNotification.LevelWarning, notification.Level);
        Assert.Equal(3, notification.RunId);
        Assert.Equal(7, notification.TodoId);
        Assert.Equal("信用卡账单", notification.WorkflowName);
        Assert.Contains("压缩包解压密码", notification.Title);
        Assert.Contains("请输入本次 ZIP", notification.Message);
        Assert.DoesNotContain("top-secret-value", notification.Message);
        Assert.False(notification.IsRead);
        Assert.Equal(now, notification.CreatedAt);
    }

    [Fact]
    public void ForRunFailure_ContainsStepAndTruncatesLongValues()
    {
        var run = new InboxRun
        {
            Id = 9,
            WorkflowName = new string('w', 260),
            Status = InboxRunStatus.Failed,
            Error = new string('e', 2_000)
        };

        var notification = InboxNotification.ForRunFailure(
            run, "extract", new DateTime(2026, 10, 24, 12, 0, 0));

        Assert.Equal(InboxNotification.TypeRunFailed, notification.Type);
        Assert.Equal(InboxNotification.LevelError, notification.Level);
        Assert.Equal(9, notification.RunId);
        Assert.Equal(200, notification.Title.Length);
        Assert.Equal(1_000, notification.Message.Length);
        Assert.StartsWith("步骤 extract：", notification.Message);
    }

    [Fact]
    public void ForRunFailure_WithoutStepUsesRunError()
    {
        var run = new InboxRun
        {
            Id = 11,
            WorkflowName = "账单导入",
            Status = InboxRunStatus.Failed,
            Error = "运行执行失败"
        };

        var notification = InboxNotification.ForRunFailure(
            run, null, new DateTime(2026, 10, 24, 12, 0, 0));

        Assert.Equal("运行执行失败", notification.Message);
        Assert.Contains("账单导入", notification.Title);
    }
}
