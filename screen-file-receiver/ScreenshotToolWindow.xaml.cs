using System;
using System.Drawing;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace screen_file_receiver
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
                //Close();
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
                //case Key.F6:
                //    if (Keyboard.Modifiers != ModifierKeys.Control)
                //        return;
                //    CancelSelection();
                //    e.Handled = true;
                //    break;
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
            //return;
            if (_isCollapsed) return;
            _isCollapsed = true;
            //_expandedHeight = ActualHeight;
            //_expandedWidth = ActualWidth;

            // Hide content and shadow, remove margin and corner radius for a tight 2px bar
            ((Grid)(RootBorder.Child)).Height = 2;
            //RootBorder.Margin = new Thickness(0);
            //RootBorder.CornerRadius = new CornerRadius(0);
            //RootBorder.BorderThickness = new Thickness(0, 0, 0, 2);
            //RootBorder.BorderBrush = System.Windows.Media.Brushes.DodgerBlue;
            //RootBorder.Effect = null;

            //Width = _expandedWidth;
            //Height = _collapsedHeight;
            //Top = 0;
        }

        private void ExpandToolbar()
        {
            if (!_isCollapsed) return;
            _isCollapsed = false;

            // Restore normal appearance
            //RootBorder.Child.Visibility = System.Windows.Visibility.Visible;
            ((Grid)(RootBorder.Child)).Height = double.NaN;
            //RootBorder.Margin = new Thickness(6);
            //RootBorder.CornerRadius = new CornerRadius(4);
            //RootBorder.BorderThickness = new Thickness(1);
            //RootBorder.BorderBrush = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xCC, 0xCC, 0xCC));
            //RootBorder.Effect = new System.Windows.Media.Effects.DropShadowEffect
            //{
            //    ShadowDepth = 0,
            //    BlurRadius = 8,
            //    Opacity = 0.25
            //};

            //Height = _expandedHeight;
            // Keep width consistent with pre-collapse size
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

        private void CancelSelection()
        {
            _selectedHwnd = IntPtr.Zero;
            _selectedRegion = Rect.Empty;
            StopFollowTimer();
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
        }

        private void StartWindowSelection()
        {
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            var overlay = new WindowSelectOverlay();
            overlay.Closed += (s, args) =>
            {
                if (overlay.SelectedHwnd != IntPtr.Zero)
                {
                    _selectedHwnd = overlay.SelectedHwnd;
                    _selectedRegion = Rect.Empty;
                    StartFollowTimer();
                }
                Activate();
                Focus();
            };
            overlay.Show();
        }

        private void StartRegionSelection()
        {
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
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
                Activate();
                Focus();
            };
            overlay.Show();
        }

        private void ShowSelectionBorder(Rect region)
        {
            EnsureSelectionBorderWindow();
            _selectionBorderWindow.Left = region.X;
            _selectionBorderWindow.Top = region.Y;
            _selectionBorderWindow.Width = region.Width;
            _selectionBorderWindow.Height = region.Height;
            _selectionBorderWindow.Show();
        }

        private void EnsureSelectionBorderWindow()
        {
            if (_selectionBorderWindow == null)
            {
                _selectionBorderWindow = new SelectionBorderWindow();
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
                _followTimer.Tick += (s, e) => UpdateSelectionBorderToHwnd();
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

            NativeMethods.GetWindowRect(_selectedHwnd, out var rc);
            if (NativeMethods.TryGetExtendedFrameBounds(_selectedHwnd, out var dwmRc))
                rc = dwmRc;
            if (rc.Right <= rc.Left || rc.Bottom <= rc.Top)
                return;

            const int borderThickness = 2;
            int left = rc.Left - borderThickness;
            int top = rc.Top - borderThickness;
            int width = (rc.Right - rc.Left) + borderThickness * 2;
            int height = (rc.Bottom - rc.Top) + borderThickness * 2;

            var logicalTopLeft = ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(left, top));
            var logicalSize = ScreenCaptureHelper.PhysicalToLogical(this, new System.Windows.Point(width, height));

            EnsureSelectionBorderWindow();
            _selectionBorderWindow.Left = logicalTopLeft.X;
            _selectionBorderWindow.Top = logicalTopLeft.Y;
            _selectionBorderWindow.Width = logicalSize.X;
            _selectionBorderWindow.Height = logicalSize.Y;
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

        private void ExecuteCapture()
        {
            HideSelectionBorder();
            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show("请先设置有效的保存路径", "截图工具", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Bitmap bmp = null;
            if (_selectedHwnd != IntPtr.Zero)
            {
                bmp = ScreenCaptureHelper.CaptureWindow(_selectedHwnd);
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
                MessageBox.Show("请先选择窗口或区域", "截图工具", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (bmp == null)
            {
                MessageBox.Show("截图失败", "截图工具", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _lastCapture?.Dispose();
            _lastCapture = bmp;
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
                fileName = $"Screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png";
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
                ToastNotification.Show($"已保存到: {fullPath}", "截图成功", MessageBoxImage.Information);
                _mainVm?.AddFiles(new[] { fullPath });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败: {ex.Message}", "截图工具", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteDecodeTest()
        {
            if (_lastCapture == null)
            {
                MessageBox.Show("请先截图", "解码测试", MessageBoxButton.OK,  MessageBoxImage.Information);
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
                            ? $"元数据OK: {meta.FileName} ({meta.MaxRows}x{meta.MaxCols} P={meta.CurrentPage}/{meta.TotalPages})"
                            : "未识别到元数据";
                    }
                }
                catch (Exception ex)
                {
                    metaInfo = $"元数据异常: {ex.Message}";
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
                        ? $"解码成功，数据块数: {decodeResult.DataBlocks.Count}"
                        : "解码失败，未解析到数据块";
                }
                catch (Exception ex)
                {
                    decodeInfo = $"解码异常: {ex.Message}";
                }
                finally
                {
                    try { File.Delete(tempPath); } catch { }
                }

                MessageBox.Show($"{metaInfo}\n{decodeInfo}", "解码测试结果", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"测试失败: {ex.Message}", "解码测试", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            _expandDelayTimer?.Stop();
            _collapseDelayTimer?.Stop();
            _lastCapture?.Dispose();
            _lastCapture = null;
            _selectionBorderWindow?.Close();
            _selectionBorderWindow = null;
            base.OnClosed(e);
        }
    }
}
