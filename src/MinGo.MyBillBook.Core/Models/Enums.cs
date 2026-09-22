namespace MinGo.MyBillBook.Core.Models;

public enum TransactionType
{
    Income = 0,
    Expense = 1,
    Transfer = 2,
    Adjustment = 3
}

/// <summary>
/// 清洗决策所针对的字段维度（设计第 11 节 ClassificationResult）。
/// </summary>
public enum ClassificationField
{
    Merchant = 0,
    Category = 1,
    Tag = 2
}

/// <summary>
/// 清洗决策来源，用于优先级判断与溯源（User 最高，Default 最低）。
/// </summary>
public enum ClassificationSource
{
    User = 0,
    ExplicitRule = 1,
    MerchantRule = 2,
    AI = 3,
    Default = 4
}

public enum AccountType
{
    Balance = 0,       // 余额
    YuEBao = 1,        // 余额宝
    BankCard = 2,      // 银行卡
    LingQian = 3,      // 零钱
    LingQianTong = 4   // 零钱通
}

public enum ImportBatchStatus
{
    Imported = 0,
    Processing = 1,
    Completed = 2,
    Failed = 3
}

public enum MatchField
{
    Counterparty = 0,
    ProductName = 1,
    Merchant = 2
}

/// <summary>
/// 商户别名匹配方式（设计第 4/7 节）。
/// </summary>
public enum AliasMatchType
{
    Regex = 0,
    Contains = 1,
    Exact = 2
}

/// <summary>
/// 去重评分结果状态（设计第 6 节）。Unique 为唯一记录；PotentialDuplicate 需人工复核；
/// Duplicate 判定为重复并跳过发布，但保留 Raw 数据不删除。
/// </summary>
public enum DuplicateStatus
{
    Unique = 0,
    PotentialDuplicate = 1,
    Duplicate = 2
}

/// <summary>
/// 重复候选对的复核状态。
/// </summary>
public enum DuplicateCandidateStatus
{
    Pending = 0,
    Confirmed = 1,
    Rejected = 2
}

/// <summary>
/// 标签类型（设计第 1 节，Tag 与 Category 正交的横向属性维度）。
/// </summary>
public enum TagType
{
    Scenario = 0,   // 场景：旅行/社交/家庭
    Purpose = 1,    // 用途：工作/报销
    Nature = 2,     // 性质：必要/非必要
    Project = 3,    // 项目
    Status = 4      // 状态：周末等
}

/// <summary>
/// 标签适用范围（设计第 4 节）。
/// </summary>
public enum TagScope
{
    Global = 0,     // 全局可用
    Category = 1,   // 限定某些分类
    Account = 2     // 限定某些账户
}

/// <summary>
/// 标签规则的条件维度（设计第 7 节）。
/// </summary>
public enum TagRuleCondition
{
    Category = 0,
    Merchant = 1,
    DayOfWeek = 2,
    AmountRange = 3,
    Keyword = 4
}

/// <summary>
/// 标签来源，用于优先级与不被自动规则覆盖（User 最高）。
/// </summary>
public enum TagSource
{
    User = 0,
    Rule = 1,
    AI = 2
}

/// <summary>
/// 转账识别状态（设计第 10 节）。Detected 为自动识别待确认，可人工确认或驳回。
/// </summary>
public enum TransferStatus
{
    Detected = 0,
    Confirmed = 1,
    Rejected = 2
}

/// <summary>
/// 对账差异状态（设计第 11 节）。
/// </summary>
public enum ReconciliationIssueStatus
{
    Open = 0,
    Resolved = 1
}

public enum PipelineType
{
    Import = 0,
    Rebuild = 1
}

public enum PipelineStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2
}

public enum PipelineStepStatus
{
    Running = 0,
    Completed = 1,
    Failed = 2,
    Skipped = 3
}

/// <summary>
/// 后台任务类型（设计第 16 节 Job 队列）。Import 为批次处理，Rebuild 为规则变更后的局部重跑。
/// </summary>
public enum PipelineJobType
{
    Process = 0,
    Rebuild = 1
}

/// <summary>
/// 后台任务状态。Pending 等待 Worker 领取，Running 执行中，Completed/Failed 为终态。
/// </summary>
public enum PipelineJobStatus
{
    Pending = 0,
    Running = 1,
    Completed = 2,
    Failed = 3
}
