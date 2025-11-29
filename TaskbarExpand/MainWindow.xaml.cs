using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        private bool _isAppBarRegistered;

        private const double HORIZONTAL_ITEM_WIDTH = 150;
        private const int APP_BAR_WIDTH = 280;

        public MainWindow()
        {
            InitializeComponent();
            WindowListBox.ItemsSource = _windows;
            HorizontalWindowListBox.ItemsSource = _windows;
        }

        #region Window Events
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _hwnd = new WindowInteropHelper(this).Handle;

            var exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle | NativeMethods.WS_EX_NOACTIVATE);

            // AppBar로 등록하여 작업 영역 예약
            RegisterAppBar();

            _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _refreshTimer.Tick += (_, _) => RefreshWindowList();
            _refreshTimer.Start();

            RefreshWindowList();
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            _refreshTimer?.Stop();
            UnregisterAppBar();
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
            // 버튼 영역 (↕, X) 약 80px 제외
            double usableWidth = SystemParameters.PrimaryScreenWidth - 100;
            int itemsPerRow = Math.Max(1, (int)(usableWidth / HORIZONTAL_ITEM_WIDTH));

            // 1줄에 다 들어가면 1줄, 넘치면 2줄
            int rows = _windows.Count <= itemsPerRow ? 1 : 2;
            int height = rows == 1 ? 40 : 76;

            // AppBar 높이 업데이트
            SetAppBarPos(NativeMethods.ABE_BOTTOM, height);
        }
        #endregion

        #region UI Events
        private void WindowListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isActivating || WindowListBox.SelectedItem is not WindowInfo w) return;
            _isActivating = true;
            ToggleWindow(w.Handle);
            Dispatcher.BeginInvoke(() => { WindowListBox.SelectedItem = null; _isActivating = false; }, DispatcherPriority.Background);
        }

        private void HorizontalWindowListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isActivating || HorizontalWindowListBox.SelectedItem is not WindowInfo w) return;
            _isActivating = true;
            ToggleWindow(w.Handle);
            Dispatcher.BeginInvoke(() => { HorizontalWindowListBox.SelectedItem = null; _isActivating = false; }, DispatcherPriority.Background);
        }

        private void ToggleWindow(IntPtr hwnd)
        {
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

        private void CloseWindowButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button { Tag: IntPtr hwnd })
                NativeMethods.PostMessage(hwnd, NativeMethods.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            e.Handled = true;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 1) DragMove();
        }
        #endregion

        #region Drag & Drop
        private void ListBox_PreviewMouseMove(object sender, MouseEventArgs e)
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

        private void ListBox_DragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(typeof(WindowInfo)) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }

        private void ListBox_Drop(object sender, DragEventArgs e)
        {
            if (sender is not ListBox lb || e.Data.GetData(typeof(WindowInfo)) is not WindowInfo dropped) return;

            var target = GetListBoxItemAt(lb, e.GetPosition(lb))?.DataContext as WindowInfo;
            int oldIdx = _windows.IndexOf(dropped);
            int newIdx = target != null ? _windows.IndexOf(target) : _windows.Count - 1;

            if (oldIdx >= 0 && newIdx >= 0 && oldIdx != newIdx)
                _windows.Move(oldIdx, newIdx);
            e.Handled = true;
        }

        protected override void OnPreviewMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseLeftButtonDown(e);
            _dragStartPoint = e.GetPosition(this);
        }

        private static ListBoxItem? GetListBoxItemAt(ListBox lb, Point pt)
        {
            var el = lb.InputHitTest(pt) as DependencyObject;
            while (el != null)
            {
                if (el is ListBoxItem item) return item;
                el = System.Windows.Media.VisualTreeHelper.GetParent(el);
            }
            return null;
        }
        #endregion

        #region Resize
        private void Resize_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
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
        #endregion

        #region AppBar
        private void RegisterAppBar()
        {
            if (_isAppBarRegistered) return;

            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd
            };

            // AppBar 등록
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
            _isAppBarRegistered = true;

            // 오른쪽 가장자리에 위치 설정
            SetAppBarPos(NativeMethods.ABE_RIGHT, APP_BAR_WIDTH);
        }

        private void UnregisterAppBar()
        {
            if (!_isAppBarRegistered) return;

            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd
            };

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
            _isAppBarRegistered = false;
        }

        private void SetAppBarPos(uint edge, int size)
        {
            var abd = new NativeMethods.APPBARDATA
            {
                cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(NativeMethods.APPBARDATA)),
                hWnd = _hwnd,
                uEdge = edge
            };

            // 화면 전체 크기 가져오기
            int screenWidth = (int)SystemParameters.PrimaryScreenWidth;
            int screenHeight = (int)SystemParameters.PrimaryScreenHeight;

            // 작업 표시줄 영역 계산
            if (edge == NativeMethods.ABE_RIGHT)
            {
                abd.rc.left = screenWidth - size;
                abd.rc.top = 0;
                abd.rc.right = screenWidth;
                abd.rc.bottom = screenHeight;
            }
            else if (edge == NativeMethods.ABE_BOTTOM)
            {
                abd.rc.left = 0;
                abd.rc.top = screenHeight - size;
                abd.rc.right = screenWidth;
                abd.rc.bottom = screenHeight;
            }

            // 위치 쿼리 (다른 AppBar와 충돌 조정)
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);

            // 위치 설정
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            // 창 위치 적용
            Left = abd.rc.left;
            Top = abd.rc.top;
            Width = abd.rc.right - abd.rc.left;
            Height = abd.rc.bottom - abd.rc.top;
        }
        #endregion

        #region Mode Toggle
        private void ToggleModeButton_Click(object sender, RoutedEventArgs e)
        {
            _isHorizontalMode = !_isHorizontalMode;
            ApplyMode();
        }

        private void ApplyMode()
        {
            if (_isHorizontalMode)
            {
                VerticalModeContainer.Visibility = Visibility.Collapsed;
                HorizontalModeContainer.Visibility = Visibility.Visible;
                // 가로 모드: 하단에 AppBar 설정 (초기 1줄 높이)
                UpdateHorizontalHeight();
            }
            else
            {
                VerticalModeContainer.Visibility = Visibility.Visible;
                HorizontalModeContainer.Visibility = Visibility.Collapsed;
                // 세로 모드: 오른쪽에 AppBar 설정
                SetAppBarPos(NativeMethods.ABE_RIGHT, APP_BAR_WIDTH);
                ToggleModeButton.Content = "↔";
            }
        }
        #endregion
    }
}
