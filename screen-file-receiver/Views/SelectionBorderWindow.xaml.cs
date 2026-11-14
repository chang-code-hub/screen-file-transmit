using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;

namespace screen_file_transmit
{
    public class RectChangedEventArgs : EventArgs
    {
        public Rect NewRect { get; }
        public RectChangedEventArgs(Rect newRect) => NewRect = newRect;
    }

    public partial class SelectionBorderWindow : Window
    {
        public event EventHandler<RectChangedEventArgs> Resized;

        private Rectangle _handleTL, _handleT, _handleTR;
        private Rectangle _handleL, _handleR;
        private Rectangle _handleBL, _handleB, _handleBR;

        // 模拟点击位置标记（百分比，0-100）
        private bool _showClickMarker;
        private double _clickPercentX, _clickPercentY;

        private bool _isResizing;
        private ResizeEdge _resizeEdge;
        private System.Windows.Point _resizeStartScreenLogical;
        private double _resizeStartLeft, _resizeStartTop, _resizeStartWidth, _resizeStartHeight;

        private bool _editMode;
        public bool EditMode
        {
            get => _editMode;
            set
            {
                _editMode = value;
                UpdateHandlesVisibility();
                if (!_editMode && _isResizing)
                {
                    _isResizing = false;
                    ReleaseMouseCapture();
                }
            }
        }

        public bool IsResizing => _isResizing;

        private const double HandleSize = 8;
        private const double EdgeDetectThickness = 6;

        /// <summary>选区边框线宽（物理像素）。截图后按此宽度裁掉红框。</summary>
        public const int BorderThicknessPx = 2;

        /// <summary>深绿：上一次识别生成成功。</summary>
        private static readonly Brush GeneratedBrush = new SolidColorBrush(Colors.DarkGreen);

        /// <summary>黄色：自动翻页开启中。</summary>
        private static readonly Brush AutoFlipBrush = new SolidColorBrush(Colors.Yellow);

        /// <summary>
        /// 深绿常亮：上一次识别生成成功。
        /// 圆点是「状态」不是「闪烁」—— 不设定时器，改一次颜色即定住。
        /// </summary>
        public void ShowGeneratedSteady()
        {
            BlinkDot.Fill = GeneratedBrush;
            BlinkDot.Opacity = 1;
            BlinkDot.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 黄色常亮：自动翻页开启中。与深绿互斥，后调者覆盖前一个。
        /// </summary>
        public void ShowAutoFlipSteady()
        {
            BlinkDot.Fill = AutoFlipBrush;
            BlinkDot.Opacity = 1;
            BlinkDot.Visibility = Visibility.Visible;
        }

        /// <summary>隐藏圆点（自动翻页结束、无生成结果时清理）。</summary>
        public void HideDot()
        {
            BlinkDot.Opacity = 1;
            BlinkDot.Visibility = Visibility.Collapsed;
        }

        private enum ResizeEdge
        {
            None,
            TopLeft, Top, TopRight,
            Left, Right,
            BottomLeft, Bottom, BottomRight
        }

        public SelectionBorderWindow()
        {
            InitializeComponent();
            CreateResizeHandles();
            MouseMove += OnMouseMove;
            MouseLeftButtonDown += OnMouseLeftButtonDown;
            MouseLeftButtonUp += OnMouseLeftButtonUp;
            SizeChanged += OnSizeChanged;
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

            // 置顶
            NativeMethods.SetWindowPos(hWnd, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOACTIVATE);
        }
        private void CreateResizeHandles()
        {
            var handleFill = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255));
            var handleStroke = new SolidColorBrush(Colors.Red);

            _handleTL = CreateHandle(handleFill, handleStroke, Cursors.SizeNWSE);
            _handleT  = CreateHandle(handleFill, handleStroke, Cursors.SizeNS);
            _handleTR = CreateHandle(handleFill, handleStroke, Cursors.SizeNESW);
            _handleL  = CreateHandle(handleFill, handleStroke, Cursors.SizeWE);
            _handleR  = CreateHandle(handleFill, handleStroke, Cursors.SizeWE);
            _handleBL = CreateHandle(handleFill, handleStroke, Cursors.SizeNESW);
            _handleB  = CreateHandle(handleFill, handleStroke, Cursors.SizeNS);
            _handleBR = CreateHandle(handleFill, handleStroke, Cursors.SizeNWSE);

            HandleCanvas.Children.Add(_handleTL);
            HandleCanvas.Children.Add(_handleT);
            HandleCanvas.Children.Add(_handleTR);
            HandleCanvas.Children.Add(_handleL);
            HandleCanvas.Children.Add(_handleR);
            HandleCanvas.Children.Add(_handleBL);
            HandleCanvas.Children.Add(_handleB);
            HandleCanvas.Children.Add(_handleBR);
            UpdateClickMarkerPosition();

            UpdateHandlesVisibility();
        }

        private Rectangle CreateHandle(Brush fill, Brush stroke, Cursor cursor)
        {
            return new Rectangle
            {
                Width = HandleSize,
                Height = HandleSize,
                Fill = fill,
                Stroke = stroke,
                StrokeThickness = 1,
                Cursor = cursor,
                Visibility = Visibility.Collapsed,
                IsHitTestVisible = true
            };
        }

