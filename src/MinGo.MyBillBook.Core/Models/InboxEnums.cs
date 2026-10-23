namespace MinGo.MyBillBook.Core.Models;

/// <summary>工作流触发方式。</summary>
public enum InboxTriggerType
{
    Manual = 0,
    Schedule = 1
}

/// <summary>
/// 工作流运行状态。WaitingInput 表示运行已挂起、等待待办完成后由 Worker 恢复，
/// 该状态持久化在数据库中，进程重启不影响。
/// </summary>
public enum InboxRunStatus
{
    Queued = 0,
    Running = 1,
    WaitingInput = 2,
    Completed = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>工作流步骤状态。</summary>
public enum InboxStepStatus
{
    Pending = 0,
    Running = 1,
    WaitingInput = 2,
    Completed = 3,
    Failed = 4,
    Skipped = 5
}

/// <summary>人工待办状态。</summary>
public enum InboxTodoStatus
{
    Pending = 0,
    Completed = 1,
    Expired = 2
}
