using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows.Media.Imaging;

namespace TaskbarExpand
{
    public class WindowInfo : INotifyPropertyChanged
    {
        // 아이콘 캐시 (프로세스 경로별)
        private static readonly ConcurrentDictionary<string, BitmapSource?> _iconCache = new();

        // 제외할 클래스 이름 (static readonly - 한 번만 할당)
        private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
        {
            "Shell_TrayWnd", "DV2ControlHost", "MsgrNotifyClass", "SysShadow",
            "Button", "Windows.UI.Core.CoreWindow", "ApplicationFrameWindow",
            "WorkerW", "Progman", "EdgeUiInputTopWndClass", "NativeHWNDHost",
            "Chrome_WidgetWin_0"
        };

        // 재사용 StringBuilder
        private static readonly StringBuilder _classNameBuffer = new(256);

        private string? _title;
        private BitmapSource? _icon;

        public IntPtr Handle { get; set; }

        public string? Title
        {
            get => _title;
            set { if (_title != value) { _title = value; OnPropertyChanged(); } }
        }

        public BitmapSource? Icon
        {
            get => _icon;
            set { if (_icon != value) { _icon = value; OnPropertyChanged(); } }
        }

        public uint ProcessId { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public static WindowInfo FromHandle(IntPtr hwnd)
        {
            var info = new WindowInfo { Handle = hwnd };

            // 창 제목
            int len = NativeMethods.GetWindowTextLength(hwnd);
            if (len > 0)
            {
                var sb = new StringBuilder(len + 1);
                NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
                info.Title = sb.ToString();
            }
            else
            {
                info.Title = "(제목 없음)";
            }

            // 프로세스 ID
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            info.ProcessId = pid;

            // 아이콘 (캐시 사용)
            info.Icon = GetCachedIcon(pid);

            return info;
        }

        private static BitmapSource? GetCachedIcon(uint processId)
        {
            try
            {
                var proc = Process.GetProcessById((int)processId);
                var path = proc.MainModule?.FileName;
                if (string.IsNullOrEmpty(path)) return null;

                return _iconCache.GetOrAdd(path, p =>
                {
                    try
                    {
                        using var icon = System.Drawing.Icon.ExtractAssociatedIcon(p);
                        if (icon == null) return null;

                        var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                            icon.Handle,
                            System.Windows.Int32Rect.Empty,
                            BitmapSizeOptions.FromEmptyOptions());
                        bmp.Freeze(); // 스레드 안전 + 성능
                        return bmp;
                    }
                    catch { return null; }
                });
            }
            catch { return null; }
        }

        public static bool IsValidTaskbarWindow(IntPtr hwnd)
        {
            if (!NativeMethods.IsWindowVisible(hwnd)) return false;
            if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;

            // 클래스 이름 체크 (버퍼 재사용)
            lock (_classNameBuffer)
            {
                _classNameBuffer.Clear();
                NativeMethods.GetClassName(hwnd, _classNameBuffer, _classNameBuffer.Capacity);
                if (ExcludedClasses.Contains(_classNameBuffer.ToString()))
                    return false;
            }

            long style = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
            long exStyle = NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);

            // ToolWindow 제외 (AppWindow 플래그 없으면)
            if ((exStyle & NativeMethods.WS_EX_TOOLWINDOW) != 0 &&
                (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return false;

            // NoActivate 제외
            if ((exStyle & NativeMethods.WS_EX_NOACTIVATE) != 0)
                return false;

            // Owner 있는 창 제외
            var owner = NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER);
            if (owner != IntPtr.Zero && NativeMethods.IsWindowVisible(owner))
                return false;

            // Caption/SysMenu 없으면 AppWindow 플래그 필요
            if ((style & NativeMethods.WS_CAPTION) == 0 &&
                (style & NativeMethods.WS_SYSMENU) == 0 &&
                (exStyle & NativeMethods.WS_EX_APPWINDOW) == 0)
                return false;

            return true;
        }
    }
}
