# 交接文档

更新时间：2026-10-28 15:06（Asia/Shanghai）

## 对话来源

- 原对话：`codex://threads/01a17ddf-0123-7a73-9606-683b293ff0db`
- 对话标题：`增加雷蛇耳机 2.4G 电量显示`
- 工作目录：`E:\Codex_work\鼠标耳机控制中心\.worktree\rog-mouse-control-center`
- 交接基线：`81c70de feat: show both device batteries in tray`
- 新版本分支：`release/v1.5`
- 新版本标签：`v1.5`
- 原版保留位置：GitHub `main` / `v1.4`（不移动、不覆盖）

## 产品目标

这是一个面向 Windows 10 的本地 WPF 外设控制中心：

- 控制 ROG 月刃 AimPoint 鼠标的 DPI、轮询率、按键响应和直线修正。
- 通过 2.4 GHz 接收器读取雷蛇旋风黑鲨 V2 专业版耳机电量。
- 不依赖 Razer Synapse、云服务或新运行时依赖。
- 保留托盘、开机自启动、低完整性/单实例保护和设备访问暂停策略。

## 交接时状态

耳机 2.4 GHz 电量读取、双设备托盘图标和战术仪表 UI 的主体已经完成。本轮继续处理用户提出的
“切边显示不全”“整体 UI 细节”“轮询率与按键响应间距”和“耳机刷新按钮下边显示不完整”问题。
改动已通过构建、测试和截图检查，并发布为 `v1.5`；原版 `v1.4` 保留在 GitHub `main`。

本版本包含文件：

- `src/App.cs`
- `src/Connection.cs`
- `src/HeadsetController.cs`
- `src/HeadsetPageState.cs`
- `src/MainController.cs`
- `ui/MainWindow.xaml`
- `HANDOFF.md`

## 本轮完成内容

### 状态与数据一致性

- 鼠标页不再用静态 `CONNECTED/ONLINE` 冒充在线状态；标题、设备栏和状态点统一由最近一次
  实际读取决定，区分 `STANDBY`、`NO REPLY` 和 `OFFLINE`。
- 鼠标和耳机电量同时显示在标题栏和托盘提示中；没有真实读数时显示 `--`。
- 耳机页把“连接方式”“电池状态”“充电状态”拆开，失败时显示无数据，不把无响应误画成 0%。
- 耳机状态颜色统一使用设计令牌：绿色正常、琥珀色无响应、红色接收器/打开失败。

### UI 裁切修复

- 全局 `TextFormattingMode` 从 `Display` 改为 `Ideal`，修复窄窗口下 WPF 把中文标签只渲染出
  顶部笔画的问题，例如 `轮询率`、`直线修正`。
- 鼠标阶位按钮改为短标签 `3 · 1,200`，避免最窄工作区裁掉末尾数字；完整含义放入 Tooltip。
- 阶位、轮询率、按键响应分别使用专用样式，消除动态按钮原有的最小宽度溢出。
- 快捷 DPI 五个按钮改为 `UniformGrid` 等宽布局，窄窗口不再把 `1600/2400/3200` 插到面板外。
- 耳机 `ACCESS POLICY` 面板改为更紧凑的两行文案，避免底部明细被面板裁切。
- 鼠标页降低了固定读数行高度并收紧控件间距，最小尺寸下快捷 DPI 行保持完整可见。
- 轮询率按钮与“按键响应 / MS”之间增加 14 px 分组间距，避免两段配置挤在一起。
- 耳机刷新按钮增加底部安全边距，修复按钮下边框和底部一像素被父布局裁掉的问题。
- 删除了未编译完成的 `--probe-ui` 调试探针，保留可用的 `--shot-demo` 截图入口。

## 关键代码位置

- 雷蛇协议与设备访问：
  - `src/RazerBlackSharkProtocol.cs`
  - `src/RazerHidDevice.cs`
  - `src/HeadsetService.cs`
  - `src/HeadsetTransactionPlan.cs`
- 耳机页面状态与 UI：
  - `src/HeadsetPageState.cs`
  - `src/HeadsetController.cs`
