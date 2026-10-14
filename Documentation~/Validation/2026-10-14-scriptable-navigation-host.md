# 脚本化导航真实宿主验证

日期：2026-10-14，Asia/Shanghai。

宿主工程：`E:\Unity\wx_gardenagrown_2\Unity`，团结编辑器 `2022.3.62t6`。
验证目标：从 `Assets/Launcher.unity` 冷启动，执行默认登录流程并进入真正可交互的主界面。
工具分支：`dev_scriptable_navigation`，临时新版 `0.3.0`。

## 隔离方式

宿主保留 Git 包 `com.shthemw.unity-autorun-tool#v0.2.6`，旧 Bridge 版本 `0.1.2`。未更新、移除或改写旧包，未安装新版客户端配置或 Skills。

临时验证 DLL 使用独立程序集 `UnityAutorun.Preview.Editor`，全局工具类型置于 `UnityAutorun.Preview`，公开脚本契约仍为 `UnityAutorun.Scripting`。临时组件导入 `Assets/AutorunTemporaryValidation/Editor`，客户端使用 profile `scriptable-navigation-case`，动态端点位于 `Library/UnityAutorunTool/profiles/scriptable-navigation-case/bridge-endpoint.json`。

## 用例与结果

脚本 ID：`business.launcher-existing-account-to-main`。
参数：`flowTimeoutSeconds=120`、`stableSeconds=0.5`，外层总超时 `150` 秒。
复用基础脚本：`click-button`、`wait-for-ui-state`、`wait`、`screenshot`。

| 检查 | 实际结果 |
| --- | --- |
| 第一轮自测 | 18.322 秒到达登录界面，因生成用例错误解释 `Auth.IsSignIn` 而停止，未点击登录 |
| 修正后自测 | 32.059 秒成功，真实 EventSystem 点击登录一次，主页断言与 PNG 截图通过 |
| 错误起点 | 已在主页时返回 `precondition_failed`，未执行子脚本，保留现场 |
| `run-script` 独立复跑 | 33.130 秒成功，使用已有脚本，源码 SHA256 与复跑前一致 |
| 显式停止基础脚本 | 成功结束并退出 Play Mode，终态跨重载仍可读取 |

最终断言同时检查：实际 `HotEntry.GameReadied=true`、流程为 `Game.Hot.ProcedureGame`、`UIFormOperation` 激活、根 CanvasGroup 完全可见并允许交互、`Collection_ExButton` 能被真实前景指针命中、登录和加载界面关闭、无强制引导或首次过场，并在短暂稳定等待及截图后复查。

宿主可能通过 `Assembly.Load` 加载热更代码。用例从场景中的真实 HotEntry 组件取得所属程序集，只读取经源码确认的公开状态，避免将编辑器类型副本当作运行时实例。脚本日志未打印 UID、密码、token 或会话标识，没有修改账号、服务器或绕过引导；原始游戏截图保留正常可见的界面内容。

## 本次修复

- 增加独立 Bridge profile，并按桥程序集将首次选择保存在 Editor 会话中，跨域重载保持不变。
- 识别 Mono 同进程重复 HTTP 前缀返回的错误码 `400`，自动选择空闲端口，并在重载后保留已选端口。
- 将后台刷新、初始化、Bridge 恢复和显式停止改为主线程 `EditorApplication.update`，避免未聚焦时 Inspector 延迟回调不执行。
- 用 SessionState 保存待刷新请求，防止另一轮编译触发重载后丢失已接受的刷新。
- 修正生成用例对 `IsSignIn` 的解释：此属性表示正在登录，不能证明已认证或没有已有账号；默认认证身份由宿主既定流程决定。
- 更新生成 Skill：状态标志用于起点判断前，必须核实 getter 和赋值语义，不能凭名称添加用户未要求的限制。
- 删除已无调用方的旧导航图路径常量与安装配置助手。

## 日志边界

通过宿主已安装 Unity Agent Bridge 的 `search_logs` 读取实际项目日志，保留完整响应。成功复跑期间存在一项宿主错误：本地赛季配置接口 `/api/config/get_current_season_info` 重试三次后返回 HTTP 404。主界面目标仍通过；没有修改该宿主接口，也不据此宣称整个项目日志无错误。

## 清理与保留

临时 DLL、生成用例及宿主测试截图已清理，进程环境已恢复，编辑器回到测试前的停止状态，旧 Bridge 在原端口 `17331` 恢复为 `0.1.2`。

`Packages/manifest.json`、`Packages/packages-lock.json` 和宿主 `.mcp.json` 的 SHA256 与测试前完全一致；原有 9 项已修改资源内容保持一致，原有子模块状态未改动。

完整本地证据保存在工具仓库 `bin/HostValidation~/evidence/`，生成用例源码保存在 `bin/HostValidation~/case/LaunchGameToMainScreen.cs`。成功截图为 `main-screen-selftest.png` 和 `main-screen-run-script.png`。
