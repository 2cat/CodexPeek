# Codex Peek

[![Windows checks](https://github.com/2cat/CodexPeek/actions/workflows/ci.yml/badge.svg)](https://github.com/2cat/CodexPeek/actions/workflows/ci.yml)

Windows 11 上的本地 Codex 状态工具。在任务栏左侧空白区域显示当前工作状态和账号剩余额度，点击展开本机任务列表。

## 产品演示

任务栏左侧显示状态、剩余额度和重置倒计时：

<a href="docs/images/taskbar-demo.png"><img src="docs/images/taskbar-demo.png" width="320" alt="Codex Peek 任务栏状态条：等待确认、剩余额度与重置倒计时"></a>

点击展开，查看并行任务的具体内容，优先处理等待确认的操作：

<a href="docs/images/tasks-demo.png"><img src="docs/images/tasks-demo.png" width="460" alt="Codex Peek 玻璃任务面板：圆角卡片、大字号，一项等待确认、两项正在运行"></a>

截图来自 Windows 11 上当前可运行的版本，任务和额度均为演示数据。使用系统原始分辨率的无损 PNG（560 × 70 / 805 × 917），点击图片可查看原图。

## 功能

- 单任务显示动作和当前内容，如「正在修改 App.cs」「命令 · git status --short」「工具 · 检查状态栏文字显示」。
- 回复时显示最新非空行的文字摘要，长行跟随最新片段更新；没有可用动作内容时显示任务名称，不停留在孤立的「正在整理回复」。
- 多任务显示运行数量；等待确认、等待回复和异常优先提示。
- 空闲时显示最近一次已同步任务的名称，断线时明确显示「状态未同步」。
- 额度显示剩余百分比及实际重置倒计时，例如「剩余 7% · 3天5小时后重置」。
- 点击查看每项任务的动作、最近进展和计时，再进入 Codex 原会话处理。
- 展开面板同时保留最近 3 个已结束任务，显示最近回复摘要和运行时间；正在运行的任务放在上方，历史任务不计入运行数量。
- 任务栏空间不足时收窄或退到托盘；不增加任务栏高度。
- 任务栏入口和展开面板使用系统 Acrylic、圆角与细边框；任务卡片增大字号、保留清晰的键盘焦点，透明效果不可用时使用实色背景。

这是独立开发的本地工具，与 OpenAI 官方产品无隶属关系。桌面任务接入使用 Codex 内部协议，升级后可能需要适配。

## 当前状态

当前 **320 × 40 DIP** 玻璃入口已在开发机的 Windows 11、175% 缩放下部署到日常入口。任务面板宽 460 DIP，没有任何记录时高度为 184 DIP；有任务时显示圆角卡片，最多占屏幕可用高度的 70%。

最近 3 个历史任务和更轻的玻璃底层已包含在当前程序中。本次修复开始菜单打开时按钮读取为空导致入口消失数秒的问题：任务栏没有变化时保留最近可靠的位置。最终 EXE 已正常冷启动、验证单实例限制并部署到项目根目录。README 截图仍为上一版；新底层在较亮背景下的可读性仍需进一步验收。

源码已移除任务面板显示后的重复前台激活请求，避免后台激活未获准时的提示闪烁。该修复产物在开发机启动时被 Windows 应用控制阻止，当前日常程序已恢复到之前可用的版本；新修复尚未生效。真实点击时另采到约 0.2 秒的玻璃材质变化，尚未消除，详见验证记录。

仓库源码、CI 测试通过与某台机器上允许启动需要分别确认。当前没有签名安装包或正式 Release。详见 [验证记录与已知限制](docs/validation.md)。

## 开发与运行

环境要求：

- Windows 11，系统 .NET Framework 4.x 编译器及 WinForms / UI Automation 程序集。
- Node.js 24 或更新版本；数据组件仅使用标准库，没有 npm 项目依赖。
- 已登录并运行的 Codex 桌面应用。
- 通过 npm 安装的 Codex CLI；额度读取目前从 `%APPDATA%\npm\node_modules\@openai\codex` 定位它。使用其他 npm 全局目录时需要适配路径。

在 PowerShell 中克隆和检查：

```powershell
git clone https://github.com/2cat/CodexPeek.git
cd CodexPeek
npm test
.\build.ps1
```

日常运行入口固定为项目根目录的 `CodexPeek.exe`。在本机始终双击 `C:\Users\guoyo\Documents\CodexPeek\CodexPeek.exe`，或同目录的「启动 Codex Peek」快捷方式。

`build.ps1` 编译并运行测试，构建中间文件留在 `build/`，默认保留当前可用的日常程序。`dist/` 不再生成运行入口。

在解锁且无人操作的桌面上，可额外运行 `.\build\LayoutTests.exe --popup-paint`，检查实际合成的玻璃背景是否在连续刷新时保持稳定；该像素检查不包含在默认 CI 中。

只编译、不运行测试程序：

```powershell
.\build.ps1 -BuildOnly
```

首次部署或更新，并自动重启固定入口：

```powershell
.\build.ps1 -Deploy
```

已经完成构建时，也可单独运行 `.\deploy.ps1`。更新流程在根目录替换同一个 EXE；启动失败会原地恢复上一版并重新打开。内部候选和备份不作为日常入口。根目录的 `backend.mjs`、`status.mjs` 保持在 EXE 旁边。程序以当前用户权限运行，不设置开机自启。

点击状态条展开详情，Escape 或点击其他窗口收起，右键状态条或托盘图标退出。快捷方式始终指向根目录；拉取代码或单独构建不会替换日常程序，`-Deploy` 才执行更新。

## 代码与维护

| 文件 | 职责 |
| --- | --- |
| `App.cs`、`app.manifest` | C# WinForms 窗口、托盘、DPI、任务栏避让、会话跳转 |
| `backend.mjs` | 本机任务目录、桌面 IPC、额度请求、重连与生命周期 |
| `status.mjs` | 任务状态归纳、优先级、命令内容和剩余额度文案 |
| `test.mjs` | 数据规则及真实命名管道传输测试，使用隔离的测试数据 |
| `LayoutTests.cs` | 100% / 175% 缩放、图标避让和弹层边界检查 |
| `build.ps1`、`deploy.ps1` | 构建、测试与固定入口更新；启动失败原地恢复 |
| `deploy-check.ps1` | 用隔离进程检查固定入口、失败恢复及缺失构建保护 |

[产品与接入说明](docs/design.md)记录显示规则和接口边界；[验证记录](docs/validation.md)记录已验证行为与待处理问题。

GitHub Actions 在 `main` 推送和 Pull Request 时执行 Windows 构建、数据测试和原生布局测试。它不连接开发者的 Codex 账号，也不执行真实桌面交互验收。更换最终 EXE 后，仍要在目标机器验证冷启动、操作、退出和再次启动。

EXE、快捷方式、构建目录、诊断文件及本机会话数据库不入库。修改功能时先在现有测试边界补充回归用例，验证后提交。日常程序固定在根目录，构建中间文件和备份留在 `build/`。