- 鼠标状态、动态按钮和页面切换：
  - `src/MainController.cs`
- 双环托盘图标与菜单：
  - `src/TrayIcon.cs`
- WPF 界面和设计令牌：
  - `ui/MainWindow.xaml`
  - `DESIGN.md`
  - `UX-CONTRACT.md`
- 启动、单实例、托盘和截图入口：
  - `src/App.cs`

## 验证结果

### 自动测试与构建

```powershell
.\build\test.ps1
.\build\build.ps1
```

- 协议测试：13/13 通过。
- 服务、页面和窗口测试：12/12 通过。
- 构建成功：`dist\ROGMouseControlCenter.exe`。
- 程序可正常启动，托盘注册成功。

### 视觉检查

已检查以下尺寸的鼠标页和耳机页：

- `1200x780`：设计尺寸。
- `1040x680`：最小高度压力场景。
- `1008x712`：1024 宽工作区压力场景。
- `1200x712`：1366x768 桌面实际适配高度。

截图位于 `_test\fix3-*.png`、`_test\final2-*.png` 和 `_test\ui2-*.png`。最终确认：

- 鼠标阶位、`1000 Hz` 和五个快捷 DPI 按钮均无裁切。
- 轮询率与按键响应分组间距清晰，鼠标页在 `1008x712` 下仍无重叠。
- 耳机访问策略说明和底部按钮均无裁切。
- 耳机刷新按钮四边边框完整可见，不再出现底部缺边。
- 页码、状态标签、底部遥测和托盘动作均保持可见。

### 实机状态

最终实机检查时接收器在线，但耳机返回“无响应”，日志为：

```text
headset no response: 耳机无响应（接收器在线）
```

因此真实截图正确显示 `NO RESPONSE` 和 `--`，没有伪造电量。原对话早先曾成功读到 `74%`，
说明协议路径可用；最终硬件验收前需要先打开耳机并确认 2.4 GHz 连接。

## 已知限制

- 不支持蓝牙耳机功能，这是用户明确要求排除的范围。
- 耳机轮询只在耳机页选中、窗口可见且非全屏游戏时进行；收起窗口时走托盘低频刷新策略。
- 鼠标硬件写入无法在设备休眠时验证，必须用真实设备做最终回读。
- 项目使用 Windows 自带的 .NET Framework 4.x `csc.exe`，代码保持 C# 5 语法，不新增 NuGet。
- 自动测试不包含 WPF 像素级断言；UI 回归依赖 `--shot-demo` 输出的人工检查。

## 下一步

1. 打开耳机并确认 2.4 GHz 接收器连接，运行：

   ```powershell
   $exe = (Resolve-Path '.\dist\ROGMouseControlCenter.exe').Path
   $out = (Join-Path (Resolve-Path '_test').Path 'headset-acceptance.png')
   Start-Process -FilePath $exe -ArgumentList @('--shot-headset', $out) -Wait
   ```

2. 确认耳机页出现真实百分比、充电状态和最后读取时间；如果仍无响应，查看
   `%LOCALAPPDATA%\ROGMouse\tray.log`。
3. 用真实鼠标验证 DPI、轮询率、按键响应和直线修正写入后能回读。
4. 新版本位于 `release/v1.5` 和标签 `v1.5`；原版 `main` / `v1.4` 保持不变。

## 截图命令

`--shot-demo` 会使用固定演示数据，适合在没有连接设备时审查布局。GUI 程序需要用
`Start-Process -Wait`，否则 PowerShell 会在 PNG 写完前继续执行。

```powershell
$exe = (Resolve-Path '.\dist\ROGMouseControlCenter.exe').Path
Start-Process -FilePath $exe -WorkingDirectory (Get-Location).Path -Wait `
  -ArgumentList @('--shot-demo', '_test\mouse-review.png', 'mouse', '1040x680')
Start-Process -FilePath $exe -WorkingDirectory (Get-Location).Path -Wait `
  -ArgumentList @('--shot-demo', '_test\headset-review.png', 'headset', '1040x680')
```
