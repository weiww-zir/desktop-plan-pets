param([switch]$OpenPlanner)
$ErrorActionPreference = 'Stop'
$staged = Get-Item -LiteralPath (Join-Path $PSScriptRoot '小黑手帐-更新.exe')
$target = Get-Item -LiteralPath (Join-Path $PSScriptRoot '..\outputs\小黑手帐.exe')
if ($staged.VersionInfo.ProductName -ne '小黑手帐' -or $staged.VersionInfo.FileVersion -ne '1.18.0.0') {
    throw '未找到已验证的新版桌宠。'
}
$instances = @(Get-Process -Name '小黑手帐' -ErrorAction SilentlyContinue)
if (@($instances | Where-Object { $_.Path -ne $target.FullName }).Count -gt 0) {
    throw '另一个目录里也有小黑在运行，请在它的右键菜单中退出后再更新。'
}
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class XiaoheiWindowUpdate
{
    private delegate bool Enumerate(IntPtr window, IntPtr parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(Enumerate callback, IntPtr parameter);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out int processId);
    [DllImport("user32.dll", CharSet=CharSet.Unicode)] private static extern int GetWindowText(IntPtr window, StringBuilder text, int capacity);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr window, uint command);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] private static extern bool IsWindowEnabled(IntPtr window);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    public static void ClosePet(int processId)
    {
        var pets = new List<IntPtr>();
        bool editing = false;
        EnumWindows(delegate(IntPtr w, IntPtr unused) {
            int ownerId;
            GetWindowThreadProcessId(w, out ownerId);
            if (ownerId != processId || !IsWindowVisible(w)) return true;
            var title = new StringBuilder(256);
            GetWindowText(w, title, title.Capacity);
            if (title.ToString() == "小黑手帐") {
                if (!IsWindowEnabled(w)) editing = true;
                IntPtr owner = GetWindow(w, 4);
                if (owner == IntPtr.Zero || !IsWindowVisible(owner)) pets.Add(w);
            }
            return true;
        }, IntPtr.Zero);
        if (editing) throw new InvalidOperationException("小黑的编辑窗口仍开着，请先保存或关闭此窗口。");
        if (pets.Count != 1) throw new InvalidOperationException("未定位到正在运行的桌宠窗口。");
        if (!PostMessage(pets[0], 0x0010, IntPtr.Zero, IntPtr.Zero)) throw new InvalidOperationException("桌宠未收到退出请求。");
    }
}
'@
foreach ($instance in $instances) {
    [XiaoheiWindowUpdate]::ClosePet($instance.Id)
    if (!$instance.WaitForExit(8000)) { throw '桌宠仍在使用中，此次更新已暂停。' }
}
$backup = Join-Path $PSScriptRoot ('小黑手帐-原版-' + [Guid]::NewGuid().ToString('N') + '.exe')
[System.IO.File]::Replace($staged.FullName, $target.FullName, $backup)
$launch = @{ FilePath = $target.FullName; PassThru = $true }
if ($OpenPlanner) { $launch.ArgumentList = '--planner' }
$runningPet = Start-Process @launch
if (!$runningPet.WaitForInputIdle(10000)) { throw '桌宠未在预期时间内完成初始化。' }
Start-Sleep -Milliseconds 800
$runningPet.Refresh()
if ($runningPet.HasExited) { throw '新版桌宠没有保持运行。' }
$runningPet | Select-Object Id, Responding, @{Name='Version';Expression={$_.MainModule.FileVersionInfo.FileVersion}}
