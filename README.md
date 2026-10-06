# HDR 控制中心

Windows 系统托盘工具，用于快捷控制显示器 HDR、SDR 内容亮度、杜比视界和 NVIDIA RTX 视频增强。

[下载 beta1.0](https://github.com/Forejant/HDR-Control-Center/releases/tag/beta1.0)

## 功能

- 选择显示器，并记住上次选择。
- 切换 Windows HDR，滑动实时调节 SDR 内容亮度。
- 切换杜比视界；可启用“始终关闭杜比视界”，在显示器重新连接或点亮后执行开启再关闭的恢复操作。
- 快捷控制 NVIDIA RTX Super Resolution 和视频 HDR，显示可读取的开关及活动状态。
- 登录 Windows 时启动，常驻系统托盘，单击图标打开或收起控制中心。
- 点击其他窗口自动收起，后台操作继续完成。
- 跟随系统深浅主题，圆角毛玻璃界面，开关过渡与加载动画。

## 使用

1. 下载发布页中的 `HdrControlCenter-beta1.0.zip`。
2. 解压到固定目录，运行 `HdrControlCenter.exe`。
3. 选择显示器后使用相应控制项。
4. 需要开机启动时，勾选“登录 Windows 时启动”。

升级前请从旧版托盘右键菜单选择“退出”，再运行新版。关闭面板会保留托盘运行；完全退出请使用托盘菜单。

## 运行要求

- Windows 10/11 x64 与 .NET Framework 4.8，Windows 11 已内置。
- SDR 内容亮度调节需要所选显示器已启用 HDR。
- 杜比视界需要系统、显示器和当前连接支持，并在 Windows HDR 设置中提供对应选项。
- RTX 视频增强需要支持的 NVIDIA 显卡、驱动及 NVIDIA 控制面板。
- 原生毛玻璃需要 Windows 11 22621 或更新版本且系统透明效果开启；不支持时使用实色主题。

杜比视界和 RTX 快捷操作会按需打开对应设置页，完成后关闭由软件自动打开的窗口。活动状态仅在系统或控制面板提供可读取信息时显示，不能从开关是否开启推断实际生效状态。

## 源码构建

在仓库目录中用 Windows PowerShell 执行：

```powershell
.\build.ps1
```

构建使用系统自带的 .NET Framework C# 编译器，无需安装 .NET SDK。完成后运行仓库目录中的 `HdrControlCenter.exe`。

## 配置与反馈

显示器选择和软件设置保存在 `%LOCALAPPDATA%\HdrControlCenter`，升级时继续沿用。

测试版的兼容性取决于设备、系统和驱动。反馈问题时请提供 Windows 版本、显卡/驱动版本、显示器型号和复现步骤。本次发布重新编译并打包，未进行新的运行测试。
