# 小黑手帐 · desktop-plan-pets

一只可以记待办的 Windows 桌面宠物。点一下小黑，日历手帐就在它上方打开。红色角标贴着右耳，显示当天还没完成的任务数。

![小黑的尾巴变小猫](outputs/尾巴变小猫.gif)

## 运行

下载 [小黑手帐.exe](outputs/小黑手帐.exe?raw=1)，在 Windows 桌面双击运行。当前版本为 **1.10**。任务、周计划和窗口设置保存在当前 Windows 用户的 `%LOCALAPPDATA%\XiaoheiPlanner\planner.xml`。移动程序或更新版本后，仍会读到原来的手帐。

## 使用

- **记任务**　点击小黑，选中日期，点「＋ 新建任务」直接输入。每个任务旁的「＋ 子任务」可以追加计划，主任务和子任务都能打勾。
- **看提醒**　每天 18:00，当天未完成的任务会标红并顺延至次日。周五 18:00，所有未完成任务都会标红。日历和「每周汇总」都会把红框任务排在前面。
- **写周计划**　切换到「每周汇总」，可以查看一周的完成情况，也可以点「写周计划」记录安排。
- **调大小和位置**　拖动小黑可以移动它。右键选择「桌宠大小」，或按住 Ctrl 滚动鼠标滚轮微调。拖动手帐任意一边或一角可调整窗口大小，双击边缘可自动适配。

## 从源码构建

在装有 64 位 .NET Framework 4.x 和 WPF 的 Windows 上，从仓库根目录打开 PowerShell，运行以下命令。

```powershell
.\src\Build.ps1
```

编译结果位于 `outputs\小黑手帐.exe`。若桌宠正在运行，可用 `-Destination` 指定其他文件名。应用源码、图标和构建脚本在 `src`，回归检查在 `tests`，图标和动画预览的生成程序在 `tools`。

例如，下面的命令会编译并运行任务顺延检查，使用 `work` 下的临时数据。

```powershell
New-Item -ItemType Directory -Force .\work | Out-Null
.\src\Build.ps1 -Destination "$PWD\work\ScheduleCheck.exe" -Main Xiaohei.ScheduleCheck -ExtraSource "$PWD\tests\ScheduleCheck.cs" -Console
.\work\ScheduleCheck.exe
```
