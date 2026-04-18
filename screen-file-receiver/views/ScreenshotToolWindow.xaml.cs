using System;
using System.Drawing;
using System.IO;
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
        private int _autoFlipTotalPages;
        private string _autoFlipFileName;

        // 编辑选区相关
        private bool _isEditMode;
        private double _offsetLeftPhys;
        private double _offsetTopPhys;
        private double _offsetRightPhys;
        private double _offsetBottomPhys;
        private NativeMethods.RECT _offsetBaselineRect;

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
        }

 
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            // 获取当前窗口的句柄和扩展样式
            var helper = new WindowInteropHelper(this);
            IntPtr hWnd = helper.Handle;
            int exStyle = (int)NativeMethods.GetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE);

            // 添加 WS_EX_TOOLWINDOW 样式
            NativeMethods.SetWindowLong(hWnd, NativeMethods.GWL_EXSTYLE, (IntPtr)(exStyle | NativeMethods.WS_EX_TOOLWINDOW));
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
            overlay.Closed += (s, args) =>
            {
                if (overlay.SelectedHwnd != IntPtr.Zero)
                {
                    _selectedHwnd = overlay.SelectedHwnd;
                    _selectedRegion = Rect.Empty;
                    StartFollowTimer();
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
            overlay.Closed += (s, args) =>
            {
                if (overlay.SelectedRegion.Width > 0 && overlay.SelectedRegion.Height > 0)
                {
                    _selectedRegion = overlay.SelectedRegion;
                    _selectedHwnd = IntPtr.Zero;
                    StopFollowTimer();
                    ShowSelectionBorder(_selectedRegion);
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
        }

        private void EnsureSelectionBorderWindow()
        {
            if (_selectionBorderWindow == null)
            {
                _selectionBorderWindow = new SelectionBorderWindow();
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

            var logicalTopLeft = ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(physLeft, physTop));
            var logicalBottomRight = ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(physRight, physBottom));

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
            if (_selectedHwnd != IntPtr.Zero)
            {
                StartFollowTimer();
            }
            else if (_selectedRegion.Width > 0 && _selectedRegion.Height > 0)
            {
                ShowSelectionBorder(_selectedRegion);
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
                _selectionBorderWindow.Resized -= OnSelectionBorderResized;
                _selectionBorderWindow.Resized += OnSelectionBorderResized;

                // 初始化基线窗口矩形
                if (_selectedHwnd != IntPtr.Zero)
                {
                    NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                    if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                        rc = dwmRc;
                    _offsetBaselineRect = rc;
                }
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
                BtnDecodeTest.IsEnabled = _lastCapture != null;
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
                var newPhysTL = ScreenCaptureHelper.LogicalToPhysical(this, new System.Windows.Point(newRect.Left, newRect.Top));
                var newPhysBR = ScreenCaptureHelper.LogicalToPhysical(this, new System.Windows.Point(newRect.Left + newRect.Width, newRect.Top + newRect.Height));

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
                || Math.Abs(_offsetRightPhys) > 0.5 || Math.Abs(_offsetBottomPhys) > 0.5;
        }

        private void ExecuteCapture()
        {
            HideSelectionBorder();
            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
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
                var rc = new Rectangle(
                    (int)_selectedRegion.X,
                    (int)_selectedRegion.Y,
                    (int)_selectedRegion.Width,
                    (int)_selectedRegion.Height);
                bmp = ScreenCaptureHelper.CaptureRegion(rc);
            }
            else
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectWindowOrRegion"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (bmp == null)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFailed"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _lastCapture?.Dispose();
            _lastCapture = bmp;
            UpdateActionButtonState();
            ShowSelectionBorder();

            string fileName = null;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    var meta = ImageDecoder.ReadMetadata(metaBmp);
                    if (!string.IsNullOrWhiteSpace(meta.FileName))
                        fileName = meta.FileName;
                }
            }
            catch { }

            if (string.IsNullOrWhiteSpace(fileName))
            {
                fileName = $"{Properties.Resources.ResourceManager.GetString("Screenshot_FileNamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.png";
            }
            else
            {
                fileName = Path.ChangeExtension(fileName, ".png");
            }

            string fullPath = Path.Combine(outputDir, fileName);
            fullPath = ScreenCaptureHelper.GetUniqueFilePath(fullPath);

            try
            {
                var bitmapSource = ScreenCaptureHelper.ToBitmapSource(bmp);
                ScreenCaptureHelper.SavePng(bitmapSource, fullPath);
                ToastNotification.Show(string.Format(Properties.Resources.ResourceManager.GetString("Toast_SavedTo"), fullPath), Properties.Resources.ResourceManager.GetString("Toast_ScreenshotSuccess"), MessageBoxImage.Information);
                _mainVm?.AddFiles(new[] { fullPath });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ResourceManager.GetString("Error_SaveFailed"), ex.Message), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteDecodeTest()
        {
            if (_lastCapture == null)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFirst"), Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string metaInfo;
                try
                {
                    using (var metaBmp = new Bitmap(_lastCapture))
                    {
                        var meta = ImageDecoder.ReadMetadata(metaBmp);
                        metaInfo = meta?.Metadata != null && meta.Metadata.Length >= 4
                            ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaOk"), meta.FileName, meta.MaxRows, meta.MaxCols, meta.CurrentPage, meta.TotalPages)
                            : Properties.Resources.ResourceManager.GetString("DecodeTest_MetaNotFound");
                    }
                }
                catch (Exception ex)
                {
                    metaInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaError"), ex.Message);
                }

                string tempPath = Path.Combine(Path.GetTempPath(), $"scrtmp_{Guid.NewGuid()}.png");
                string decodeInfo;
                try
                {
                    using (var tempBmp = new Bitmap(_lastCapture))
                    {
                        var source = ScreenCaptureHelper.ToBitmapSource(tempBmp);
                        ScreenCaptureHelper.SavePng(source, tempPath);
                    }

                    var decodeResult = ImageDecoder.DecodeImageWithMetadata(tempPath, false);
                    bool decodeOk = decodeResult?.DataBlocks?.Count > 0;
                    decodeInfo = decodeOk
                        ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeSuccess"), decodeResult.DataBlocks.Count)
                        : Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeFailed");
                }
                catch (Exception ex)
                {
                    decodeInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeError"), ex.Message);
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }

                MessageBox.Show($"{metaInfo}\n{decodeInfo}", Properties.Resources.ResourceManager.GetString("MsgBox_Title_DecodeTestResult"), MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ResourceManager.GetString("Error_TestFailed"), ex.Message), Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private Bitmap CaptureSelection(bool hideBorder)
        {
            if (hideBorder)
                HideSelectionBorder();

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
                var rc = new Rectangle(
                    (int)_selectedRegion.X,
                    (int)_selectedRegion.Y,
                    (int)_selectedRegion.Width,
                    (int)_selectedRegion.Height);
                bmp = ScreenCaptureHelper.CaptureRegion(rc);
            }

            return bmp;
        }

        private void SaveCapture(Bitmap bmp, ImageDecoder.MetadataResult meta)
        {
            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string fileName = null;
            if (meta != null && !string.IsNullOrWhiteSpace(meta.FileName))
                fileName = Path.GetFileNameWithoutExtension($"{meta.FileName}" )+$"_{meta.FileId}_{meta.CurrentPage:0000}.png";

            if (string.IsNullOrWhiteSpace(fileName))
                fileName = $"{Properties.Resources.ResourceManager.GetString("Screenshot_FileNamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.png";

            string fullPath = Path.Combine(outputDir, fileName);
            fullPath = ScreenCaptureHelper.GetUniqueFilePath(fullPath);

            try
            {
                var bitmapSource = ScreenCaptureHelper.ToBitmapSource(bmp);
                ScreenCaptureHelper.SavePng(bitmapSource, fullPath);
                //string toastTitle = string.Format(Properties.Resources.ResourceManager.GetString("Toast_AutoFlipPageSaved"), meta?.CurrentPage ?? 0, _autoFlipTotalPages);
                //ToastNotification.Show(string.Format(Properties.Resources.ResourceManager.GetString("Toast_SavedTo"), fullPath), toastTitle, MessageBoxImage.Information);
                _mainVm?.AddFiles(new[] { fullPath });
            }
            catch (Exception ex)
            {
                MessageBox.Show(string.Format(Properties.Resources.ResourceManager.GetString("Error_SaveFailed"), ex.Message), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
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
            bool hasSelection = _selectedHwnd != IntPtr.Zero || _selectedRegion != Rect.Empty;
            if (!hasSelection)
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectWindowOrRegion"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Bitmap bmp = CaptureSelection(true);
            if (bmp == null)
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_ScreenshotFailed"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _lastCapture?.Dispose();
            _lastCapture = bmp;
            UpdateActionButtonState();

            ImageDecoder.MetadataResult meta = null;
            bool decodeOk = false;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    meta = ImageDecoder.ReadMetadata(metaBmp);
                }

                string tempPath = Path.Combine(Path.GetTempPath(), $"scrtmp_{Guid.NewGuid()}.png");
                try
                {
                    using (var tempBmp = new Bitmap(bmp))
                    {
                        var source = ScreenCaptureHelper.ToBitmapSource(tempBmp);
                        ScreenCaptureHelper.SavePng(source, tempPath);
                    }

                    var decodeResult = ImageDecoder.DecodeImageWithMetadata(tempPath, false);
                    decodeOk = decodeResult?.DataBlocks?.Count > 0;
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }
            }
            catch { }

            if (meta?.Metadata == null || meta.Metadata.Length < 4 || !decodeOk)
            {
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipNoData"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                ShowSelectionBorder();
                return;
            }

            var confirmResult = MessageBox.Show(
                Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipConfirm"),
                Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Question);

            if (confirmResult != MessageBoxResult.OK)
            {
                BtnAutoFlip.IsChecked = false;
                ShowSelectionBorder();
                return;
            }

            SaveCapture(bmp, meta);
            _lastProcessedPage = meta.CurrentPage;
            _autoFlipTotalPages = meta.TotalPages;
            _autoFlipFileName = meta.FileName;

            if (meta.CurrentPage >= meta.TotalPages)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                BtnAutoFlip.IsChecked = false;
                ShowSelectionBorder();
                return;
            }

            SimulateClickRightSide();
            ShowSelectionBorder();

            _isAutoFlipping = true;
            _autoFlipTimer = new DispatcherTimer(DispatcherPriority.Normal)
            {
                Interval = TimeSpan.FromMilliseconds(100)
            };
            _autoFlipTimer.Tick += AutoFlipTick;
            _autoFlipTimer.Start();
        }

        private void AutoFlipTick(object sender, EventArgs e)
        {
            if (!_isAutoFlipping) return;

            Bitmap bmp = CaptureSelection(false);
            if (bmp == null) return;

            ImageDecoder.MetadataResult meta = null;
            try
            {
                using (var metaBmp = new Bitmap(bmp))
                {
                    meta = ImageDecoder.ReadMetadata(metaBmp);
                }
            }
            catch { }

            if (meta?.Metadata == null || meta.Metadata.Length < 4)
            {
                bmp.Dispose();
                return;
            }

            if (meta.CurrentPage == _lastProcessedPage)
            {
                bmp.Dispose();
                return;
            }

            SaveCapture(bmp, meta);
            bmp.Dispose();
            _lastProcessedPage = meta.CurrentPage;

            if (meta.CurrentPage >= _autoFlipTotalPages)
            {
                StopAutoFlip();
                BtnAutoFlip.IsChecked = false;
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_AutoFlipComplete"), Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                ShowSelectionBorder();
                return;
            }

            SimulateClickRightSide();
        }

        private void StopAutoFlip()
        {
            _isAutoFlipping = false;
            if (_autoFlipTimer != null)
            {
                _autoFlipTimer.Stop();
                _autoFlipTimer.Tick -= AutoFlipTick;
                _autoFlipTimer = null;
            }
        }

        private void SimulateClickRightSide()
        {
            int x, y, width, height;

            if (_selectedHwnd != IntPtr.Zero)
            {
                NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
                if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                    rc = dwmRc;
                x = rc.Left;
                y = rc.Top;
                width = rc.Right - rc.Left;
                height = rc.Bottom - rc.Top;
            }
            else
            {
                x = (int)_selectedRegion.X;
                y = (int)_selectedRegion.Y;
                width = (int)_selectedRegion.Width;
                height = (int)_selectedRegion.Height;
            }

            int clickX = x + width * 3 / 4;
            int clickY = y + height / 2;

            NativeMethods.GetCursorPos(out var originalPos);
            NativeMethods.SetCursorPos(clickX, clickY);
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(10);
            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
            System.Threading.Thread.Sleep(50);
            NativeMethods.SetCursorPos(originalPos.X, originalPos.Y);
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
