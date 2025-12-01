Add-Type -AssemblyName System.Windows.Forms

Add-Type @"
using System;
using System.Runtime.InteropServices;

public class TaskbarFinder {
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

    [DllImport("user32.dll")]
    public static extern bool GetWindowRect(IntPtr hwnd, out RECT lpRect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }
}
"@

$screen = [System.Windows.Forms.Screen]::PrimaryScreen
Write-Host "=== Screen Info ==="
Write-Host "Bounds: $($screen.Bounds)"
Write-Host "WorkingArea: $($screen.WorkingArea)"

Write-Host ""
Write-Host "=== Shell_TrayWnd (Windows Taskbar) ==="
$hwnd = [TaskbarFinder]::FindWindow("Shell_TrayWnd", $null)
Write-Host "Handle: $hwnd"

if ($hwnd -ne [IntPtr]::Zero) {
    $rect = New-Object TaskbarFinder+RECT
    $result = [TaskbarFinder]::GetWindowRect($hwnd, [ref]$rect)
    Write-Host "GetWindowRect result: $result"
    Write-Host "Left: $($rect.Left)"
    Write-Host "Top: $($rect.Top)"
    Write-Host "Right: $($rect.Right)"
    Write-Host "Bottom: $($rect.Bottom)"
    Write-Host "Height: $($rect.Bottom - $rect.Top)"
}

Write-Host ""
Write-Host "=== All windows at bottom ==="
Add-Type @"
using System;
using System.Runtime.InteropServices;
using System.Text;

public class AllWindowsFinder {
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

[AllWindowsFinder]::EnumWindows({
    param($hwnd, $lParam)

    if ([AllWindowsFinder]::IsWindowVisible($hwnd)) {
        $rect = New-Object AllWindowsFinder+RECT
        [AllWindowsFinder]::GetWindowRect($hwnd, [ref]$rect) | Out-Null

        # 화면 하단 근처에 있는 창만 표시 (Top > 1200)
        if ($rect.Top -gt 1200 -and $rect.Top -lt 1500 -and $rect.Left -ge 0) {
            $sb = New-Object System.Text.StringBuilder 256
            [AllWindowsFinder]::GetWindowText($hwnd, $sb, 256) | Out-Null
            $title = $sb.ToString()

            $classSb = New-Object System.Text.StringBuilder 256
            [AllWindowsFinder]::GetClassName($hwnd, $classSb, 256) | Out-Null
            $className = $classSb.ToString()

            Write-Host "Class: $className | Top: $($rect.Top) | Bottom: $($rect.Bottom) | Height: $($rect.Bottom - $rect.Top) | Title: $title"
        }
    }
    return $true
}, [IntPtr]::Zero) | Out-Null
