using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Core.Pipeline;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 商户归一化步骤（Order 介于 Normalize 与 Classify 之间）：对每条标准化交易，
/// 按 MerchantAlias 优先级把交易对方/描述归一到统一 Merchant，结果按交易 Id 存入上下文，
/// 供 ClassifyCategoryStep 写入 BillRecord.MerchantId 与 Merchant 溯源。
/// </summary>
public class ResolveMerchantStep(IMerchantResolver resolver) : IPipelineStep<BillImportContext>
{
    public string Name => "ResolveMerchant";
    public int Order => 15;

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var input = context.NormalizedTransactions.Count;
        var resolved = 0;

        foreach (var tx in context.NormalizedTransactions)
        {
            var resolution = await resolver.ResolveAsync(tx.Counterparty, tx.RawDescription, ct);
            context.MerchantResolutions[tx.Id] = resolution;
            resolved++;
        }

        return new StepResult(input, resolved);
    }
}
