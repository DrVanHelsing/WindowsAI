# QA helper: drives the running WindowsAI overlay and captures PrintWindow screenshots.
# Usage: app must already be running; then  powershell -File tools\qa-overlay.ps1
# Output: artifacts\qa\overlay-*.png
param([string]$OutDir = "artifacts\qa")

$ErrorActionPreference = "Stop"
New-Item -ItemType Directory -Force $OutDir | Out-Null
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type @"
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

public static class Native
{
    [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdc, uint flags);
    [DllImport("user32.dll")] public static extern void keybd_event(byte vk, byte scan, uint flags, UIntPtr extra);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    public static List<IntPtr> WindowsOf(uint pid)
    {
        var list = new List<IntPtr>();
        EnumWindows((h, l) =>
        {
            uint p;
            GetWindowThreadProcessId(h, out p);
            if (p == pid && IsWindowVisible(h)) list.Add(h);
            return true;
        }, IntPtr.Zero);
        return list;
    }
}
"@

function Send-AltSpace {
    # 0x12 = VK_MENU, 0x20 = VK_SPACE, 2 = KEYEVENTF_KEYUP
    [Native]::keybd_event(0x12, 0, 0, [UIntPtr]::Zero)
    [Native]::keybd_event(0x20, 0, 0, [UIntPtr]::Zero)
    [Native]::keybd_event(0x20, 0, 2, [UIntPtr]::Zero)
    [Native]::keybd_event(0x12, 0, 2, [UIntPtr]::Zero)
}

function Get-OverlayHwnd([uint32]$AppPid) {
    # The overlay is the visible window narrower than ~800px (panel is 420 logical).
    foreach ($h in [Native]::WindowsOf($AppPid)) {
        $r = New-Object Native+RECT
        [Native]::GetWindowRect($h, [ref]$r) | Out-Null
        if (($r.Right - $r.Left) -lt 800 -and ($r.Bottom - $r.Top) -gt 600) { return $h }
    }
    return [IntPtr]::Zero
}

function Save-Window([IntPtr]$Hwnd, [string]$Path) {
    $r = New-Object Native+RECT
    [Native]::GetWindowRect($Hwnd, [ref]$r) | Out-Null
    $w = $r.Right - $r.Left; $h = $r.Bottom - $r.Top
    $bmp = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $hdc = $g.GetHdc()
    [Native]::PrintWindow($Hwnd, $hdc, 3) | Out-Null   # 3 = PW_RENDERFULLCONTENT
    $g.ReleaseHdc($hdc); $g.Dispose()
    $bmp.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png); $bmp.Dispose()
    Write-Host "saved $Path"
}

$proc = Get-Process WindowsAI.App -ErrorAction Stop

# Toggle only if hidden, then poll — the slide-in takes ~300ms and timing varies.
$hwnd = Get-OverlayHwnd $proc.Id
if ($hwnd -eq [IntPtr]::Zero) {
    Send-AltSpace
    for ($i = 0; $i -lt 20 -and $hwnd -eq [IntPtr]::Zero; $i++) {
        Start-Sleep -Milliseconds 250
        $hwnd = Get-OverlayHwnd $proc.Id
    }
}
if ($hwnd -eq [IntPtr]::Zero) { throw "overlay window not found/visible" }
Start-Sleep -Milliseconds 600   # let the entrance animation settle

# 1) Empty state (recents + TRY)
Save-Window $hwnd (Join-Path $OutDir "overlay-empty.png")

# 2) Launcher query → grouped results ("blue" hits Bluetooth settings + apps/files)
[System.Windows.Forms.SendKeys]::SendWait("blue")
Start-Sleep -Milliseconds 1500
Save-Window $hwnd (Join-Path $OutDir "overlay-search.png")

# 3) Prompt-like query → Ask AI row selected
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")    # first Esc clears the query
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("how do i rename a file in powershell")
Start-Sleep -Milliseconds 1500
Save-Window $hwnd (Join-Path $OutDir "overlay-prompt.png")

# Dismiss: clear text, then hide
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
Start-Sleep -Milliseconds 300
[System.Windows.Forms.SendKeys]::SendWait("{ESC}")
