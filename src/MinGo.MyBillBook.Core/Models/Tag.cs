namespace MinGo.MyBillBook.Core.Models;

/// <summary>
/// 标签（设计第 1–10 节）。与 Category（树形、主归属）正交的扁平横向属性，
/// 一笔交易可携带多个 Tag。TagType 表达标签语义类别，Scope 限定其适用范围。
/// </summary>
public class Tag
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public TagType TagType { get; set; } = TagType.Scenario;

    /// <summary>适用范围：Global 全局可用，Category/Account 需配合 ScopeId 或 CategoryTag 限定。</summary>
    public TagScope Scope { get; set; } = TagScope.Global;

    /// <summary>Scope 为 Category/Account 时的目标 Id（Global 时为 null）。</summary>
    public int? ScopeId { get; set; }

    public int SortOrder { get; set; }

    public ICollection<TransactionTag> TransactionTags { get; set; } = [];
    public ICollection<TagRule> Rules { get; set; } = [];
    public ICollection<CategoryTag> CategoryTags { get; set; } = [];
}
