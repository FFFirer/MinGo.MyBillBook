using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Models;
using MinGo.MyBillBook.Core.Pipeline;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services.Pipeline.Steps;

/// <summary>
/// 转账检测步骤（Order 在 Classify 之后、ApplyTags 之前）。
/// 1) 依据 NormalizedTransaction.PaymentMethod 将 Canonical 记录映射到资金账户(FundAccountId)，
///    为对账(Reconcile)提供账户维度；
/// 2) 识别账户间转账：Direction=不计收支 / 含转账关键词 / 同日等额一正一负成对记录，
///    标记 TransactionType.Transfer，避免"还款被算两次消费"，并暂存 Transfer 待发布落库。
/// 不删除任何原始记录（符合设计"不要直接删除 Raw 数据"）。
/// </summary>
public class DetectTransferStep(AppDbContext db) : IPipelineStep<BillImportContext>
{
    public string Name => "DetectTransfer";
    public int Order => 22;

    private static readonly string[] TransferKeywords =
        ["转账", "还款", "转入", "转出", "信用卡还款", "还信用卡", "充值", "提现"];

    public async Task<StepResult> ExecuteAsync(BillImportContext context, CancellationToken ct)
    {
        var records = context.CanonicalRecords;
        var input = records.Count;
        if (input == 0)
            return new StepResult(0, 0);

        // 1) 资金账户映射：按 RawRecordId 关联 PaymentMethod -> FundAccount.Name
        var accounts = await db.FundAccounts.AsNoTracking().ToListAsync(ct);
        var paymentByRaw = context.NormalizedTransactions
            .GroupBy(t => t.RawRecordId)
            .ToDictionary(g => g.Key, g => g.First().PaymentMethod);
        foreach (var r in records)
        {
            if (r.FundAccountId.HasValue) continue;
            if (!paymentByRaw.TryGetValue(r.RawRecordId, out var pm) || string.IsNullOrWhiteSpace(pm)) continue;
            var acc = MatchAccount(accounts, pm);
            if (acc.HasValue) r.FundAccountId = acc.Value;
        }

        // 2a) 关键词识别（Direction=不计收支 已由 ClassifyCategoryStep 标为 Transfer）
        foreach (var r in records)
        {
            if (r.TransactionType == TransactionType.Transfer) continue;
            var text = $"{r.ProductName} {r.Counterparty}";
            if (TransferKeywords.Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase)))
                r.TransactionType = TransactionType.Transfer;
        }

        // 2b) 同日、等额、一正一负成对匹配（跨账户转账的典型特征）
        var detected = 0;
        var groups = records
            .Where(r => r.TransactionType == TransactionType.Income || r.TransactionType == TransactionType.Expense)
            .GroupBy(r => (r.TransactionDate.Date, r.AmountMinor))
            .Where(g => g.Any(x => x.TransactionType == TransactionType.Income)
                     && g.Any(x => x.TransactionType == TransactionType.Expense))
            .ToList();

        foreach (var g in groups)
        {
            var incomes = g.Where(x => x.TransactionType == TransactionType.Income).ToList();
            var expenses = g.Where(x => x.TransactionType == TransactionType.Expense).ToList();
            var n = Math.Min(incomes.Count, expenses.Count);
            for (var i = 0; i < n; i++)
            {
                var expense = expenses[i]; // 转出方（金额减少）
                var income = incomes[i];   // 转入方（金额增加）
                expense.TransactionType = TransactionType.Transfer;
                income.TransactionType = TransactionType.Transfer;
                context.PendingTransfers.Add((expense, income, g.Key.AmountMinor, g.Key.Date));
                detected++;
            }
        }

        var transferCount = records.Count(r => r.TransactionType == TransactionType.Transfer);
        return new StepResult(input, transferCount, 0, detected);
    }

    /// <summary>付款方式文本匹配资金账户：精确名优先，其次双向包含。</summary>
    private static int? MatchAccount(List<FundAccount> accounts, string paymentMethod)
    {
        var pm = paymentMethod.Trim();
        var exact = accounts.FirstOrDefault(a => string.Equals(a.Name, pm, StringComparison.OrdinalIgnoreCase));
        if (exact != null) return exact.Id;
        var contains = accounts.FirstOrDefault(a =>
            pm.Contains(a.Name, StringComparison.OrdinalIgnoreCase) ||
            a.Name.Contains(pm, StringComparison.OrdinalIgnoreCase));
        return contains?.Id;
    }
}
