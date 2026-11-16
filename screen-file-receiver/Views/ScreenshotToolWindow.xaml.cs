using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace screen_file_transmit
{
    public partial class ScreenshotToolWindow : Window
    {
        private readonly MainWindowViewModel _mainVm;

        private IntPtr _selectedHwnd;
        private Rect _selectedRegion;
        private Bitmap _lastCapture;
        private SelectionBorderWindow _selectionBorderWindow;
        private DispatcherTimer _followTimer;
        private DispatcherTimer _expandDelayTimer;
        private DispatcherTimer _collapseDelayTimer;

        private bool _isCollapsed;
        private bool _isDragging;
        private bool _isPinned;
        private bool _positionInitialized;
        private const double _dockThreshold = 6;
        private static readonly TimeSpan ExpandDelay = TimeSpan.FromMilliseconds(250);
        private static readonly TimeSpan CollapseDelay = TimeSpan.FromSeconds(3);

        // 自动翻页相关
        private DispatcherTimer _autoFlipTimer;
        private bool _isAutoFlipping;
        private int _lastProcessedPage = -1;
        private string _lastDecodeError;   // 最近一次解码异常，用于超时提示诊断
        private DateTime _lastRefireTime;  // 上次「补发翻页」的时间，用于限频
        private const int RefireIntervalMs = 2000;  // 页面未变化时补发翻页的最小间隔
        private int _autoFlipTotalPages;
        private string _autoFlipFileName;
        private FlipMethod _flipMethod = FlipMethod.LeftRight;
        private DateTime _lastPageTurnTime;
        private const int AutoFlipTimeoutSeconds = 30;

        // 自动翻页：模拟鼠标点击翻页
        private bool _useClick;
        private double _clickOffsetPercentX = 99;
        private double _clickOffsetPercentY = 10;
        private System.Drawing.Point _savedCursorPos;
        private bool _cursorSaved;
        private bool _cursorMoved;

        // 自动翻页：全局键盘钩子，捕获 ESC 打断翻页（此时焦点在发送端窗口）
        private IntPtr _autoFlipKeyboardHook;
        private NativeMethods.LowLevelKeyboardProc _autoFlipKeyboardProc;

        // 自动翻页：数据校验（解码无误才翻页）
        private int _pendingPage = -1;              // 待重试的页码
        private int _pendingRetries;                // 当前页校验失败重试次数
        private int _underfullRetries;              // 当前页「未满载」重试次数（独立计数）
        private bool _disableUpscaleUsed;           // 本页是否已尝试过「屏蔽放大算法」的降级解码
        private const int MaxPageRetries = 3;
        private const int MaxUnderfullRetries = 5;  // 未满载连续重试上限，超过后换算法再试一次
        private const double DecodeQrRatioThreshold = 0.95;
        private string _clickRectDebug = "";

        // 编辑选区相关
        private bool _isEditMode;
        private double _offsetLeftPhys;
        private double _offsetTopPhys;
        private double _offsetRightPhys;
        private double _offsetBottomPhys;
        private NativeMethods.RECT _offsetBaselineRect;
        private bool _isClosing = false;

        // 保存闪烁相关


        public ScreenshotToolWindow(MainWindowViewModel mainVm)
        {
            InitializeComponent();
            _mainVm = mainVm;
            ContentRendered += OnWindowContentRendered;

            _expandDelayTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = ExpandDelay };
            _expandDelayTimer.Tick += (s, e) =>
            {
                _expandDelayTimer.Stop();
                if (_isCollapsed)
                    ExpandToolbar();
            };

            _collapseDelayTimer = new DispatcherTimer(DispatcherPriority.Normal) { Interval = CollapseDelay };
            _collapseDelayTimer.Tick += (s, e) =>
            {
                _collapseDelayTimer.Stop();
                if (!_isCollapsed && !_isPinned)
                    CheckDockCollapse();
            };

            this.Closing += OnClosing;
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            _isClosing = true;
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 获取当前窗口的句柄和扩展样式
            var helper = new WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            int exStyle = (int)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            // 添加 WS_EX_TOOLWINDOW 样式
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE,
                (IntPtr)(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
        }

        private void OnWindowContentRendered(object sender, EventArgs e)
        {
            if (_positionInitialized) return;
            Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
            Top = 0;
            _positionInitialized = true;
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                // 自动翻页进行中：ESC 只停止翻页，保留当前选区，方便重新开始
                if (_isAutoFlipping)
                {
                    StopAutoFlip();
                    if (BtnAutoFlip != null)
                        BtnAutoFlip.IsChecked = false;
                    ShowSelectionBorder();
                    e.Handled = true;
                    return;
                }

                CancelSelection();
                e.Handled = true;
                return;
            }

            switch (e.Key)
            {
                case Key.F2:
                    if (Keyboard.Modifiers != ModifierKeys.Control)
                        return;
                    StartWindowSelection();
                    e.Handled = true;
                    break;
                case Key.F3:
                    if (Keyboard.Modifiers != ModifierKeys.Control)
                        return;
                    StartRegionSelection();
                    e.Handled = true;
                    break;
                case Key.F4:
                    if (Keyboard.Modifiers == ModifierKeys.Control)
                        ExecuteDecodeTest();
                    else
                        ExecuteCapture();
                    e.Handled = true;
                    break;
            }
        }

        private void DragGrip_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_isCollapsed)
            {
                ExpandToolbar();
            }

            _isDragging = true;
            DragMove();
            _isDragging = false;
            StartCollapseDelay();
        }

        private void Window_LocationChanged(object sender, EventArgs e)
        {
            if (!_isDragging)
                StartCollapseDelay();
        }

        private void Window_MouseEnter(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _collapseDelayTimer?.Stop();
            if (_isCollapsed)
            {
                _expandDelayTimer?.Start();
            }
        }

        private void Window_MouseLeave(object sender, System.Windows.Input.MouseEventArgs e)
        {
            _expandDelayTimer?.Stop();
            if (!_isCollapsed)
            {
                StartCollapseDelay();
            }
        }

        private void StartCollapseDelay()
        {
            if (_isPinned || _isCollapsed)
                return;
            _collapseDelayTimer?.Start();
        }

        private void BtnPin_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _isPinned = BtnPin.IsChecked == true;
            if (_isPinned)
            {
                _expandDelayTimer?.Stop();
                _collapseDelayTimer?.Stop();
                if (_isCollapsed)
                    ExpandToolbar();
                Top = 0;
            }
            else
            {
                StartCollapseDelay();
            }
        }

        private void CheckDockCollapse()
        {
            if (_isPinned) return;
            if (Top <= _dockThreshold && !_isCollapsed)
            {
                CollapseToolbar();
            }
        }

        private void CollapseToolbar()
        {
            if (_isCollapsed) return;
            _isCollapsed = true;
            ((Grid)(RootBorder.Child)).Height = 2;
        }

        private void ExpandToolbar()
        {
            if (!_isCollapsed) return;
            _isCollapsed = false;
            ((Grid)(RootBorder.Child)).Height = double.NaN;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void BtnSelectWindow_Click(object sender, RoutedEventArgs e)
        {
            StartWindowSelection();
        }

        private void BtnSelectRegion_Click(object sender, RoutedEventArgs e)
        {
            StartRegionSelection();
        }

        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCapture();
        }

        private void BtnDecodeTest_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDecodeTest();
        }

        private void BtnCancelSelection_Click(object sender, RoutedEventArgs e)
        {
            CancelSelection();
        }

        private void BtnEditSelection_Click(object sender, RoutedEventArgs e)
        {
            ToggleEditMode();
        }

        private void CancelSelection()
        {
            if (_isAutoFlipping)
            {
                StopAutoFlip();
                BtnAutoFlip.IsChecked = false;
            }

            if (_selectionBorderWindow != null)
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            _selectedHwnd = IntPtr.Zero;
            _selectedRegion = Rect.Empty;
            _isEditMode = false;
            ResetWindowOffsets();
            UpdateEditButtonState();
            StopFollowTimer();
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            UpdateActionButtonState();
        }

        private void ResetWindowOffsets()
        {
            _offsetLeftPhys = 0;
            _offsetTopPhys = 0;
            _offsetRightPhys = 0;
            _offsetBottomPhys = 0;
            _offsetBaselineRect = new NativeMethods.RECT();
        }

        private void StartWindowSelection()
        {
            _isEditMode = false;
            ResetWindowOffsets();
            UpdateEditButtonState();
            if (_selectionBorderWindow != null)
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            StopFollowTimer();
            _selectedHwnd = IntPtr.Zero;
            _selectedRegion = Rect.Empty;
            var overlay = new WindowSelectOverlay();
            overlay.Owner = this;
            overlay.Closed += (s, args) =>
            {
                if (overlay.SelectedHwnd != IntPtr.Zero)
                {
                    _selectedHwnd = overlay.SelectedHwnd;
                    _selectedRegion = Rect.Empty;
                    StartFollowTimer();
                    EnableEditMode();
                }

                UpdateEditButtonState();
                UpdateActionButtonState();
                Activate();
                Focus();
            };
            overlay.Show();
            overlay.Activate();
        }

        private void StartRegionSelection()
        {
            _isEditMode = false;
            ResetWindowOffsets();
            UpdateEditButtonState();
            if (_selectionBorderWindow != null)
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            StopFollowTimer();
            _selectedHwnd = IntPtr.Zero;
            _selectedRegion = Rect.Empty;
            var overlay = new RegionSelectOverlay();
            overlay.Owner = this;
            overlay.Closed += (s, args) =>
            {
                if (overlay.SelectedRegion.Width > 0 && overlay.SelectedRegion.Height > 0)
                {
                    _selectedRegion = overlay.SelectedRegion;
                    _selectedHwnd = IntPtr.Zero;
                    StopFollowTimer();
                    ShowSelectionBorder(_selectedRegion);
                    EnableEditMode();
                }

                UpdateEditButtonState();
                UpdateActionButtonState();
                Activate();
                Focus();
            };
            overlay.Show();
            overlay.Activate();
        }

        private void ShowSelectionBorder(Rect region)
        {
            EnsureSelectionBorderWindow();

            _selectionBorderWindow.Left = region.X;
            _selectionBorderWindow.Top = region.Y;
            _selectionBorderWindow.Width = region.Width;
            _selectionBorderWindow.Height = region.Height;
            _selectionBorderWindow.EditMode = _isEditMode;
            _selectionBorderWindow.Show();
            SyncClickMarker();
        }

        private void SyncClickMarker()
        {
            if (_selectionBorderWindow == null) return;

            bool show = _useClick && _flipMethod == FlipMethod.MouseClick;
            _selectionBorderWindow.SetClickMarker(show, _clickOffsetPercentX, _clickOffsetPercentY);
        }

        private void EnsureSelectionBorderWindow()
        {
            if (_isClosing) return;
            if (_selectionBorderWindow == null)
            {
                _selectionBorderWindow = new SelectionBorderWindow();
                _selectionBorderWindow.Owner = this;
                if (_isEditMode)
                    _selectionBorderWindow.Resized += OnSelectionBorderResized;
            }
        }

        private void HideSelectionBorder()
        {
            _selectionBorderWindow?.Hide();
            StopFollowTimer();
        }

        private void StartFollowTimer()
        {
            if (_followTimer == null)
            {
                _followTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(16)
                };
                _followTimer.Tick += (s, e) =>
                {
                    if (_selectionBorderWindow?.IsResizing == true)
                        return;
                    UpdateSelectionBorderToHwnd();
                };
            }

            _followTimer.Start();
            UpdateSelectionBorderToHwnd();
        }

        private void StopFollowTimer()
        {
            _followTimer?.Stop();
        }

        private void UpdateSelectionBorderToHwnd()
        {
            if (_isClosing)
            {
                return;
            }

            if (_selectedHwnd == IntPtr.Zero)
                return;

            if (!NativeMethods.IsWindow(_selectedHwnd))
            {
                CancelSelection();
                return;
            }

            NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
            if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                rc = dwmRc;
            if (rc.Right <= rc.Left || rc.Bottom <= rc.Top)
                return;

            // 检测窗口大小变化并缩放偏移
            int prevWidth = _offsetBaselineRect.Right - _offsetBaselineRect.Left;
            int prevHeight = _offsetBaselineRect.Bottom - _offsetBaselineRect.Top;
            int currWidth = rc.Right - rc.Left;
            int currHeight = rc.Bottom - rc.Top;

            if (prevWidth > 0 && prevHeight > 0 && _isEditMode)
            {
                if (currWidth != prevWidth || currHeight != prevHeight)
                {
                    double scaleX = (double)currWidth / prevWidth;
                    double scaleY = (double)currHeight / prevHeight;
                    _offsetLeftPhys *= scaleX;
                    _offsetTopPhys *= scaleY;
                    _offsetRightPhys *= scaleX;
                    _offsetBottomPhys *= scaleY;
                }
            }

            if (_isEditMode)
            {
                _offsetBaselineRect = rc;
            }

            const int borderThickness = 2;
            int physLeft = rc.Left - borderThickness + (int)Math.Round(_offsetLeftPhys);
            int physTop = rc.Top - borderThickness + (int)Math.Round(_offsetTopPhys);
            int physRight = rc.Right + borderThickness + (int)Math.Round(_offsetRightPhys);
            int physBottom = rc.Bottom + borderThickness + (int)Math.Round(_offsetBottomPhys);

            var logicalTopLeft =
                ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(physLeft, physTop));
            var logicalBottomRight =
                ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(physRight, physBottom));

            EnsureSelectionBorderWindow();
            _selectionBorderWindow.Left = logicalTopLeft.X;
            _selectionBorderWindow.Top = logicalTopLeft.Y;
            _selectionBorderWindow.Width = Math.Max(4, logicalBottomRight.X - logicalTopLeft.X);
            _selectionBorderWindow.Height = Math.Max(4, logicalBottomRight.Y - logicalTopLeft.Y);
            _selectionBorderWindow.EditMode = _isEditMode;

            if (!_selectionBorderWindow.IsVisible)
                _selectionBorderWindow.Show();

            var borderHwnd = new WindowInteropHelper(_selectionBorderWindow).Handle;
            if (borderHwnd == IntPtr.Zero)
                return;

            IntPtr zAnchor = NativeMethods.GetAncestor(_selectedHwnd, NativeMethods.GA_ROOT);
            if (zAnchor == IntPtr.Zero)
                zAnchor = _selectedHwnd;

            IntPtr below = NativeMethods.GetWindow(borderHwnd, NativeMethods.GW_HWNDNEXT);
            if (below != zAnchor)
            {
                IntPtr insertAfter = NativeMethods.GetWindow(zAnchor, NativeMethods.GW_HWNDPREV);
                if (insertAfter == IntPtr.Zero)
                    insertAfter = NativeMethods.HWND_TOP;

                NativeMethods.SetWindowPos(
                    borderHwnd,
                    insertAfter,
                    0, 0, 0, 0,
                    NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            }
        }

        private void ShowSelectionBorder()
        {
            SyncClickMarker();
            if (_selectedHwnd != IntPtr.Zero)
            {
                StartFollowTimer();
            }
            else if (_selectedRegion.Width > 0 && _selectedRegion.Height > 0)
            {
                ShowSelectionBorder(_selectedRegion);
            }
        }

        private void EnableEditMode()
        {
            if (_selectedHwnd == IntPtr.Zero && _selectedRegion == Rect.Empty)
                return;

            _isEditMode = true;
            UpdateEditButtonState();

            EnsureSelectionBorderWindow();
            _selectionBorderWindow.EditMode = true;
            _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            _selectionBorderWindow.Resized += OnSelectionBorderResized;

            if (_selectedHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                    rc = dwmRc;
                _offsetBaselineRect = rc;
            }
        }

        private void ToggleEditMode()
        {
            if (_selectedHwnd == IntPtr.Zero && _selectedRegion == Rect.Empty)
            {
                _isEditMode = false;
                UpdateEditButtonState();
                return;
            }

            _isEditMode = !_isEditMode;
            UpdateEditButtonState();

            EnsureSelectionBorderWindow();
            _selectionBorderWindow.EditMode = _isEditMode;

            if (_isEditMode)
            {
                EnableEditMode();
            }
            else
            {
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            }
        }

        private void UpdateEditButtonState()
        {
            if (BtnEditSelection != null)
            {
                BtnEditSelection.IsEnabled = _selectedHwnd != IntPtr.Zero || _selectedRegion != Rect.Empty;
                BtnEditSelection.Background = _isEditMode
                    ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xE4, 0xFF))
                    : System.Windows.Media.Brushes.Transparent;
            }
        }

        private void UpdateActionButtonState()
        {
            bool hasSelection = _selectedHwnd != IntPtr.Zero || _selectedRegion != Rect.Empty;
            if (BtnCancelSelection != null)
                BtnCancelSelection.IsEnabled = hasSelection;
            if (BtnCapture != null)
                BtnCapture.IsEnabled = hasSelection;
            if (BtnDecodeTest != null)
                BtnDecodeTest.IsEnabled = hasSelection;
            if (BtnAutoFlip != null)
                BtnAutoFlip.IsEnabled = hasSelection;
        }

        private void OnSelectionBorderResized(object sender, RectChangedEventArgs e)
        {
            var newRect = e.NewRect;

            if (_selectedHwnd != IntPtr.Zero)
            {
                // 窗口模式：计算新物理偏移
                NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                    rc = dwmRc;

                // 新边框逻辑坐标转物理坐标
                var newPhysTL =
                    ScreenCaptureHelper.LogicalToPhysical(this, new System.Windows.Point(newRect.Left, newRect.Top));
                var newPhysBR = ScreenCaptureHelper.LogicalToPhysical(this,
                    new System.Windows.Point(newRect.Left + newRect.Width, newRect.Top + newRect.Height));

                const int borderThickness = 2;
                int defaultPhysLeft = rc.Left - borderThickness;
                int defaultPhysTop = rc.Top - borderThickness;
                int defaultPhysRight = rc.Right + borderThickness;
                int defaultPhysBottom = rc.Bottom + borderThickness;

                _offsetLeftPhys = newPhysTL.X - defaultPhysLeft;
                _offsetTopPhys = newPhysTL.Y - defaultPhysTop;
                _offsetRightPhys = newPhysBR.X - defaultPhysRight;
                _offsetBottomPhys = newPhysBR.Y - defaultPhysBottom;
            }
            else if (_selectedRegion != Rect.Empty)
            {
                // 区域模式：直接更新选区
                _selectedRegion = newRect;
            }
        }

        private bool HasWindowOffset()
        {
            return Math.Abs(_offsetLeftPhys) > 0.5 || Math.Abs(_offsetTopPhys) > 0.5
                                                   || Math.Abs(_offsetRightPhys) > 0.5 ||
                                                   Math.Abs(_offsetBottomPhys) > 0.5;
        }

        private async void ExecuteCapture()
        {
            // 选区模式不 Hide 边框：红框已由 StripBorder 裁掉，Hide/Show 只会让画面抖动。
            // 窗口模式仍需隐藏，因为边框压在目标窗口上。
            bool hideBorderForCapture = !(_selectedRegion.Width > 0 && _selectedRegion.Height > 0);
            if (hideBorderForCapture)
                HideSelectionBorder();
            string outputDir = _mainVm?.OutputFilePath;

            if (string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            Bitmap bmp = null;
            if (_selectedHwnd != IntPtr.Zero)
            {
                if (HasWindowOffset())
                {
                    // 存在自定义偏移，按区域从屏幕截图
                    NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                    if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                        rc = dwmRc;

                    // 边框内部区域（不含 2px 红色边框）
                    int x = rc.Left + (int)Math.Round(_offsetLeftPhys);
                    int y = rc.Top + (int)Math.Round(_offsetTopPhys);
                    int w = (rc.Right - rc.Left) + (int)Math.Round(_offsetRightPhys - _offsetLeftPhys);
                    int h = (rc.Bottom - rc.Top) + (int)Math.Round(_offsetBottomPhys - _offsetTopPhys);

                    var region = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
                    bmp = ScreenCaptureHelper.CaptureRegion(region);
                }
                else
                {
                    bmp = ScreenCaptureHelper.CaptureWindow(_selectedHwnd);
                }
            }
            else if (_selectedRegion.Width > 0 && _selectedRegion.Height > 0)
            {
                var physTL = ScreenCaptureHelper.LogicalToPhysical(this,
                    new System.Windows.Point(_selectedRegion.X, _selectedRegion.Y));
                var physBR = ScreenCaptureHelper.LogicalToPhysical(this,
                    new System.Windows.Point(_selectedRegion.Right, _selectedRegion.Bottom));
                var rc = new Rectangle(
                    (int)physTL.X,
                    (int)physTL.Y,
                    (int)(physBR.X - physTL.X),
                    (int)(physBR.Y - physTL.Y));
                bmp = ScreenCaptureHelper.CaptureRegion(rc);

                // 与 CaptureSelection 保持一致：裁掉叠加在选区上的红框残留。
                // 红框虽然已 Hide，但 Hide 不保证立刻刷新屏幕合成，
                // 这道裁切兜住偶发的红框入画。
                bmp = ScreenCaptureHelper.StripBorder(bmp, SelectionBorderWindow.BorderThicknessPx);
            }
            else
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectWindowOrRegion"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            if (bmp == null)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFailed"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _lastCapture?.Dispose();
            _lastCapture = bmp;
            UpdateActionButtonState();
            if (hideBorderForCapture)
                ShowSelectionBorder();

            string fileName = null;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    var meta = ImageDecoder.ReadMetadata(metaBmp);
                    // 与自动翻页 SaveCapture 保持一致：{meta.FileName}_{meta.FileId}_{meta.CurrentPage:00000}.png
                    if (!string.IsNullOrWhiteSpace(meta.FileName))
                        fileName = Path.GetFileNameWithoutExtension(meta.FileName) +
                                   $"_{meta.FileId}_{meta.CurrentPage:00000}.png";
                }
            }
            catch
            {
            }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName =
                    $"{Properties.Resources.ResourceManager.GetString("Screenshot_FileNamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.png";
            }

            fileName = ScreenCaptureHelper.SanitizeFileName(fileName);
            string fullPath = Path.Combine(outputDir, fileName);
            fullPath = ScreenCaptureHelper.GetUniqueFilePath(fullPath);

            try
            {
                var bitmapSource = ScreenCaptureHelper.ToBitmapSource(bmp);
                ScreenCaptureHelper.SavePng(bitmapSource, fullPath);
                ShowGeneratedIndicator();
                ToastNotification.Show(
                    string.Format(Properties.Resources.ResourceManager.GetString("Toast_SavedTo"), fullPath),
                    Properties.Resources.ResourceManager.GetString("Toast_ScreenshotSuccess"),
                    MessageBoxImage.Information);
                if (_mainVm != null)
                    await _mainVm.AddFilesAsync(new[] { fullPath });
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(Properties.Resources.ResourceManager.GetString("Error_SaveFailed"), ex.Message),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void ExecuteDecodeTest()
        {
            Bitmap bmp = CaptureSelection(true);
            if (bmp == null)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFailed"),
                    Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                string metaInfo;
                string decodeInfo;
                try
                {
                    using (bmp)
                    {
                        var meta = ImageDecoder.ReadMetadata(bmp);
                        metaInfo = meta?.Metadata != null && meta.Metadata.Length >= 4
                            ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaOk"),
                                meta.FileName, meta.MaxRows, meta.MaxCols, meta.CurrentPage, meta.TotalPages)
                            : Properties.Resources.ResourceManager.GetString("DecodeTest_MetaNotFound");

                        var decodeResult = ImageDecoder.DecodeImageWithMetadata(bmp, false);
                        bool decodeOk = decodeResult?.DataBlocks?.Count > 0;
                        decodeInfo = decodeOk
                            ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeSuccess"),
                                decodeResult.DataBlocks.Count)
                            : Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeFailed");
                    }
                }
                catch (Exception ex)
                {
                    metaInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaError"),
                        ex.Message);
                    decodeInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeError"),
                        ex.Message);
                }

                MessageBox.Show($"{metaInfo}\n{decodeInfo}",
                    Properties.Resources.ResourceManager.GetString("MsgBox_Title_DecodeTestResult"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(Properties.Resources.ResourceManager.GetString("Error_TestFailed"), ex.Message),
                    Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private Bitmap CaptureSelection(bool hideBorder)
        {
            // 选区模式不 Hide 边框：红框已由 StripBorder 裁掉，
            // Hide/Show 只会让画面抖动。窗口模式确实需要隐藏（边框压在窗口上）。
            bool needHide = hideBorder && !(_selectedRegion.Width > 0 && _selectedRegion.Height > 0);
            if (needHide)
                HideSelectionBorder();
            try
            {
                Bitmap bmp = null;
                if (_selectedHwnd != IntPtr.Zero)
                {
                    if (HasWindowOffset())
                    {
                        NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                        if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                            rc = dwmRc;

                        int x = rc.Left + (int)Math.Round(_offsetLeftPhys);
                        int y = rc.Top + (int)Math.Round(_offsetTopPhys);
                        int w = (rc.Right - rc.Left) + (int)Math.Round(_offsetRightPhys - _offsetLeftPhys);
                        int h = (rc.Bottom - rc.Top) + (int)Math.Round(_offsetBottomPhys - _offsetTopPhys);

                        var region = new Rectangle(x, y, Math.Max(1, w), Math.Max(1, h));
                        bmp = ScreenCaptureHelper.CaptureRegion(region);
                    }
                    else
                    {
                        bmp = ScreenCaptureHelper.CaptureWindow(_selectedHwnd);
                    }
                }
                else if (_selectedRegion.Width > 0 && _selectedRegion.Height > 0)
                {
                    var physTL = ScreenCaptureHelper.LogicalToPhysical(this,
                        new System.Windows.Point(_selectedRegion.X, _selectedRegion.Y));
                    var physBR = ScreenCaptureHelper.LogicalToPhysical(this,
                        new System.Windows.Point(_selectedRegion.Right, _selectedRegion.Bottom));
                    var rc = new Rectangle(
                        (int)physTL.X,
                        (int)physTL.Y,
                        (int)(physBR.X - physTL.X),
                        (int)(physBR.Y - physTL.Y));
                    bmp = ScreenCaptureHelper.CaptureRegion(rc);

                    // 选区截图会带上叠加在选区上的红框，按线宽把四周裁掉
                    bmp = ScreenCaptureHelper.StripBorder(bmp, SelectionBorderWindow.BorderThicknessPx);
                }

                return bmp;
            }
            finally
            {
                if (needHide)
                    ShowSelectionBorder();
            }
        }

        private async Task<String> SaveCapture(Bitmap bmp, ImageDecoder.MetadataResult meta, bool overwrite = false)
        {
            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return null;
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            string fileName = null;
            if (meta != null && !string.IsNullOrWhiteSpace(meta.FileName))
                fileName = Path.GetFileNameWithoutExtension($"{meta.FileName}") +
                           $"_{meta.FileId}_{meta.CurrentPage:00000}.png";

            if (string.IsNullOrWhiteSpace(fileName))
                fileName =
                    $"{Properties.Resources.ResourceManager.GetString("Screenshot_FileNamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.png";

            fileName = ScreenCaptureHelper.SanitizeFileName(fileName);
            string fullPath = Path.Combine(outputDir, fileName);
            // 重截替换：覆盖同名（不加 unique 后缀），确保落到同一文件路径
            if (!overwrite)
                fullPath = ScreenCaptureHelper.GetUniqueFilePath(fullPath);

            try
            {
                var bitmapSource = ScreenCaptureHelper.ToBitmapSource(bmp);
                ScreenCaptureHelper.SavePng(bitmapSource, fullPath);

                // 非自动翻页（手动单张）：落盘成功即「上次识别生成」，转深绿。
                // 自动翻页中保持黄色（自动翻页开启优先），翻页结束再定格到深绿。
                if (!_isAutoFlipping)
                    ShowGeneratedIndicator();
                //string toastTitle = string.Format(Properties.Resources.ResourceManager.GetString("Toast_AutoFlipPageSaved"), meta?.CurrentPage ?? 0, _autoFlipTotalPages);
                //ToastNotification.Show(string.Format(Properties.Resources.ResourceManager.GetString("Toast_SavedTo"), fullPath), toastTitle, MessageBoxImage.Information);
                if (_mainVm != null)
                    await _mainVm.AddFilesAsync(new[] { fullPath });
                return fullPath;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    string.Format(Properties.Resources.ResourceManager.GetString("Error_SaveFailed"), ex.Message),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }

            return null;
        }

        private void BtnAutoFlip_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (BtnAutoFlip.IsChecked == true)
                StartAutoFlip();
            else
                StopAutoFlip();
        }

        private void StartAutoFlip()
        {
            // 清掉上一次残留的圆点状态
            _selectionBorderWindow?.HideDot();

            bool hasSelection = _selectedHwnd != IntPtr.Zero || _selectedRegion != Rect.Empty;
            if (!hasSelection)
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectWindowOrRegion"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (!Directory.Exists(outputDir))
            {
                Directory.CreateDirectory(outputDir);
            }

            Bitmap bmp = CaptureSelection(true);
            if (bmp == null)
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFailed"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Error);
                return;
            }

            _lastProcessedPage = -1;
            _pendingPage = -1;
            _pendingRetries = 0;
            _underfullRetries = 0;
            _disableUpscaleUsed = false;
            _cursorSaved = false;
            _cursorMoved = false;
            _lastCapture?.Dispose();
            _lastCapture = bmp;
            UpdateActionButtonState();

            ImageDecoder.MetadataResult meta = null;
            bool decodeOk = false;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    meta = ImageDecoder.ReadMetadata(metaBmp, false);
                }

                using (var tempBmp = new Bitmap(bmp))
                {
                    var decodeResult = ImageDecoder.DecodeImageWithMetadata(tempBmp, false);
                    decodeOk = decodeResult?.DataBlocks?.Count > 0;
                }
            }
            catch (Exception ex)
            {
                //ignore
                //bmp.Save("D:/test.bmp");
            }

            if (meta?.Metadata == null || meta.Metadata.Length < 4 || !decodeOk)
            {
                var noDataText = Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipNoData");
                MessageBox.Show(
                    noDataText,
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                BtnAutoFlip.IsChecked = false;
                ShowSelectionBorder();
                return;
            }

            var confirmText = Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipConfirm");
            var confirmResult = MessageBox.Show(
                confirmText,
                Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.OK)
            {
                BtnAutoFlip.IsChecked = false;
                ShowSelectionBorder();
                return;
            }

            var configDialog = new AutoFlipConfigDialog(_selectedHwnd);

            // 拾取/条码算出百分比后，实时同步到本窗口字段并刷新选区框上的点击位置高亮
            configDialog.OnPercentChanged = (px, py) =>
            {
                _clickOffsetPercentX = px;
                _clickOffsetPercentY = py;
                SyncClickMarker();
            };

            // 直接把选区控件的 DIP 坐标传给对话框，蒙版用它显示，避免重新计算导致错位
            var border = _selectionBorderWindow;
            if (border != null)
            {
                configDialog.SetMaskRectDip(border.Left, border.Top, border.Width, border.Height);
            }

            // 用条码框定内容区域，计算默认点击位置百分比（99%/10%）
            try
            {
                using (var clickBmp = new Bitmap(bmp))
                {
                    configDialog.SetScreenshot(clickBmp);
                }
            }
            catch
            {
                //ignore
            }

            configDialog.Owner = this;
            var configResult = configDialog.ShowDialog();
            if (configResult != true)
            {
                BtnAutoFlip.IsChecked = false;
                ShowSelectionBorder();
                return;
            }

            _flipMethod = configDialog.SelectedMethod;
            _useClick = configDialog.UseMouseClick;
            _clickOffsetPercentX = configDialog.ClickOffsetPercentX;
            _clickOffsetPercentY = configDialog.ClickOffsetPercentY;

            // 用户选了模拟鼠标点击，立刻在选区控件上标注点击位置
            SyncClickMarker();

            Thread.Sleep(200);
            if (_isClosing) return;
            ShowAutoFlipIndicator();
            SaveCapture(bmp, meta);
            _lastProcessedPage = meta.CurrentPage;
            _lastRefireTime = DateTime.MinValue;
            _autoFlipTotalPages = meta.TotalPages;
            _autoFlipFileName = meta.FileName;

            if (meta.CurrentPage >= meta.TotalPages)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Information);
                BtnAutoFlip.IsChecked = false;
                HideSelectionBorder();
                return;
            }

            SimulatePageTurn();
            ShowSelectionBorder();

            _isAutoFlipping = true;
            HookAutoFlipKeyboard();
            _autoFlipTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _autoFlipTimer.Tick += AutoFlipTick;
            _autoFlipTimer.Start();
        }

        /// <summary>
        /// 深绿常亮：上一次识别生成成功。圆点画在选区控件上，跟随选区移动。
        /// </summary>
        private void ShowGeneratedIndicator()
        {
            _selectionBorderWindow?.ShowGeneratedSteady();
        }

        /// <summary>
        /// 黄色常亮：自动翻页开启中。在 sleep(200) 之后、截图之前切换。
        /// </summary>
        private void ShowAutoFlipIndicator()
        {
            _selectionBorderWindow?.ShowAutoFlipSteady();
        }

        private void AutoFlipTick(object sender, EventArgs e)
        {
            if (!_isAutoFlipping) return;

            Bitmap bmp = CaptureSelection(true);
            if (bmp == null) return;

            ImageDecoder.MetadataResult meta = null;
            ImageDecoder.DecodeResult decodeResult = null;
            bool decodeVerified = false;
            _lastDecodeError = null;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    meta = ImageDecoder.ReadMetadata(metaBmp);
                }

                using (var tempBmp = new Bitmap(bmp))
                {
                    decodeResult = ImageDecoder.DecodeImageWithMetadata(tempBmp, false);
                    // 元数据可读不代表数据区解码无误，必须整页校验通过才允许翻页
                    decodeVerified = ImageDecoder.VerifyDecodeResult(decodeResult, meta, DecodeQrRatioThreshold);
                }
            }
            catch (Exception ex)
            {
                _lastDecodeError = ex.Message;
            }

            if ((DateTime.Now - _lastPageTurnTime).TotalSeconds > AutoFlipTimeoutSeconds)
            {
                bmp.Dispose();
                StopAutoFlip();
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(
                    string.Format(
                        Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipTimeoutDetail"),
                        _lastProcessedPage, _pendingPage, _lastDecodeError ?? "-"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                ShowSelectionBorder();
                return;
            }

            if (meta?.Metadata == null || meta.Metadata.Length < 4)
            {
                bmp.Dispose();
                return;
            }

            // 页面未变化：等待发送端完成翻页，不做任何处理。
            // 但若该页已进入校验重试流程（_pendingPage 命中），说明本页仍需推进，
            // 不能被这里短路——否则校验失败/未满载会永远卡在本页，直到超时。
            if (_lastProcessedPage == meta.CurrentPage && _pendingPage != meta.CurrentPage)
            {
                // 本页已识别成功且是末页：翻页动作在上一轮已发出，这里只需定格完成提示，
                // 不再补翻（否则最后一轮会多翻一次）。
                if (meta.CurrentPage >= meta.TotalPages)
                {
                    bmp.Dispose();
                    StopAutoFlip();
                    BtnAutoFlip.IsChecked = false;
                    MessageBox.Show(
                        Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"),
                        Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Information);
                    ShowSelectionBorder();
                    return;
                }

                // 本页已识别成功（_lastProcessedPage 已记录该页，且不在重试队列中）：
                // 发送端可能没接收到上一次翻页信号，补发一次，避免卡在本页直到超时。
                // 每 500ms 的 tick 会重复命中这里，故按 RefireIntervalMs 限频，避免刷爆发送端。
                if ((DateTime.Now - _lastRefireTime).TotalMilliseconds >= RefireIntervalMs)
                {
                    _lastRefireTime = DateTime.Now;
                    bmp.Dispose();
                    SimulatePageTurn();
                    return;
                }

                bmp.Dispose();
                return;
            }

            // 新页面：校验失败计数与未满载计数分开维护，互不干扰
            if (_pendingPage != meta.CurrentPage)
            {
                _pendingPage = meta.CurrentPage;
                _pendingRetries = 0;
                _underfullRetries = 0;
                _disableUpscaleUsed = false;
            }

            // 新页面：解码无误才保存并翻页，否则留在本页重试
            if (!decodeVerified)
            {
                bmp.Dispose();

                _pendingRetries++;
                if (_pendingRetries > MaxPageRetries)
                {
                    StopAutoFlip();
                    BtnAutoFlip.IsChecked = false;
                    MessageBox.Show(
                        string.Format(
                            Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipVerifyFailed"),
                            meta.CurrentPage, MaxPageRetries),
                        Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    ShowSelectionBorder();
                }

                return;
            }

            // 解码通过但「非末页未满载」：留在本页重截，不保存不翻页，直到截到满或重试耗尽。
            // IsUnderfull 内部已含末页判定（末页天然不满为 false），故无需再判末页。
            if (decodeResult != null && decodeResult.IsUnderfull)
            {
                _underfullRetries++;

                // 前 5 次：先等 200ms 让发送端把剩余单元格刷出来，再重截重试。
                // 同时把降级标记复位，下一 tick 若仍不满，第 6 次会走「屏蔽放大」路径。
                if (_underfullRetries <= MaxUnderfullRetries)
                {
                    Thread.Sleep(200);
                    if (_isClosing)
                    {
                        bmp.Dispose();
                        return;
                    }

                    _disableUpscaleUsed = false;
                    bmp.Dispose();
                    return;
                }

                // 5 次重试仍不满：屏蔽放大算法换一种识别方式，用当前帧再试一次
                if (!_disableUpscaleUsed)
                {
                    _disableUpscaleUsed = true;
                    ImageDecoder.DisableUpscale = true;
                    try
                    {
                        using (var retryBmp = new Bitmap(bmp))
                        {
                            decodeResult = ImageDecoder.DecodeImageWithMetadata(retryBmp, false);
                            decodeVerified = ImageDecoder.VerifyDecodeResult(decodeResult, meta, DecodeQrRatioThreshold);
                        }
                    }
                    catch (Exception)
                    {
                        //ignore
                    }
                    finally
                    {
                        ImageDecoder.DisableUpscale = false;
                    }

                    // 换算法后仍然未满（或校验失败）：不再重试，走下面的保底保存
                    if (decodeVerified && decodeResult != null && decodeResult.IsUnderfull)
                    {
                        // 换算法后解码出更多单元格但仍不满载：再等 200ms，下一 tick 重截
                        Thread.Sleep(200);
                        if (_isClosing)
                        {
                            bmp.Dispose();
                            return;
                        }

                        bmp.Dispose();
                        return;
                    }
                }

                // 重试耗尽仍不满：保底保存这张未满的（文件列表会标记 IsUnderfull），继续翻页不丢数据
                Thread.Sleep(200);
                if (_isClosing)
                {
                    bmp.Dispose();
                    return;
                }

                ShowAutoFlipIndicator();
                SaveCapture(bmp, meta, overwrite: true);
                bmp.Dispose();

                _lastProcessedPage = meta.CurrentPage;
                _lastRefireTime = DateTime.MinValue;
                _pendingPage = -1;
                _pendingRetries = 0;
                _underfullRetries = 0;
                _disableUpscaleUsed = false;
                _autoFlipTotalPages = meta.TotalPages;
                _autoFlipFileName = meta.FileName;

                if (meta.CurrentPage >= meta.TotalPages)
                {
                    StopAutoFlip();
                    BtnAutoFlip.IsChecked = false;
                    MessageBox.Show(
                        Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"),
                        Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    ShowSelectionBorder();
                    return;
                }

                SimulatePageTurn();
                return;
            }

            Thread.Sleep(200);
            if (_isClosing) return;

            ShowAutoFlipIndicator();
            SaveCapture(bmp, meta);
            bmp.Dispose();

            _lastProcessedPage = meta.CurrentPage;
            _lastRefireTime = DateTime.MinValue;
            _pendingPage = -1;
            _pendingRetries = 0;
            _underfullRetries = 0;
            _disableUpscaleUsed = false;
            _autoFlipTotalPages = meta.TotalPages;
            _autoFlipFileName = meta.FileName;

            if (meta.CurrentPage >= meta.TotalPages)
            {
                StopAutoFlip();
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Information);
                ShowSelectionBorder();
                return;
            }

            SimulatePageTurn();
        }

        private void StopAutoFlip()
        {
            _isAutoFlipping = false;

            // 停止翻页后隐藏点击位置标记
            if (_selectionBorderWindow != null)
                _selectionBorderWindow.SetClickMarker(false, 0, 0);
            if (_autoFlipTimer != null)
            {
                _autoFlipTimer.Stop();
                _autoFlipTimer.Tick -= AutoFlipTick;
                _autoFlipTimer = null;
            }

            UnhookAutoFlipKeyboard();
            RestoreCursor();

            // 自动翻页结束：本次有产物就定格深绿（上次识别生成），否则收掉圆点
            if (_lastCapture != null)
                ShowGeneratedIndicator();
            else
                _selectionBorderWindow?.HideDot();
        }

        /// <summary>
        /// 安装低级键盘钩子，用于在自动翻页期间（焦点可能在发送端窗口）按 ESC 打断
        /// </summary>
        private void HookAutoFlipKeyboard()
        {
            if (_autoFlipKeyboardHook != IntPtr.Zero) return;

            _autoFlipKeyboardProc = AutoFlipKeyboardHookCallback;
            _autoFlipKeyboardHook = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _autoFlipKeyboardProc,
                IntPtr.Zero,
                0);
        }

        private void UnhookAutoFlipKeyboard()
        {
            if (_autoFlipKeyboardHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_autoFlipKeyboardHook);
                _autoFlipKeyboardHook = IntPtr.Zero;
            }
        }

        private IntPtr AutoFlipKeyboardHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0
                && (wParam == (IntPtr)NativeMethods.WM_KEYDOWN || wParam == (IntPtr)NativeMethods.WM_SYSKEYDOWN))
            {
                var kbd = System.Runtime.InteropServices.Marshal.PtrToStructure<NativeMethods.KBDLLHOOKSTRUCT>(lParam);
                if (kbd.vkCode == NativeMethods.VK_ESCAPE && _isAutoFlipping)
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        if (!_isAutoFlipping) return;
                        StopAutoFlip();
                        if (BtnAutoFlip != null)
                            BtnAutoFlip.IsChecked = false;
                        ShowSelectionBorder();
                    }));
                    // 吞掉 ESC，避免其传递到发送端窗口触发别的动作
                    return (IntPtr)1;
                }
            }

            return NativeMethods.CallNextHookEx(_autoFlipKeyboardHook, nCode, wParam, lParam);
        }

        /// <summary>
        /// 鼠标点击翻页会移动光标，停止自动翻页时把光标还原到原位置
        /// </summary>
        private void RestoreCursor()
        {
            if (!_cursorSaved || !_cursorMoved) return;

            try
            {
                var v = PhysicalToVirtualPoint(_savedCursorPos);
                NativeMethods.SetCursorPos(v.X, v.Y);
            }
            catch
            {
                //ignore
            }

            _cursorMoved = false;
        }

        private void SimulatePageTurn()
        {
            if (_flipMethod == FlipMethod.Wait)
            {
                _lastPageTurnTime = DateTime.Now;
                return;
            }

            if (_selectedHwnd != IntPtr.Zero)
            {
                NativeMethods.SetForegroundWindow(_selectedHwnd);
                //System.Threading.Thread.Sleep(50);
            }

            if (_useClick)
            {
                SimulateClick();
                return;
            }

            byte vk;
            switch (_flipMethod)
            {
                case FlipMethod.UpDown:
                    vk = NativeMethods.VK_DOWN;
                    break;
                case FlipMethod.PageUpDown:
                    vk = NativeMethods.VK_NEXT;
                    break;
                default:
                    vk = NativeMethods.VK_RIGHT;
                    break;
            }

            _lastPageTurnTime = DateTime.Now;
            NativeMethods.keybd_event(vk, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
        }

        /// <summary>
        /// 在选区/目标窗口内模拟一次鼠标点击（用于点击翻页的发送端）
        /// </summary>
        private void SimulateClick()
        {
            var pt = ComputeClickPoint();
            if (pt == null) return;

            // pt.Value 是物理像素；光标保存/比较也用物理，保证同基准
            if (TryGetPhysicalCursorPos(out var cursor))
            {
                if (!_cursorSaved)
                {
                    _savedCursorPos = new System.Drawing.Point(cursor.X, cursor.Y);
                    _cursorSaved = true;
                }

                if (cursor.X != pt.Value.X || cursor.Y != pt.Value.Y)
                    _cursorMoved = true;
            }

            // pt 是物理像素，但 SetCursorPos 接收的是 DPI 虚拟化坐标，必须先转换。
            // 与 RestoreCursor 里的 PhysicalToVirtualPoint 保持一致，否则缩放 >100% 时
            // 落点会被放大到错误位置，第一次可能碰巧命中、后续全部点空。
            var vp = PhysicalToVirtualPoint(pt.Value);

            // 点击前隐藏选框上的十字准星标记，避免标记遮挡点击位置影响识别；
            // 点击后恢复显示。
            _selectionBorderWindow?.HideClickMarker();

            // WPF 渲染是异步的：改 Visibility 只更新视觉树状态，
            // 真正绘制要等 Dispatcher 空闲时执行 Layout/Render pass。
            // 这里同步 Invoke 一次，强制处理挂起的 UI 消息，确保隐藏真正生效后再点击。
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));

            NativeMethods.SetCursorPos(vp.X, vp.Y);
            System.Threading.Thread.Sleep(50);

            // 真实点击
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);

            _selectionBorderWindow?.ShowClickMarker();
            Dispatcher.Invoke(System.Windows.Threading.DispatcherPriority.Render, new Action(() => { }));
            Trace.WriteLine("Click");

            _lastPageTurnTime = DateTime.Now;
        }

        /// <summary>
        /// 取光标真物理像素坐标，失败回退 GetCursorPos
        /// </summary>
        private static bool TryGetPhysicalCursorPos(out NativeMethods.POINT pt)
        {
            if (NativeMethods.GetPhysicalCursorPos(out pt))
                return true;
            return NativeMethods.GetCursorPos(out pt);
        }

        /// <summary>
        /// 把真物理像素点换算为 DPI 虚拟化坐标（SetCursorPos 接收的坐标系）
        /// </summary>
        private System.Drawing.Point PhysicalToVirtualPoint(System.Drawing.Point p)
        {
            double s = GetDpiScaleFactor();
            return new System.Drawing.Point(
                (int)Math.Round(p.X / s),
                (int)Math.Round(p.Y / s));
        }

        /// <summary>
        /// 把 DPI 虚拟化矩形换算为真物理像素矩形
        /// </summary>
        private NativeMethods.RECT VirtualToPhysicalRect(NativeMethods.RECT rc)
        {
            double s = GetDpiScaleFactor();
            return new NativeMethods.RECT
            {
                Left = (int)Math.Round(rc.Left * s),
                Top = (int)Math.Round(rc.Top * s),
                Right = (int)Math.Round(rc.Right * s),
                Bottom = (int)Math.Round(rc.Bottom * s)
            };
        }

        /// <summary>
        /// 取 DIP→物理 缩放比。以 WPF TransformToDevice 为准，与 ScreenCaptureHelper.LogicalToPhysical 同源。
        /// </summary>
        private double GetDpiScaleFactor()
        {
            try
            {
                var m = ScreenCaptureHelper.GetDpiScale(this);
                if (m.M11 > 0 && m.M22 > 0) return m.M11;
            }
            catch
            {
                //ignore
            }

            try
            {
                var sd = NativeMethods.GetDpiForSystem();
                if (sd != 0) return sd / 96.0;
            }
            catch
            {
                //ignore
            }

            return 1.0;
        }

        /// <summary>
        /// 计算点击落点：一律基于「选区」计算（选区控件矩形），与拾取百分比同源，
        /// 不基于选中窗口，避免 Windows 缩放下基准不一致导致落点偏移。
        /// 返回物理屏幕坐标。
        /// </summary>
        private System.Drawing.Point? ComputeClickPoint()
        {
            // 优先用选区控件的 DIP 矩形（与拾取百分比 _maskDip 同源）
            double l, t, w, h;
            var border = _selectionBorderWindow;
            if (border != null)
            {
                l = border.Left; t = border.Top; w = border.Width; h = border.Height;
            }
            else if (_selectedRegion.Width > 0 && _selectedRegion.Height > 0)
            {
                l = _selectedRegion.X; t = _selectedRegion.Y; w = _selectedRegion.Width; h = _selectedRegion.Height;
            }
            else
            {
                return null;
            }

            if (w <= 0 || h <= 0) return null;

            _clickRectDebug = $"({l:F1},{t:F1}) {w:F1}x{h:F1}(DIP)";

            double px = Math.Min(100, Math.Max(0, _clickOffsetPercentX)) / 100.0;
            double py = Math.Min(100, Math.Max(0, _clickOffsetPercentY)) / 100.0;

            // 在选区 DIP 空间按百分比算落点，再换算到物理像素。
            // 拾取用 GetPhysicalCursorPos(物理)，SetCursorPos 与之配套也走物理像素，
            // 故落点必须转物理；直接喂 DIP 会因缩放被「缩小」而偏移。
            double dipX = l + w * px;
            double dipY = t + h * py;
            double scale = GetDpiScaleFactor();

            return new System.Drawing.Point(
                (int)Math.Round(dipX * scale),
                (int)Math.Round(dipY * scale));
        }

        protected override void OnClosed(EventArgs e)
        {
            StopAutoFlip();
            _expandDelayTimer?.Stop();
            _collapseDelayTimer?.Stop();
            _lastCapture?.Dispose();
            _lastCapture = null;
            if (_selectionBorderWindow != null)
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            base.OnClosed(e);
        }
    }
}