        private void UpdateHandlesVisibility()
        {
            var vis = _editMode ? Visibility.Visible : Visibility.Collapsed;
            _handleTL.Visibility = vis;
            _handleT.Visibility = vis;
            _handleTR.Visibility = vis;
            _handleL.Visibility = vis;
            _handleR.Visibility = vis;
            _handleBL.Visibility = vis;
            _handleB.Visibility = vis;
            _handleBR.Visibility = vis;
        }

        private void OnSizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateHandlePositions();
            UpdateClickMarkerPosition();
        }

        private void UpdateHandlePositions()
        {
            UpdateHandlePositions(HandleCanvas.ActualWidth, HandleCanvas.ActualHeight);
        }

        private void UpdateHandlePositions(double w, double h)
        {
            if (w <= 0 || h <= 0) return;

            double half = HandleSize / 2;

            Canvas.SetLeft(_handleTL, -half);
            Canvas.SetTop(_handleTL, -half);

            Canvas.SetLeft(_handleT, (w - HandleSize) / 2);
            Canvas.SetTop(_handleT, -half);

            Canvas.SetLeft(_handleTR, w - half);
            Canvas.SetTop(_handleTR, -half);

            Canvas.SetLeft(_handleL, -half);
            Canvas.SetTop(_handleL, (h - HandleSize) / 2);

            Canvas.SetLeft(_handleR, w - half);
            Canvas.SetTop(_handleR, (h - HandleSize) / 2);

            Canvas.SetLeft(_handleBL, -half);
            Canvas.SetTop(_handleBL, h - half);

            Canvas.SetLeft(_handleB, (w - HandleSize) / 2);
            Canvas.SetTop(_handleB, h - half);

            Canvas.SetLeft(_handleBR, w - half);
            Canvas.SetTop(_handleBR, h - half);
        }

        /// <summary>
        /// 显示/隐藏模拟鼠标点击位置标记。
        /// </summary>
        /// <param name="show">是否显示</param>
        /// <param name="percentX">横向百分比（0-100）</param>
        /// <param name="percentY">纵向百分比（0-100）</param>
        public void SetClickMarker(bool show, double percentX, double percentY)
        {
            _showClickMarker = show;
            _clickPercentX = percentX;
            _clickPercentY = percentY;
            UpdateClickMarkerPosition();
        }

        /// <summary>
        /// 把标记摆到选区内百分比对应位置：绿色圆点 + 十字准星。
        /// </summary>
        private void UpdateClickMarkerPosition()
        {
            if (!_showClickMarker)
            {
                ClickMarkerDot.Visibility = Visibility.Collapsed;
                ClickMarkerHLine.Visibility = Visibility.Collapsed;
                ClickMarkerVLine.Visibility = Visibility.Collapsed;
                return;
            }

            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return;

            double px = Math.Min(100, Math.Max(0, _clickPercentX)) / 100.0;
            double py = Math.Min(100, Math.Max(0, _clickPercentY)) / 100.0;
            double cx = w * px;
            double cy = h * py;

            const double dotSize = 16;
            const double crossLen = 24;

            ClickMarkerDot.Visibility = Visibility.Visible;
            ClickMarkerHLine.Visibility = Visibility.Visible;
            ClickMarkerVLine.Visibility = Visibility.Visible;

            Canvas.SetLeft(ClickMarkerDot, cx - dotSize / 2);
            Canvas.SetTop(ClickMarkerDot, cy - dotSize / 2);

            ClickMarkerHLine.X1 = cx - crossLen / 2;
            ClickMarkerHLine.Y1 = cy;
            ClickMarkerHLine.X2 = cx + crossLen / 2;
            ClickMarkerHLine.Y2 = cy;

            ClickMarkerVLine.X1 = cx;
            ClickMarkerVLine.Y1 = cy - crossLen / 2;
            ClickMarkerVLine.X2 = cx;
            ClickMarkerVLine.Y2 = cy + crossLen / 2;
        }

        private System.Windows.Point ScreenToLogical(System.Windows.Point screenPoint)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformFromDevice;
                return transform.Transform(screenPoint);
            }
            return screenPoint;
        }

        private void SetWindowRect(double left, double top, double width, double height)
        {
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                var transform = source.CompositionTarget.TransformToDevice;
                int px = (int)Math.Round(left * transform.M11);
                int py = (int)Math.Round(top * transform.M22);
                int pw = (int)Math.Round(width * transform.M11);
                int ph = (int)Math.Round(height * transform.M22);

                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.SetWindowPos(hwnd, IntPtr.Zero, px, py, pw, ph,
                        NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
                }
            }
            else
            {
                Left = left;
                Top = top;
                Width = width;
                Height = height;
            }

            // 立即同步更新手柄位置，不等待 SizeChanged
            UpdateHandlePositions(width, height);
        }

        private void OnMouseMove(object sender, MouseEventArgs e)
        {
            var pos = e.GetPosition(this);

            if (_isResizing)
            {
                var screenCurrent = ScreenToLogical(PointToScreen(pos));
                double dx = screenCurrent.X - _resizeStartScreenLogical.X;
                double dy = screenCurrent.Y - _resizeStartScreenLogical.Y;

                double newLeft = _resizeStartLeft;
                double newTop = _resizeStartTop;
                double newWidth = _resizeStartWidth;
                double newHeight = _resizeStartHeight;

                switch (_resizeEdge)
                {
                    case ResizeEdge.Left:
                        newLeft += dx;
                        newWidth -= dx;
                        break;
                    case ResizeEdge.Right:
                        newWidth += dx;
                        break;
                    case ResizeEdge.Top:
                        newTop += dy;
                        newHeight -= dy;
                        break;
                    case ResizeEdge.Bottom:
                        newHeight += dy;
                        break;
                    case ResizeEdge.TopLeft:
                        newLeft += dx;
                        newTop += dy;
                        newWidth -= dx;
                        newHeight -= dy;
                        break;
                    case ResizeEdge.TopRight:
                        newTop += dy;
                        newWidth += dx;
                        newHeight -= dy;
                        break;
                    case ResizeEdge.BottomLeft:
                        newLeft += dx;
                        newWidth -= dx;
                        newHeight += dy;
                        break;
                    case ResizeEdge.BottomRight:
                        newWidth += dx;
                        newHeight += dy;
                        break;
                }

                const double minSize = 4;
                if (newWidth < minSize)
                {
                    newLeft += newWidth - minSize;
                    newWidth = minSize;
                }
                if (newHeight < minSize)
                {
                    newTop += newHeight - minSize;
                    newHeight = minSize;
                }

                SetWindowRect(newLeft, newTop, newWidth, newHeight);
                e.Handled = true;
            }
            else if (_editMode)
            {
                var edge = GetEdgeAtPoint(pos);
                Cursor = GetCursorForEdge(edge);
            }
            else
            {
                Cursor = Cursors.Arrow;
            }
        }

        private void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (!_editMode) return;

            var pos = e.GetPosition(this);
            var edge = GetEdgeAtPoint(pos);
            if (edge != ResizeEdge.None)
            {
                _isResizing = true;
                _resizeEdge = edge;
                _resizeStartScreenLogical = ScreenToLogical(PointToScreen(pos));
                _resizeStartLeft = Left;
                _resizeStartTop = Top;
                _resizeStartWidth = ActualWidth;
                _resizeStartHeight = ActualHeight;
                CaptureMouse();
                e.Handled = true;
            }
        }

        private void OnMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                ReleaseMouseCapture();

                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    NativeMethods.GetWindowRect(hwnd, out var rc);
                    var source = PresentationSource.FromVisual(this);
                    if (source?.CompositionTarget != null)
                    {
                        var transform = source.CompositionTarget.TransformFromDevice;
                        Left = rc.Left * transform.M11;
                        Top = rc.Top * transform.M22;
                        Width = (rc.Right - rc.Left) * transform.M11;
                        Height = (rc.Bottom - rc.Top) * transform.M22;
                    }
                    else
                    {
                        Left = rc.Left;
                        Top = rc.Top;
                        Width = rc.Right - rc.Left;
                        Height = rc.Bottom - rc.Top;
                    }
                }

                Resized?.Invoke(this, new RectChangedEventArgs(new Rect(Left, Top, Width, Height)));
            }
        }

        private ResizeEdge GetEdgeAtPoint(Point p)
        {
            double w = ActualWidth;
            double h = ActualHeight;
            if (w <= 0 || h <= 0) return ResizeEdge.None;

            bool nearLeft = p.X < EdgeDetectThickness;
            bool nearRight = p.X >= w - EdgeDetectThickness;
            bool nearTop = p.Y < EdgeDetectThickness;
            bool nearBottom = p.Y >= h - EdgeDetectThickness;

            if (nearTop && nearLeft) return ResizeEdge.TopLeft;
            if (nearTop && nearRight) return ResizeEdge.TopRight;
            if (nearBottom && nearLeft) return ResizeEdge.BottomLeft;
            if (nearBottom && nearRight) return ResizeEdge.BottomRight;
            if (nearTop) return ResizeEdge.Top;
            if (nearBottom) return ResizeEdge.Bottom;
            if (nearLeft) return ResizeEdge.Left;
            if (nearRight) return ResizeEdge.Right;
            return ResizeEdge.None;
        }

        private Cursor GetCursorForEdge(ResizeEdge edge)
        {
            switch (edge)
            {
                case ResizeEdge.TopLeft: return Cursors.SizeNWSE;
                case ResizeEdge.TopRight: return Cursors.SizeNESW;
                case ResizeEdge.BottomLeft: return Cursors.SizeNESW;
                case ResizeEdge.BottomRight: return Cursors.SizeNWSE;
                case ResizeEdge.Top: return Cursors.SizeNS;
                case ResizeEdge.Bottom: return Cursors.SizeNS;
                case ResizeEdge.Left: return Cursors.SizeWE;
                case ResizeEdge.Right: return Cursors.SizeWE;
                default: return Cursors.Arrow;
            }
        }
    }
}
