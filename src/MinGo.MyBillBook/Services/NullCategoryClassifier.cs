using MinGo.MyBillBook.Core.Interfaces;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// AI/ML 分类器的空实现（设计第 8 节扩展点占位）。当前不接入任何模型，恒返回 null，
/// 使分类优先级链在 AI 层直接落空、继续到 Default 层。未来接入真实模型时，
/// 仅需在 DI 中替换该实现，ClassifyCategoryStep 与优先级链无需改动。
/// </summary>
public class NullCategoryClassifier : ICategoryClassifier
{
    public Task<CategoryClassification?> ClassifyAsync(string counterparty, string productName, CancellationToken ct = default)
        => Task.FromResult<CategoryClassification?>(null);
}
