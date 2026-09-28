# 移动端浏览器显示问题修复说明

## 问题概述

在移动端浏览器（特别是 iOS Safari 和 Android Chrome）中，应用存在以下渲染问题：

1. **Modal 弹窗背景遮罩失效** - 半透明背景无法正确显示，底层内容可见并产生文字重叠
2. **Matrix 表格文字重叠** - 多维月度分析矩阵中行名称与数据值发生重叠
3. **页面头部区域文字重叠** - 部分页面顶部出现日期/标题文字重叠
4. **Responsive Cards 转换异常** - 表格转卡片模式时字段内容溢出

## 根本原因分析

### 1. Modal 背景遮罩问题
- **原因**：`color-mix(in srgb, var(--color-scrim) 32%, transparent)` 函数在某些移动浏览器中支持不完整
- **影响**：模态框的半透明背景无法正确渲染，导致底层内容可见

### 2. Matrix 表格 sticky 定位问题
- **原因**：`position: sticky` 在 `overflow-x-auto` 容器内的兼容性问题，加上 `min-w-max` 导致的布局计算异常
- **影响**：表格首列固定功能失效，行名称与数据值重叠

### 3. Mobile Appbar 层级问题
- **原因**：`position: sticky` 的 `.mobile-appbar` 在某些滚动状态下未能正确保持 z-index 层级
- **影响**：页面内容可能出现在 appbar 上方

### 4. Responsive Cards 布局问题
- **原因**：`flex` 布局在窄屏下的空间分配问题，`min-width` 未正确设置
- **影响**：卡片模式下列内容溢出或重叠

## 修复方案

### 修复 1: Modal 背景遮罩兼容性
**文件**: `src/MinGo.MyBillBook/wwwroot/app.css`

```css
.modal-root,
.md-dialog-scrim {
    /* Fallback for browsers that don't support color-mix */
    background: rgba(0, 0, 0, 0.32);
    background: color-mix(in srgb, var(--color-scrim) 32%, transparent);
    /* Ensure modal covers full viewport including dynamic toolbar areas */
    min-height: 100vh;
    min-height: 100dvh;
    /* Prevent content from escaping modal bounds */
    contain: layout style;
}
```

**说明**：
- 添加 `rgba(0, 0, 0, 0.32)` 作为 `color-mix()` 的 fallback
- 使用 `100dvh` 确保模态框覆盖动态视口高度（包括地址栏）
- 添加 `contain: layout style` 防止内容溢出

### 修复 2: Modal 滚动锁定
**文件**: `src/MinGo.MyBillBook/wwwroot/modal-helper.js` (新建)

```javascript
window.ModalHelper = {
    open: function() {
        document.body.classList.add('modal-open');
        // 保存并锁定滚动位置
    },
    close: function() {
        document.body.classList.remove('modal-open');
        // 恢复滚动位置
    }
};
```

**说明**：
- 自动检测模态框的添加/移除
- 打开模态框时锁定 body 滚动
- 关闭模态框时恢复滚动位置

### 修复 3: Matrix 表格 Sticky 定位改进
**文件**: `src/MinGo.MyBillBook/wwwroot/app.css`

```css
@media (max-width: 767px) {
    .matrix-scroll table th:first-child,
    .matrix-scroll table td:first-child {
        position: -webkit-sticky;
        position: sticky;
        left: 0;
        z-index: 2;
        background-color: var(--color-surface-container);
        border-right: 1px solid var(--color-outline-variant);
        box-shadow: none;
        min-width: 6rem;
        max-width: 10rem;
    }
}
```

**说明**：
- 添加 `-webkit-sticky` 前缀以支持旧版浏览器
- 使用 `border-right` 替代 `box-shadow` 以提高兼容性
- 设置明确的 `min-width` 和 `max-width` 防止文字重叠

### 修复 4: Mobile Appbar 层级确保
**文件**: `src/MinGo.MyBillBook/wwwroot/app.css`

