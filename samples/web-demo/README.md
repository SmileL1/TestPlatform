# Web 自动化测试 · 静态 demo 靶子

一个零依赖、零构建的「下单」网页，用作 TestPlatform **Web 网页端自动化测试**的被测目标（SUT）。
纯静态、无后端，校验逻辑全在前端 JS。

## 启动

任选一种把本目录起成静态服务（端口用 3000，正好匹配前端 URL 占位）：

```powershell
# 方式一：Node
npx serve -l 3000 samples/web-demo

# 方式二：Python
python -m http.server 3000 --directory samples/web-demo
```

然后浏览器打开 `http://localhost:3000` 确认页面可交互。

## 页面元素（稳定 id，便于选择器定位）

| 元素 | selector | 说明 |
|------|----------|------|
| 用户名 | `#username` | 文本框 |
| 密码 | `#password` | 密码框 |
| 商品 | `#product` | 下拉：苹果(库存10)/香蕉(库存5)/橙子(缺货0) |
| 数量 | `#qty` | 数字框 |
| 同意条款 | `#agree` | 复选框 |
| 提交按钮 | `#submit`（文字「提交订单」）| |
| 成功横幅 | `#result` | 绿色，「下单成功！订单号 …」 |
| 错误横幅 | `#error` | 红色，失败原因 |

## 在 TestPlatform 里建一个 Web 场景

1. 新建场景 → 类型选 **🌐 Web 网页**，起始 URL 填 `http://localhost:3000`。
2. 目标描述（示例）：

   > 这是一个下单页面。请用 browser_fill 在 #username 填「alice」、在 #password 填「123456」，
   > 用 browser_click 勾选 #agree，把 #qty 设为 2，点击「提交订单」，确认页面出现「下单成功」。

3. 验证条件（可选）：`页面出现「下单成功」`、`无错误提示`。
4. 以 **AI 模式**运行，在监控页观察 `browser_scan → browser_fill → browser_click_text(提交订单) → done`。

## 制造失败用例（验证判定 + AI 截图验证）

- 选「橙子（缺货）」或数量填很大 → 提交后 `#error` 显示库存不足；
- 不勾选 `#agree` → 提示需同意条款。

开启「AI 截图验证」时，失败界面（红色错误条）应被多模态模型判为不通过。
