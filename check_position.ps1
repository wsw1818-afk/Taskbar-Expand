Add-Type -AssemblyName System.Windows.Forms

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host "=== Screen Info ==="
Write-Host "Bounds: $($screen.Bounds)"
Write-Host "WorkingArea: $($screen.WorkingArea)"
Write-Host "Bounds.Bottom: $($screen.Bounds.Bottom)"
Write-Host "WorkingArea.Bottom: $($screen.WorkingArea.Bottom)"
Write-Host "Windows Taskbar Height: $($screen.Bounds.Bottom - $screen.WorkingArea.Bottom)"

# TaskbarExpand 창 찾기
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class Win32 {
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

Write-Host ""
Write-Host "=== TaskbarExpand Window ==="

$found = $false
[Win32]::EnumWindows({
    param($hwnd, $lParam)

    if ([Win32]::IsWindowVisible($hwnd)) {
        $sb = New-Object System.Text.StringBuilder 256
        [Win32]::GetWindowText($hwnd, $sb, 256) | Out-Null
        $title = $sb.ToString()

        if ($title -like "*TaskbarExpand*" -or $title -like "*Taskbar Expand*") {
            $rect = New-Object Win32+RECT
            [Win32]::GetWindowRect($hwnd, [ref]$rect) | Out-Null
            Write-Host "Title: $title"
            Write-Host "Left: $($rect.Left)"
            Write-Host "Top: $($rect.Top)"
            Write-Host "Right: $($rect.Right)"
            Write-Host "Bottom: $($rect.Bottom)"
            Write-Host "Width: $($rect.Right - $rect.Left)"
            Write-Host "Height: $($rect.Bottom - $rect.Top)"
            $script:found = $true
        }
    }
    return $true
}, [IntPtr]::Zero)

if (-not $found) {
    Write-Host "TaskbarExpand window not found"
}
