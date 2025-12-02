using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace TaskbarExpand
{
    public class WindowInfo : INotifyPropertyChanged
    {
        // 아이콘 캐시 (프로세스 경로별)
        private static readonly ConcurrentDictionary<string, BitmapSource?> _iconCache = new();

        // 아이콘 로딩 중인 경로 추적 (중복 로딩 방지)
        private static readonly ConcurrentDictionary<string, bool> _loadingPaths = new();

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
        public string? ProcessPath { get; private set; }

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

            try
            {
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

                // 프로세스 경로 가져오기 (빠름)
                info.ProcessPath = GetProcessPath(pid);

                // 캐시된 아이콘이 있으면 즉시 사용
                if (info.ProcessPath != null && _iconCache.TryGetValue(info.ProcessPath, out var cachedIcon))
                {
                    info.Icon = cachedIcon;
                }
                else
                {
                    // 비동기로 아이콘 로딩
                    info.LoadIconAsync();
                }
            }
            catch
            {
                info.Title ??= "(제목 없음)";
            }

            return info;
        }

        private static string? GetProcessPath(uint processId)
        {
            try
            {
                var proc = Process.GetProcessById((int)processId);
                return proc.MainModule?.FileName;
            }
            catch { return null; }
        }

        private async void LoadIconAsync()
        {
            var path = ProcessPath;
            if (string.IsNullOrEmpty(path)) return;

            // 이미 로딩 중이면 대기
            if (!_loadingPaths.TryAdd(path, true))
            {
                // 다른 곳에서 로딩 중 - 잠시 후 캐시 확인
                try
                {
                    await Task.Delay(150);
                    if (_iconCache.TryGetValue(path, out var cachedIcon))
                    {
                        var app = Application.Current;
                        if (app != null && !app.Dispatcher.HasShutdownStarted)
                        {
                            app.Dispatcher.Invoke(() => Icon = cachedIcon);
                        }
                    }
                }
                catch { }
                return;
            }

            try
            {
                var icon = await Task.Run(() => ExtractIcon(path));

                if (icon != null)
                {
                    _iconCache.TryAdd(path, icon);
                    // UI 스레드에서 아이콘 설정 (앱 종료 중인지 확인)
                    var app = Application.Current;
                    if (app != null && !app.Dispatcher.HasShutdownStarted)
                    {
                        app.Dispatcher.Invoke(() => Icon = icon);
                    }
                }
            }
            catch { }
            finally
            {
                _loadingPaths.TryRemove(path, out _);
            }
        }

        private static BitmapSource? ExtractIcon(string path)
        {
            try
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon == null) return null;

                var bmp = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icon.Handle,
                    Int32Rect.Empty,
                    BitmapSizeOptions.FromEmptyOptions());
                bmp.Freeze(); // 스레드 안전 + 성능
                return bmp;
            }
            catch { return null; }
        }

        public static bool IsValidTaskbarWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return false;
                if (!NativeMethods.IsWindowVisible(hwnd)) return false;
                if (NativeMethods.GetWindowTextLength(hwnd) == 0) return false;

                // 클래스 이름 체크 (버퍼 재사용)
                string className;
                lock (_classNameBuffer)
                {
                    _classNameBuffer.Clear();
                    NativeMethods.GetClassName(hwnd, _classNameBuffer, _classNameBuffer.Capacity);
                    className = _classNameBuffer.ToString();
                }

                if (ExcludedClasses.Contains(className))
                    return false;

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
            catch { return false; }
        }
    }
}
