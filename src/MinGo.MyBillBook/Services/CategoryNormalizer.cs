using Microsoft.EntityFrameworkCore;
using MinGo.MyBillBook.Core.Interfaces;
using MinGo.MyBillBook.Data;

namespace MinGo.MyBillBook.Services;

/// <summary>
/// 将平台原始分类文本归一化到系统 BillCategory。
/// 匹配策略：静态关键词映射 → 数据库分类名模糊匹配。
/// </summary>
public class CategoryNormalizer(AppDbContext db) : ICategoryNormalizer
{
    private static readonly Dictionary<string, string> KeywordMap = new()
    {
        ["餐饮美食"] = "餐饮",
        ["餐饮"] = "餐饮",
        ["交通出行"] = "交通",
        ["交通"] = "交通",
        ["网络购物"] = "购物",
        ["购物"] = "购物",
        ["生活缴费"] = "居住",
        ["居住"] = "居住",
        ["住房"] = "居住",
        ["金融保险"] = "理财",
        ["理财"] = "理财",
        ["娱乐休闲"] = "娱乐",
        ["娱乐"] = "娱乐",
        ["医疗健康"] = "医疗",
        ["医疗"] = "医疗",
        ["教育培训"] = "教育",
        ["教育"] = "教育",
        ["通讯物流"] = "通讯",
        ["通讯"] = "通讯",
        ["物流快递"] = "通讯",
        ["服饰装扮"] = "服饰",
        ["服饰"] = "服饰",
        ["美容美发"] = "美容",
        ["美容"] = "美容",
        ["运动健身"] = "运动",
        ["运动"] = "运动",
        ["数码电器"] = "数码",
        ["数码"] = "数码",
        ["旅行出行"] = "旅行",
        ["旅行"] = "旅行",
        ["旅游"] = "旅行",
        ["社交人情"] = "社交",
        ["社交"] = "社交",
        ["宠物"] = "宠物",
        ["汽车"] = "汽车",
        ["用车养车"] = "汽车",
        ["转账红包"] = "转账",
        ["转账"] = "转账",
        ["红包"] = "转账",
        ["工资"] = "工资",
        ["薪资"] = "工资",
        ["其他"] = "其他",
    };

    public async Task<CategoryNormalizationResult?> NormalizeAsync(string sourceCategory, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(sourceCategory))
            return null;

        var trimmed = sourceCategory.Trim();

        if (KeywordMap.TryGetValue(trimmed, out var targetName))
        {
            var categoryId = await db.BillCategories
                .Where(c => c.Name == targetName)
                .Select(c => (int?)c.Id)
                .FirstOrDefaultAsync(ct);

            if (categoryId.HasValue)
                return new CategoryNormalizationResult(categoryId.Value, 0.85);
        }

        var categories = await db.BillCategories
            .Select(c => new { c.Id, c.Name })
            .ToListAsync(ct);

        foreach (var cat in categories)
        {
            if (trimmed.Contains(cat.Name, StringComparison.OrdinalIgnoreCase)
                || cat.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return new CategoryNormalizationResult(cat.Id, 0.7);
            }
        }

        return null;
    }
}
