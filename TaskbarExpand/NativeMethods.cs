using System;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskbarExpand
{
    /// <summary>
    /// Win32 API 래퍼 클래스
    /// </summary>
    internal static class NativeMethods
    {
        #region Delegates
        public delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);
        #endregion

        #region Window Enumeration
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);
        #endregion

        #region Window Properties
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsIconic(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hwnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hwnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hwnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        public static extern long GetWindowLong(IntPtr hwnd, int nIndex);

        [DllImport("user32.dll")]
        public static extern int SetWindowLong(IntPtr hwnd, int nIndex, long dwNewLong);

        [DllImport("user32.dll")]
        public static extern IntPtr GetWindow(IntPtr hwnd, uint uCmd);
        #endregion

        #region Window Activation
        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ShowWindow(IntPtr hwnd, int nCmdShow);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
        #endregion

        #region Process
        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint lpdwProcessId);
        #endregion

        #region Constants
        public const int GWL_STYLE = -16;
        public const int GWL_EXSTYLE = -20;

        // Window Styles
        public const long WS_CAPTION = 0x00C00000L;
        public const long WS_SYSMENU = 0x00080000L;
        public const long WS_EX_TOOLWINDOW = 0x00000080L;
        public const long WS_EX_APPWINDOW = 0x00040000L;
        public const long WS_EX_NOACTIVATE = 0x08000000L;

        // ShowWindow
        public const int SW_MINIMIZE = 6;
        public const int SW_RESTORE = 9;
        public const int SW_SHOW = 5;

        // GetWindow
        public const uint GW_OWNER = 4;

        // Messages
        public const uint WM_CLOSE = 0x0010;
        #endregion
    }
}
