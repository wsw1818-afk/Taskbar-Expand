Add-Type -AssemblyName System.Windows.Forms

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host "=== Screen Info ==="
Write-Host "Bounds.Bottom: $($screen.Bounds.Bottom)"
Write-Host "WorkingArea.Bottom: $($screen.WorkingArea.Bottom)"
Write-Host "Gap: $($screen.Bounds.Bottom - $screen.WorkingArea.Bottom)"

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class Win32Check2 {
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
Write-Host "=== Looking for TaskbarExpand ==="

[Win32Check2]::EnumWindows({
    param($hwnd, $lParam)

    $sb = New-Object System.Text.StringBuilder 256
    [Win32Check2]::GetWindowText($hwnd, $sb, 256) | Out-Null
    $title = $sb.ToString()

    if ($title -eq "Taskbar Expand") {
        $rect = New-Object Win32Check2+RECT
        [Win32Check2]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

        Write-Host "FOUND: $title"
        Write-Host "  Left: $($rect.Left)"
        Write-Host "  Top: $($rect.Top)"
        Write-Host "  Right: $($rect.Right)"
        Write-Host "  Bottom: $($rect.Bottom)"
        Write-Host "  Width: $($rect.Right - $rect.Left)"
        Write-Host "  Height: $($rect.Bottom - $rect.Top)"
        Write-Host ""
        Write-Host "  Expected Top (WorkArea.Bottom - Height): $($screen.WorkingArea.Bottom - ($rect.Bottom - $rect.Top))"
        Write-Host "  Actual Top: $($rect.Top)"
        Write-Host "  Difference: $($rect.Top - ($screen.WorkingArea.Bottom - ($rect.Bottom - $rect.Top)))"
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