```css
.mobile-appbar {
    z-index: 40;
    isolation: isolate;
}
```

**说明**：
- 提高 z-index 确保 appbar 在所有页面内容之上
- 添加 `isolation: isolate` 创建独立的堆叠上下文

### 修复 5: Responsive Cards 溢出防止
**文件**: `src/MinGo.MyBillBook/wwwroot/app.css`

```css
@media (max-width: 767px) {
    table.responsive-cards td {
        overflow: hidden;
    }
    
    table.responsive-cards td::before {
        flex-shrink: 0;
    }
    
    table.responsive-cards td > * {
        min-width: 0;
        flex-shrink: 1;
    }
}
```

**说明**：
- 确保 flex 子元素可以正确收缩
- 设置 `min-width: 0` 允许内容缩小到容器宽度
- 添加 `overflow: hidden` 防止内容溢出

## 文件变更清单

1. **src/MinGo.MyBillBook/wwwroot/app.css**
   - 添加 Mobile Rendering Fixes 部分（约 150 行新 CSS）
   - 修复 modal、matrix table、appbar、responsive cards 的移动端兼容性问题

2. **src/MinGo.MyBillBook/wwwroot/modal-helper.js** (新建)
   - 创建模态框辅助脚本
   - 实现 body 滚动锁定功能
   - 使用 MutationObserver 自动检测模态框状态

3. **src/MinGo.MyBillBook/Components/App.razor**
   - 添加 modal-helper.js 脚本引用

## 测试建议

### 测试设备
- iOS Safari (iPhone/iPad)
- Android Chrome
- 其他移动浏览器（Firefox Mobile、Samsung Internet 等）

### 测试场景
1. **Modal 测试**
   - 打开 Bills 页面的账单详情模态框
   - 打开 Merchants 页面的编辑商户模态框
   - 验证背景遮罩是否正确显示
   - 验证背景内容是否被正确遮挡
   - 验证模态框打开时背景是否无法滚动

2. **Matrix Table 测试**
   - 打开 Analysis 页面的多维月度分析
   - 切换到"商户"维度
   - 横向滚动表格
   - 验证首列是否正确固定
   - 验证行名称与数据值是否重叠

3. **Mobile Appbar 测试**
   - 滚动页面内容
   - 验证 appbar 是否始终保持在顶部
   - 验证页面内容不会出现在 appbar 上方

4. **Responsive Cards 测试**
   - 在窄屏设备上打开 Bills、Merchants、Jobs 页面
   - 验证表格是否正确转换为卡片模式
   - 验证卡片内容是否溢出或重叠

## 兼容性说明

### 支持的浏览器
- iOS Safari 14+
- Android Chrome 80+
- Firefox Mobile 80+
- Samsung Internet 12+

### 降级策略
- `color-mix()` 不支持时使用 `rgba()` fallback
- `position: sticky` 不支持时使用 `-webkit-sticky` 前缀
- `100dvh` 不支持时使用 `100vh` fallback

## 后续优化建议

1. **性能优化**
   - 考虑使用 CSS `contain: strict` 进一步优化模态框性能
   - 对大型表格使用虚拟滚动

2. **用户体验改进**
   - 添加模态框打开/关闭的过渡动画
   - 优化横向滚动表格的滚动提示

3. **可访问性增强**
   - 确保模态框正确管理焦点
   - 添加适当的 ARIA 属性

## 回滚方案

如果修复引入新问题，可以通过以下方式回滚：

1. 删除 `app.css` 末尾的 "Mobile Rendering Fixes" 部分
2. 删除 `modal-helper.js` 文件
3. 从 `App.razor` 中移除 `modal-helper.js` 的脚本引用

---

**修复日期**: 2026-09-28  
**影响范围**: 所有移动端浏览器  
**风险等级**: 低（仅添加兼容性修复，不改变现有功能逻辑）
