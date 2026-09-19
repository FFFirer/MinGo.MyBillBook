# 个人财务分析

导入支付宝、微信的对账单，自动解析商户，消费分类，资金账户，收入支出，持续更新资金状态。

# 技术架构

基于aspnetcore前后端分离，使用webapi+blazor auto, 前端以WASM为主，后端提供接口服务等。
样式使用tailwindcss(CDN)，风格简洁大气，material 3风格。
数据分析使用DuckDB。数据支持持续更新。应用数据使用Sqlite+EFCore+自动数据库迁移。
单体应用。数据分析使用QuartzNET后台调度运行。前后端执行解耦，完成自动通知前端。
良好的单元测试与集成测试。

# 功能清单

- 导入对账单
- 账单明细查询
- 自动解析对账单数据：资金账户、商户、消费分类、消费金额等
- 支付平台管理
- 资金账户管理
- 消费情况分析
- 报表导出

# 参考项目

- https://gitea.private.fffirer.top:9081/0sdf0/MinGo.Accounting.git
- https://gitea.private.fffirer.top:9081/0sdf0/Accounting