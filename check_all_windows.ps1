Add-Type -AssemblyName System.Windows.Forms

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host "=== Screen Info ==="
Write-Host "Bounds.Bottom: $($screen.Bounds.Bottom)"
Write-Host "WorkingArea.Bottom: $($screen.WorkingArea.Bottom)"
Write-Host "Gap (Windows Taskbar + any AppBar): $($screen.Bounds.Bottom - $screen.WorkingArea.Bottom)"

Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class Win32Check {
    [DllImport("user32.dll")]
    public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

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
Write-Host "=== Windows near bottom (Top > 1300) ==="

[Win32Check]::EnumWindows({
    param($hwnd, $lParam)

    if ([Win32Check]::IsWindowVisible($hwnd)) {
        $rect = New-Object Win32Check+RECT
        [Win32Check]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

        # 화면 하단 근처에 있는 창만 표시
        if ($rect.Top -gt 1300 -and $rect.Top -lt 1500) {
            $sb = New-Object System.Text.StringBuilder 256
            [Win32Check]::GetWindowText($hwnd, $sb, 256) | Out-Null
            $title = $sb.ToString()

            $classSb = New-Object System.Text.StringBuilder 256
            [Win32Check]::GetClassName($hwnd, $classSb, 256) | Out-Null
            $className = $classSb.ToString()

            Write-Host "---"
            Write-Host "Title: $title"
            Write-Host "Class: $className"
            Write-Host "Top: $($rect.Top), Bottom: $($rect.Bottom), Height: $($rect.Bottom - $rect.Top)"
        }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
