# 两端共用的测试用例

这里的 JSON 由两边的测试一起读：

- Windows：`windows/tests/Xitu.Tests/SharedCaseTests.cs`（xunit）
- Linux：`linux/tests/test_shared_cases.py`（pytest；`layout.js` 的函数用 node 跑）

改规则时先改这里的用例，两边的测试都会跟着跑。只改了一边，另一边的检查就会红。

- `clipboard.json`：剪贴板里的位图收不收（和设置 `Collect` 一起）
- `layout.json`：绳上能挂几张、顶边感应段、保存时间的显示文字
