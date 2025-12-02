using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace TaskbarExpand
{
    public partial class MainWindow : Window
    {
        private readonly ObservableCollection<WindowInfo> _windows = new();
        private readonly HashSet<IntPtr> _currentHandles = new();
        private readonly HashSet<IntPtr> _newHandles = new();
        private readonly StringBuilder _titleBuffer = new(256);

        private IntPtr _hwnd;
        private bool _isHorizontalMode;
        private bool _isActivating;
        private bool _isDragging;
        private Point _dragStartPoint;
        private IntPtr _lastActivatedWindow;
        private DispatcherTimer? _refreshTimer;
        private DispatcherTimer? _autoHideTimer;
        private DispatcherTimer? _hideDelayTimer;
        private bool _isAppBarRegistered;
        private System.Windows.Forms.Screen? _currentScreen;
        private bool _isAutoHideEnabled;
        private bool _isHidden;
        private int _lastHorizontalHeight;

        private const double HORIZONTAL_ITEM_WIDTH = 100;
        private const int APPBAR_WIDTH = 280;
        private const int AUTO_HIDE_DELAY = 300; // 숨김 지연 시간 (ms)
        private const int EDGE_DETECTION_SIZE = 8; // 가장자리 감지 영역 (px)

        public MainWindow()
        {
            InitializeComponent();
            WindowListBox.ItemsSource = _windows;
            HorizontalWindowListBox.ItemsSource = _windows;
        }

        #region Window Events
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _hwnd = new WindowInteropHelper(this).Handle;

                var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
                NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_NOACTIVATE);

                // 마우스 커서 위치로 현재 모니터 감지
                var cursorPos = System.Windows.Forms.Cursor.Position;
                _currentScreen = System.Windows.Forms.Screen.FromPoint(cursorPos) ?? System.Windows.Forms.Screen.PrimaryScreen;

                // AppBar 등록 (다른 창들이 리사이즈되도록)
                RegisterAppBar();

                // 타이머 설정 (시작은 지연)
                _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                _refreshTimer.Tick += (_, _) => SafeRefreshWindowList();

                _autoHideTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
                _autoHideTimer.Tick += AutoHideTimer_Tick;

                _hideDelayTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AUTO_HIDE_DELAY) };
                _hideDelayTimer.Tick += HideDelayTimer_Tick;

                // UI 렌더링 완료 후 창 목록 로드 (버벅임 방지)
                Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
                {
                    SafeRefreshWindowList();
                    _refreshTimer?.Start();
                });
            }
            catch { }
        }

        private void SafeRefreshWindowList()
        {
            try { RefreshWindowList(); }
            catch { }
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            try
            {
                _refreshTimer?.Stop();
                _autoHideTimer?.Stop();
                _hideDelayTimer?.Stop();
                UnregisterAppBar();
            }
            catch { }
        }
        #endregion

        #region AppBar
        private void RegisterAppBar()
        {
            try
            {
                if (_isAppBarRegistered) return;

                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                    hWnd = _hwnd
                };

                // AppBar 등록
                if (NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd) != 0)
                {
                    _isAppBarRegistered = true;
                    SetAppBarPos();
                }
            }
            catch { }
        }

        private void UnregisterAppBar()
        {
            try
            {
                if (!_isAppBarRegistered) return;

                var abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                    hWnd = _hwnd
                };

                NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
                _isAppBarRegistered = false;
            }
            catch { }
        }

        private void SetAppBarPos()
        {
            try
            {
                if (!_isAppBarRegistered) return;

                var screen = _currentScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                var bounds = screen.Bounds;
                var workArea = screen.WorkingArea;

                NativeMethods.APPBARDATA abd;

                if (_isHorizontalMode)
                {
                    // 가로 모드: 화면 하단에 배치 (Windows 작업표시줄 바로 위)
                    int horizontalHeight = CalculateHorizontalHeight(bounds.Width);
                    _lastHorizontalHeight = horizontalHeight;

                    // bounds.Bottom 기준으로 요청 (시스템이 Windows 작업표시줄 위로 조정해줌)
                    abd = new NativeMethods.APPBARDATA
                    {
                        cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                        hWnd = _hwnd,
                        uEdge = NativeMethods.ABE_BOTTOM,
                        rc = new NativeMethods.RECT
                        {
                            left = bounds.Left,
                            top = bounds.Bottom - horizontalHeight,
                            right = bounds.Right,
                            bottom = bounds.Bottom
                        }
                    };

                    // 위치 쿼리 - 시스템이 사용 가능한 영역으로 조정
                    NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

                    // 높이 재조정 (시스템이 조정한 bottom 기준)
                    abd.rc.top = abd.rc.bottom - horizontalHeight;

                    // 위치 설정 - 시스템에 이 영역을 예약
                    NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

                    // 창 위치/크기 적용 - 시스템이 설정한 영역 사용
                    Width = abd.rc.right - abd.rc.left;
                    Height = abd.rc.bottom - abd.rc.top;
                    Left = abd.rc.left;
                    Top = abd.rc.top;
                }
                else
                {
                    // 세로 모드: 오른쪽에 배치 (bounds 기준 - 듀얼 모니터 대응)
                    abd = new NativeMethods.APPBARDATA
                    {
                        cbSize = Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                        hWnd = _hwnd,
                        uEdge = NativeMethods.ABE_RIGHT,
                        rc = new NativeMethods.RECT
                        {
                            left = bounds.Right - APPBAR_WIDTH,
                            top = bounds.Top,
                            right = bounds.Right,
                            bottom = bounds.Bottom
                        }
                    };

                    // 위치 쿼리 - 시스템이 사용 가능한 영역으로 조정
                    NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

                    // 너비 재조정 (시스템이 조정한 right 기준)
                    abd.rc.left = abd.rc.right - APPBAR_WIDTH;

                    // 위치 설정
                    NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

                    // 창 위치/크기 적용
                    Width = abd.rc.right - abd.rc.left;
                    Height = abd.rc.bottom - abd.rc.top;
                    Left = abd.rc.left;
                    Top = abd.rc.top;
                }
            }
            catch { }
        }

        private int CalculateHorizontalHeight(int screenWidth = 0)
        {
            if (screenWidth == 0)
            {
                var screen = _currentScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
                screenWidth = screen?.Bounds.Width ?? (int)SystemParameters.PrimaryScreenWidth;
            }
            double usableWidth = screenWidth - 100;
            int itemsPerRow = Math.Max(1, (int)(usableWidth / HORIZONTAL_ITEM_WIDTH));
            int rows = Math.Max(1, (int)Math.Ceiling((double)_windows.Count / itemsPerRow));
            rows = Math.Min(rows, 2);
            return rows == 1 ? 48 : 88;
        }

        /// <summary>
        /// 해당 모니터의 Windows 작업표시줄 Top 위치를 찾습니다.
        /// 주 모니터: Shell_TrayWnd, 확장 모니터: Shell_SecondaryTrayWnd
        /// </summary>
        private int GetWindowsTaskbarTop(System.Drawing.Rectangle bounds)
        {
            // 주 모니터의 작업표시줄 (Shell_TrayWnd)
            IntPtr taskbarHwnd = NativeMethods.FindWindow("Shell_TrayWnd", null);
            if (taskbarHwnd != IntPtr.Zero)
            {
                if (NativeMethods.GetWindowRect(taskbarHwnd, out NativeMethods.RECT rect))
                {
                    // 작업표시줄이 현재 모니터 범위 내에 있는지 확인
                    if (rect.left >= bounds.Left && rect.left < bounds.Right &&
                        rect.top > bounds.Height / 2)
                    {
                        return rect.top;
                    }
                }
            }

            // 확장 모니터의 작업표시줄 (Shell_SecondaryTrayWnd) 찾기
            IntPtr secondaryTaskbar = IntPtr.Zero;
            NativeMethods.EnumWindows((hwnd, _) =>
            {
                var className = new StringBuilder(256);
                NativeMethods.GetClassName(hwnd, className, className.Capacity);
                if (className.ToString() == "Shell_SecondaryTrayWnd")
                {
                    if (NativeMethods.GetWindowRect(hwnd, out NativeMethods.RECT rect))
                    {
                        // 현재 모니터 범위 내에 있는 작업표시줄 찾기
                        if (rect.left >= bounds.Left && rect.left < bounds.Right)
                        {
                            secondaryTaskbar = hwnd;
                            return false; // 찾았으면 중단
                        }
                    }
                }
                return true;
            }, IntPtr.Zero);

            if (secondaryTaskbar != IntPtr.Zero)
            {
                if (NativeMethods.GetWindowRect(secondaryTaskbar, out NativeMethods.RECT rect))
                {
                    if (rect.top > bounds.Height / 2)
                    {
                        return rect.top;
                    }
                }
            }

            // 찾지 못하면 Bounds.Bottom - 48 (기본 작업표시줄 높이) 반환
            return bounds.Bottom - 48;
        }
        #endregion

        #region Window List
        private void RefreshWindowList()
        {
            _currentHandles.Clear();
            foreach (var w in _windows) _currentHandles.Add(w.Handle);

            _newHandles.Clear();

            NativeMethods.EnumWindows((hwnd, _) =>
            {
                if (hwnd != _hwnd && WindowInfo.IsValidTaskbarWindow(hwnd))
                {
                    _newHandles.Add(hwnd);
                    if (!_currentHandles.Contains(hwnd))
                    {
                        try { _windows.Add(WindowInfo.FromHandle(hwnd)); }
                        catch { }
                    }
                }
                return true;
            }, IntPtr.Zero);

            // 사라진 창 제거
            for (int i = _windows.Count - 1; i >= 0; i--)
            {
                if (!_newHandles.Contains(_windows[i].Handle))
                    _windows.RemoveAt(i);
            }

            // 제목 업데이트 (변경된 것만)
            foreach (var w in _windows)
            {
                NativeMethods.GetWindowText(w.Handle, _titleBuffer, _titleBuffer.Capacity);
                var title = _titleBuffer.ToString();
                if (title.Length > 0 && w.Title != title)
                    w.Title = title;
                _titleBuffer.Clear();
            }

            UpdateStatusText();
        }

        private void UpdateStatusText()
        {
            StatusTextBlock.Text = _windows.Count == 0
                ? "실행 중인 창이 없습니다"
                : $"총 {_windows.Count}개의 창이 실행 중입니다";

            if (_isHorizontalMode) UpdateHorizontalHeight();
        }

        private void UpdateHorizontalHeight()
        {
            if (!_isHorizontalMode) return;

            int newHeight = CalculateHorizontalHeight();

            // 높이가 변경되면 재설정
            if (_lastHorizontalHeight != newHeight)
            {
                _lastHorizontalHeight = newHeight;
                if (_isAppBarRegistered)
                {
                    SetAppBarPos();
                }
                else if (_isAutoHideEnabled && !_isHidden)
                {
                    SetAutoHidePosition(true);
                }
            }
        }
        #endregion

        #region UI Events
        private void WindowListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_isActivating || WindowListBox.SelectedItem is not WindowInfo w) return;
                _isActivating = true;
                ToggleWindow(w.Handle);
                Dispatcher.BeginInvoke(() => { WindowListBox.SelectedItem = null; _isActivating = false; }, DispatcherPriority.Background);
            }
            catch { _isActivating = false; }
        }

        private void HorizontalItem_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            try
            {
                if (_isActivating) return;
                if (sender is FrameworkElement { DataContext: WindowInfo w })
                {
                    _isActivating = true;
                    ToggleWindow(w.Handle);
                    Dispatcher.BeginInvoke(() => _isActivating = false, DispatcherPriority.Background);
                }
            }
            catch { _isActivating = false; }
        }

        private void ToggleWindow(IntPtr hwnd)
        {
            try
            {
                if (hwnd == IntPtr.Zero) return;

                if (NativeMethods.IsIconic(hwnd))
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
                    NativeMethods.SetForegroundWindow(hwnd);
                    _lastActivatedWindow = hwnd;
                }
                else if (_lastActivatedWindow == hwnd)
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                    _lastActivatedWindow = IntPtr.Zero;
                }
                else
                {
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_SHOW);
                    NativeMethods.SetForegroundWindow(hwnd);
                    _lastActivatedWindow = hwnd;
                }
            }
            catch { }
        }

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is Button { Tag: IntPtr hwnd } && hwnd != IntPtr.Zero)
                    NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            }
            catch { }
            e.Handled = true;
        }

        private void ContextMenu_CloseWindow_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (sender is MenuItem menuItem && menuItem.Tag is IntPtr hwnd && hwnd != IntPtr.Zero)
                {
                    NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                }
            }
            catch { }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // AppBar 모드에서는 드래그 이동 비활성화
                if (!_isAppBarRegistered && e.ClickCount == 1)
                    DragMove();
            }
            catch { }
        }
        #endregion

        #region Drag & Drop
        private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            try
            {
                if (e.LeftButton != MouseButtonState.Pressed || _isDragging) return;
                if (sender is not ListBox lb) return;

                var pos = e.GetPosition(lb);
                if (Math.Abs(pos.X - _dragStartPoint.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(pos.Y - _dragStartPoint.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    var item = GetListBoxItemAt(lb, pos);
                    if (item?.DataContext is WindowInfo w)
                    {
                        _isDragging = true;
                        DragDrop.DoDragDrop(lb, w, DragDropEffects.Move);
                        _isDragging = false;
                    }
                }
            }
            catch { _isDragging = false; }
        }

        private void ListBox_DragOver(object sender, DragEventArgs e)
        {
            try
            {
                e.Effects = e.Data.GetDataPresent(typeof(WindowInfo)) ? DragDropEffects.Move : DragDropEffects.None;
            }
            catch { e.Effects = DragDropEffects.None; }
            e.Handled = true;
        }

        private void ListBox_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (sender is not ListBox lb || e.Data.GetData(typeof(WindowInfo)) is not WindowInfo dropped) return;

                var target = GetListBoxItemAt(lb, e.GetPosition(lb))?.DataContext as WindowInfo;
                int oldIdx = _windows.IndexOf(dropped);
                int newIdx = target != null ? _windows.IndexOf(target) : _windows.Count - 1;

                if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
                    _windows.Move(oldIdx, newIdx);
            }
            catch { }
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            try
            {
                base.OnPreviewMouseLeftButtonDown(e);
                _dragStartPoint = e.GetPosition(this);
            }
            catch { }
        }

        private static ListBoxItem? GetListBoxItemAt(ListBox lb, Point pt)
        {
            try
            {
                var el = lb.InputHitTest(pt) as DependencyObject;
                while (el != null)
                {
                    if (el is ListBoxItem item) return item;
                    el = System.Windows.Media.VisualTreeHelper.GetParent(el);
                }
            }
            catch { }
            return null;
        }
        #endregion

        #region Resize
        private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                // AppBar 모드에서는 리사이즈 비활성화
                if (_isAppBarRegistered) return;

                if (sender is Rectangle { Name: var name })
                {
                    int dir = name switch
                    {
                        "ResizeLeft" => 1, "ResizeRight" => 2, "ResizeTop" => 3, "ResizeBottom" => 6,
                        "ResizeTopLeft" => 4, "ResizeTopRight" => 5, "ResizeBottomLeft" => 7, "ResizeBottomRight" => 8,
                        _ => 0
                    };
                    if (dir != 0) NativeMethods.SendMessage(_hwnd, 0x112, (IntPtr)(0xF000 + dir), IntPtr.Zero);
                }
            }
            catch { }
        }
        #endregion

        #region Mode Toggle
        private void ToggleModeButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isHorizontalMode = !_isHorizontalMode;
                ApplyMode();
            }
            catch { }
        }

        private void ApplyMode()
        {
            try
            {
                // 먼저 AppBar 해제 (edge 변경을 위해)
                UnregisterAppBar();
                _hideDelayTimer?.Stop();

                // 숨김 상태 초기화
                _isHidden = false;
                _lastHorizontalHeight = 0;

                if (_isHorizontalMode)
                {
                    VerticalModeContainer.Visibility = Visibility.Collapsed;
                    HorizontalModeContainer.Visibility = Visibility.Visible;
                }
                else
                {
                    VerticalModeContainer.Visibility = Visibility.Visible;
                    HorizontalModeContainer.Visibility = Visibility.Collapsed;
                    ToggleModeButton.Content = "⇄";
                }

                // AppBar 재등록 (새 edge로)
                if (!_isAutoHideEnabled)
                {
                    RegisterAppBar();
                }
                else
                {
                    // 자동 숨김 모드에서는 보이는 상태로 시작
                    SetAutoHidePosition(true);
                }
            }
            catch { }
        }

        private DispatcherTimer? _positionCorrectionTimer;

        private void ToggleAutoHideButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _isAutoHideEnabled = !_isAutoHideEnabled;
                UpdateAutoHideButtonIcon();

                if (_isAutoHideEnabled)
                {
                    // AppBar 해제하고 자동 숨김 모드로
                    UnregisterAppBar();
                    _isHidden = false;
                    SetAutoHidePosition(true); // 먼저 보이는 상태로 시작
                    _autoHideTimer?.Start();
                }
                else
                {
                    // 자동 숨김 타이머 정지
                    _autoHideTimer?.Stop();
                    _hideDelayTimer?.Stop();
                    _isHidden = false;

                    // AppBar 등록
                    RegisterAppBar();

                    // 시스템이 workArea를 업데이트하는 데 시간이 걸리므로,
                    // 약간의 지연 후 위치를 다시 설정하여 정확한 위치 확보
                    if (_isHorizontalMode)
                    {
                        // 기존 타이머 정리
                        _positionCorrectionTimer?.Stop();
                        _positionCorrectionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
                        _positionCorrectionTimer.Tick += PositionCorrectionTimer_Tick;
                        _positionCorrectionTimer.Start();
                    }
                }
            }
            catch { }
        }

        private void PositionCorrectionTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _positionCorrectionTimer?.Stop();
                if (_isAppBarRegistered && _isHorizontalMode && !_isAutoHideEnabled)
                {
                    SetAppBarPos();
                }
            }
            catch { }
        }

        private void UpdateAutoHideButtonIcon()
        {
            try
            {
                string icon = _isAutoHideEnabled ? "📍" : "📌";
                ToggleAutoHideButton.Content = icon;
                HorizontalAutoHideButton.Content = icon;
            }
            catch { }
        }

        private void GroupByAppButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_windows.Count == 0) return;

                // 같은 프로그램끼리 그룹화 (프로세스 경로 기준)
                var grouped = _windows
                    .Select((w, i) => new { Window = w, Index = i })
                    .GroupBy(x => x.Window.ProcessPath ?? x.Window.ProcessId.ToString())
                    .SelectMany(g => g.Select(x => x.Window))
                    .ToList();

                // 기존 순서와 다르면 재정렬
                bool needsReorder = false;
                for (int i = 0; i < grouped.Count; i++)
                {
                    if (i >= _windows.Count || _windows[i] != grouped[i])
                    {
                        needsReorder = true;
                        break;
                    }
                }

                if (needsReorder)
                {
                    // 컬렉션 재정렬
                    for (int i = 0; i < grouped.Count; i++)
                    {
                        int currentIndex = _windows.IndexOf(grouped[i]);
                        if (currentIndex >= 0 && currentIndex != i)
                        {
                            _windows.Move(currentIndex, i);
                        }
                    }
                }
            }
            catch { }
        }
        #endregion

        #region Auto Hide
        private void AutoHideTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                if (!_isAutoHideEnabled) return;

                var cursorPos = System.Windows.Forms.Cursor.Position;
                var screen = _currentScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                bool isOverWindow = IsMouseOverWindow();
                bool isAtEdge = IsMouseAtEdge(cursorPos, screen);

                if (_isHidden)
                {
                    // 숨김 상태: 가장자리에 마우스가 있으면 즉시 표시
                    if (isAtEdge)
                    {
                        _hideDelayTimer?.Stop();
                        ShowBar();
                    }
                }
                else
                {
                    // 표시 상태: 마우스가 창 위나 가장자리에 있으면 유지
                    if (isOverWindow || isAtEdge)
                    {
                        _hideDelayTimer?.Stop();
                    }
                    else
                    {
                        // 마우스가 벗어났으면 지연 후 숨김
                        if (_hideDelayTimer != null && !_hideDelayTimer.IsEnabled)
                        {
                            _hideDelayTimer.Start();
                        }
                    }
                }
            }
            catch { }
        }

        private void HideDelayTimer_Tick(object? sender, EventArgs e)
        {
            try
            {
                _hideDelayTimer?.Stop();
                if (_isAutoHideEnabled && !_isHidden && !IsMouseOverWindow())
                {
                    HideBar();
                }
            }
            catch { }
        }

        private bool IsMouseAtEdge(System.Drawing.Point cursorPos, System.Windows.Forms.Screen screen)
        {
            try
            {
                var workArea = screen.WorkingArea;

                if (_isHorizontalMode)
                {
                    // 가로 모드: 하단 가장자리 감지 (WorkingArea 기준)
                    return cursorPos.Y >= workArea.Bottom - EDGE_DETECTION_SIZE &&
                           cursorPos.Y <= workArea.Bottom &&
                           cursorPos.X >= workArea.Left &&
                           cursorPos.X <= workArea.Right;
                }
                else
                {
                    // 세로 모드: 오른쪽 가장자리 감지 (WorkingArea 기준)
                    return cursorPos.X >= workArea.Right - EDGE_DETECTION_SIZE &&
                           cursorPos.X <= workArea.Right &&
                           cursorPos.Y >= workArea.Top &&
                           cursorPos.Y <= workArea.Bottom;
                }
            }
            catch { return false; }
        }

        private bool IsMouseOverWindow()
        {
            try
            {
                var cursorPos = System.Windows.Forms.Cursor.Position;
                return cursorPos.X >= Left && cursorPos.X <= Left + Width &&
                       cursorPos.Y >= Top && cursorPos.Y <= Top + Height;
            }
            catch { return false; }
        }

        private void ShowBar()
        {
            try
            {
                _isHidden = false;
                SetAutoHidePosition(true);
            }
            catch { }
        }

        private void HideBar()
        {
            try
            {
                _isHidden = true;
                SetAutoHidePosition(false);
            }
            catch { }
        }

        private void SetAutoHidePosition(bool visible)
        {
            try
            {
                var screen = _currentScreen ?? System.Windows.Forms.Screen.PrimaryScreen;
                if (screen == null) return;

                // WorkingArea 사용 (작업 표시줄 제외한 영역)
                var workArea = screen.WorkingArea;

                if (_isHorizontalMode)
                {
                    int horizontalHeight = CalculateHorizontalHeight(workArea.Width);
                    Width = workArea.Width;
                    Height = horizontalHeight;
                    Left = workArea.Left;

                    if (visible)
                    {
                        // 작업 영역 하단에 배치
                        Top = workArea.Bottom - horizontalHeight;
                    }
                    else
                    {
                        // 숨김 상태: 창을 화면 아래로 숨기고 3px만 보이게
                        Top = workArea.Bottom - 3;
                        Height = 3;
                    }
                }
                else
                {
                    if (visible)
                    {
                        Width = APPBAR_WIDTH;
                        Height = workArea.Height;
                        Top = workArea.Top;
                        Left = workArea.Right - APPBAR_WIDTH;
                    }
                    else
                    {
                        // 숨김 상태: 창을 화면 오른쪽으로 숨기고 3px만 보이게
                        Width = 3;
                        Height = workArea.Height;
                        Top = workArea.Top;
                        Left = workArea.Right - 3;
                    }
                }
            }
            catch (Exception)
            {
                // 모드 전환 중 발생할 수 있는 예외 무시
            }
        }
        #endregion
    }
}
