using MinGo.MyBillBook.Core.Models;

namespace MinGo.MyBillBook.Data;

public static class SeedData
{
    public static void Initialize(AppDbContext context)
    {
        if (context.PaymentPlatforms.Any()) return;

        // 支付平台
        var alipay = new PaymentPlatform { Name = "支付宝", Code = "ALIPAY", IconUrl = "/icons/alipay.svg" };
        var wechat = new PaymentPlatform { Name = "微信支付", Code = "WECHAT", IconUrl = "/icons/wechat.svg" };
        context.PaymentPlatforms.AddRange(alipay, wechat);
        context.SaveChanges();

        // 资金账户
        context.FundAccounts.AddRange(
            new FundAccount { Name = "支付宝余额", PlatformId = alipay.Id, AccountType = AccountType.Balance },
            new FundAccount { Name = "余额宝", PlatformId = alipay.Id, AccountType = AccountType.YuEBao },
            new FundAccount { Name = "微信零钱", PlatformId = wechat.Id, AccountType = AccountType.LingQian },
            new FundAccount { Name = "微信零钱通", PlatformId = wechat.Id, AccountType = AccountType.LingQianTong }
        );

        // 消费分类 (一级)
        var categories = new[]
        {
            new BillCategory { Name = "餐饮", Icon = "restaurant", SortOrder = 1 },
            new BillCategory { Name = "交通", Icon = "transportation", SortOrder = 2 },
            new BillCategory { Name = "购物", Icon = "shopping_bag", SortOrder = 3 },
            new BillCategory { Name = "娱乐", Icon = "sports_esports", SortOrder = 4 },
            new BillCategory { Name = "居住", Icon = "home", SortOrder = 5 },
            new BillCategory { Name = "医疗", Icon = "local_hospital", SortOrder = 6 },
            new BillCategory { Name = "教育", Icon = "school", SortOrder = 7 },
            new BillCategory { Name = "通讯", Icon = "phone", SortOrder = 8 },
            new BillCategory { Name = "服饰", Icon = "checkroom", SortOrder = 9 },
            new BillCategory { Name = "美容", Icon = "spa", SortOrder = 10 },
            new BillCategory { Name = "运动", Icon = "fitness_center", SortOrder = 11 },
            new BillCategory { Name = "数码", Icon = "devices", SortOrder = 12 },
            new BillCategory { Name = "旅行", Icon = "flight", SortOrder = 13 },
            new BillCategory { Name = "社交", Icon = "groups", SortOrder = 14 },
            new BillCategory { Name = "宠物", Icon = "pets", SortOrder = 15 },
            new BillCategory { Name = "汽车", Icon = "directions_car", SortOrder = 16 },
            new BillCategory { Name = "转账", Icon = "swap_horiz", SortOrder = 17 },
            new BillCategory { Name = "工资", Icon = "payments", SortOrder = 18 },
            new BillCategory { Name = "理财", Icon = "trending_up", SortOrder = 19 },
            new BillCategory { Name = "其他", Icon = "more_horiz", SortOrder = 99 },
        };
        context.BillCategories.AddRange(categories);
        context.SaveChanges();

        // 分类规则 (关键词 -> 分类)
        var otherCategory = categories.Last();
        var rules = new List<CategoryRule>();

        void AddRule(int categoryIndex, MatchField field, string pattern, int priority = 0)
        {
            rules.Add(new CategoryRule
            {
                CategoryId = categories[categoryIndex].Id,
                MatchField = field,
                MatchPattern = pattern,
                Priority = priority
            });
        }

        // 餐饮
        AddRule(0, MatchField.ProductName, "美团"); AddRule(0, MatchField.ProductName, "饿了么");
        AddRule(0, MatchField.Counterparty, "餐"); AddRule(0, MatchField.Counterparty, "饭");
        AddRule(0, MatchField.ProductName, "肯德基"); AddRule(0, MatchField.ProductName, "麦当劳");
        AddRule(0, MatchField.ProductName, "星巴克"); AddRule(0, MatchField.ProductName, "瑞幸");
        // 交通
        AddRule(1, MatchField.ProductName, "滴滴"); AddRule(1, MatchField.Counterparty, "地铁");
        AddRule(1, MatchField.Counterparty, "公交"); AddRule(1, MatchField.ProductName, "高德");
        AddRule(1, MatchField.ProductName, "铁路"); AddRule(1, MatchField.ProductName, "航空");
        // 购物
        AddRule(2, MatchField.ProductName, "淘宝"); AddRule(2, MatchField.ProductName, "京东");
        AddRule(2, MatchField.ProductName, "拼多多"); AddRule(2, MatchField.ProductName, "天猫");
        AddRule(2, MatchField.ProductName, "盒马"); AddRule(2, MatchField.ProductName, "超市");
        // 娱乐
        AddRule(3, MatchField.ProductName, "电影"); AddRule(3, MatchField.ProductName, "游戏");
        AddRule(3, MatchField.ProductName, "KTV"); AddRule(3, MatchField.ProductName, "网易云");
        // 居住
        AddRule(4, MatchField.ProductName, "房租"); AddRule(4, MatchField.ProductName, "物业");
        AddRule(4, MatchField.ProductName, "水电"); AddRule(4, MatchField.ProductName, "燃气");
        // 医疗
        AddRule(5, MatchField.Counterparty, "医院"); AddRule(5, MatchField.ProductName, "药");
        // 通讯
        AddRule(7, MatchField.ProductName, "话费"); AddRule(7, MatchField.Counterparty, "移动");
        AddRule(7, MatchField.Counterparty, "联通"); AddRule(7, MatchField.Counterparty, "电信");
        // 转账
        AddRule(16, MatchField.ProductName, "转账", 10);

        context.CategoryRules.AddRange(rules);
        context.SaveChanges();
    }
}
