# MinGo.MyBillBook 移动端列表设计规范

## 1. 设计原则

- **信息密度优先**：移动端屏幕空间有限，每条记录只展示用户决策所需的最少字段
- **去除冗余装饰**：不使用 label 文字、边框、圆角卡片等视觉噪音
- **分隔线替代间距**：列表项之间零间距，用 1px 分隔线区分边界
- **桌面端保留完整表格**：移动端使用独立 card 布局，桌面端（≥768px）保持 table 视图，互不干扰

## 2. 断点

| 断点 | 宽度 | 布局 |
|------|------|------|
| 移动端 | < 768px | `.bill-card-list` 列表视图 |
| 桌面端 | ≥ 768px | `table.responsive-cards` 表格视图 |

切换方式：CSS `@media` + `:has()` 选择器，桌面端隐藏 card list，移动端隐藏 table tbody。

## 3. 列表项结构

每条记录采用 **两行式** 布局：

```
┌─────────────────────────────────────┐
│  商户名称（左，可截断）    -66.38（右）│  ← 第一行：主信息 + 金额
│  2025-12-21 11:23  [购物]            │  ← 第二行：辅助信息 + 分类标签
└─────────────────────────────────────┘
              1px 分隔线
┌─────────────────────────────────────┐
│  ...                                 │
└─────────────────────────────────────┘
```

### 第一行（主信息行）

| 元素 | 对齐 | 样式 |
|------|------|------|
| 商户名称 | 左对齐，flex:1，min-width:0 | `--text-body-large`，font-weight:500，`--color-on-surface`，超长截断省略号 |
| 金额 | 右对齐，flex-shrink:0 | `--text-title-medium`，font-weight:600，tabular-nums 等宽数字 |
| 金额颜色 | — | 收入 `--color-success`（绿色），支出 `--color-error`（红色），带 +/- 前缀 |

### 第二行（辅助信息行）

| 元素 | 样式 |
|------|------|
| 日期时间 | `--text-body-small`，`--color-on-surface-variant`，格式 `yyyy-MM-dd HH:mm` |
| 分类标签 | pill 胶囊样式：`--text-label-small`，font-weight:500，`--color-primary-container` 背景 + `--color-on-primary-container` 文字，border-radius:9999px，padding: 2px 8px |

## 4. 间距与分隔

| 属性 | 值 | 说明 |
|------|-----|------|
| 列表项间距 | 0 | 项之间无 gap |
| 项内上下 padding | 0.75rem（12px） | 保证点击区域足够 |
| 项内左右 padding | 1rem（16px） | 与页面边距对齐 |
| 第一行与第二行间距 | 0.375rem（6px） | 紧凑但不拥挤 |
| 第二行内元素间距 | 0.5rem（8px） | 日期与分类标签之间 |
| 分隔线 | 1px solid `--color-outline-variant` | 仅用于 `:not(:last-child)` 的底部 |

## 5. 交互

| 状态 | 效果 |
|------|------|
| 默认 | cursor:pointer |
| 点击/按压 | background-color 变为 `--color-surface-container-high`，transition: 0.15s ease |

## 6. 响应式切换实现

```css
/* 桌面端：隐藏 card list */
@media (min-width: 768px) {
    .bill-card-list { display: none; }
}

/* 移动端：隐藏 table tbody（card list 紧邻其后） */
@media (max-width: 767px) {
    .overflow-x-auto:has(+ .bill-card-list) table.responsive-cards tbody {
        display: none;
    }
}
```

## 7. 设计规范扩展指南

当为其他列表页（如商户、标签、对账等）添加移动端适配时，遵循以下模式：

1. **保留桌面端 table**，不删除原有 `<table>` 结构
2. **在 table 之后**添加独立的移动端列表容器（如 `.xxx-card-list`）
3. **使用 BEM 命名**：`.xxx-card`、`.xxx-card__top`、`.xxx-card__bottom`、`.xxx-card__primary`、`.xxx-card__secondary`
4. **零间距 + 分隔线**：列表容器无 gap，项之间用 `border-bottom` 分隔
5. **两行式布局**：第一行放主信息（标题/名称 + 关键数值），第二行放辅助信息（时间/状态/标签）
6. **使用 CSS 变量**：所有颜色、字号、间距引用 `--color-*`、`--text-*`、`--radius-*` 变量，确保主题切换一致
7. **金额/数值使用 tabular-nums**：保证数字列视觉对齐

## 8. 反模式（避免）

- 不要在移动端卡片中重复 label 文字（如"日期："、"金额："）
- 不要给每个列表项加边框和圆角（视觉噪音大、间距浪费）
- 不要用 `gap` 分隔列表项（用分隔线更紧凑）
- 不要在移动端隐藏关键数值（金额、状态等必须可见）
- 不要为移动端单独写一套 table responsive-cards CSS（维护成本高，直接用独立 card 布局）
