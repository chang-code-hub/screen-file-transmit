using System;
using System.ComponentModel;
using System.Drawing;
using System.Threading;
using System.Windows;
using System.Windows.Input;

namespace screen_file_transmit
{
    public enum FlipMethod
    {
        LeftRight,
        UpDown,
        PageUpDown,
        Wait,
        MouseClick
    }

    public partial class AutoFlipConfigDialog : Window, INotifyPropertyChanged
    {
        private readonly IntPtr _targetHwnd;
        private NativeMethods.RECT _regionRect;
        private NativeMethods.RECT _pickRect;
        private PickPointOverlay _pickOverlay;

        // 选区控件的 DIP 坐标（Left/Top/Width/Height），蒙版直接用它显示
        private bool _hasMaskDip;
        private double _maskDipLeft, _maskDipTop, _maskDipWidth, _maskDipHeight;

        public FlipMethod SelectedMethod
        {
            get
            {
                if (RbMouseClick.IsChecked == true)
                    return FlipMethod.MouseClick;
                if (RbUpDown.IsChecked == true)
                    return FlipMethod.UpDown;
                if (RbPageUpDown.IsChecked == true)
                    return FlipMethod.PageUpDown;
                if (RbWait.IsChecked == true)
                    return FlipMethod.Wait;
                return FlipMethod.LeftRight;
            }
        }

        /// <summary>是否使用模拟鼠标点击翻页</summary>
        public bool UseMouseClick => SelectedMethod == FlipMethod.MouseClick;

        /// <summary>
        /// 百分比变化回调（拾取或条码计算后触发），
        /// 让调用方（ScreenshotToolWindow）实时更新选区框上的点击位置高亮。
        /// </summary>
        public Action<double, double> OnPercentChanged { get; set; }

        /// <summary>点击位置横向百分比（0-100）</summary>
        public double ClickOffsetPercentX { get; set; } = 99;

        /// <summary>点击位置纵向百分比（0-100）</summary>
        public double ClickOffsetPercentY { get; set; } = 15;

        // 从截图条码算出的默认百分比（模拟鼠标方案用）
        private bool _hasAutoPercent;
        private double _autoPercentX, _autoPercentY;

        // 用户手动拾取过的百分比（优先于条码计算值）
        private bool _hasPickedPercent;
        private double _pickedPercentX, _pickedPercentY;

        public string TitleText { get; set; } = "自动翻页设置";
        public string InstructionText { get; set; } = "请先在发送端窗口中测试键盘翻页，然后选择要使用的翻页方式：";
        public string MethodLeftRightText { get; set; } = "左右方向键 (→)";
        public string MethodUpDownText { get; set; } = "上下方向键 (↓)";
        public string MethodPageUpDownText { get; set; } = "PageUp / PageDown";
        public string MethodWaitText { get; set; } = "等待自动翻页";
        public string MethodMouseClickText { get; set; } = "模拟鼠标点击";
        public string ClickPosText { get; set; } = "点击位置";
        public string PickPointButtonText { get; set; } = "拾取点击位置";
        public string PickPointHintText { get; set; } = "点击「拾取点击位置」后，在发送端窗口中点击一次，记录翻页点击的落点。";
        public string TestButtonText { get; set; } = "测试";
        public string TestSentText { get; set; } = "已发送测试按键";

        public AutoFlipConfigDialog(IntPtr targetHwnd)
        {
            _targetHwnd = targetHwnd;
            InitializeComponent();
            DataContext = this;
            LoadLocalizedStrings();
        }

        private void LoadLocalizedStrings()
        {
            TitleText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_Title") ?? TitleText;
            InstructionText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_Instruction") ?? InstructionText;
            MethodLeftRightText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_MethodLeftRight") ?? MethodLeftRightText;
            MethodUpDownText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_MethodUpDown") ?? MethodUpDownText;
            MethodPageUpDownText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_MethodPageUpDown") ?? MethodPageUpDownText;
            MethodWaitText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_MethodWait") ?? MethodWaitText;
            MethodMouseClickText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_MethodMouseClick") ?? MethodMouseClickText;
            ClickPosText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_ClickPos") ?? ClickPosText;
            PickPointButtonText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_PickPoint") ?? PickPointButtonText;
            PickPointHintText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_PickPointHint") ?? PickPointHintText;
            TestButtonText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_TestButton") ?? TestButtonText;
            TestSentText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_TestSent") ?? TestSentText;
            OnPropertyChanged(nameof(TitleText));
            OnPropertyChanged(nameof(InstructionText));
            OnPropertyChanged(nameof(MethodLeftRightText));
            OnPropertyChanged(nameof(MethodUpDownText));
            OnPropertyChanged(nameof(MethodPageUpDownText));
            OnPropertyChanged(nameof(MethodWaitText));
            OnPropertyChanged(nameof(MethodMouseClickText));
            OnPropertyChanged(nameof(ClickPosText));
            OnPropertyChanged(nameof(PickPointButtonText));
            OnPropertyChanged(nameof(PickPointHintText));
            OnPropertyChanged(nameof(TestButtonText));
        }
//
//        private void BtnTest_Click(object sender, RoutedEventArgs e)
//        {
//            SimulateKeyPress(SelectedMethod);
//            TestResultText.Text = TestSentText;
//            TestResultText.Visibility = Visibility.Visible;
//        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(true);
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            CloseWithResult(false);
        }

        /// <summary>
        /// DialogResult 只能在 ShowDialog 期间设置；否则抛 InvalidOperationException。
        /// 这里按模态状态分别处理，避免任何情况下崩溃。
        /// </summary>
        private void CloseWithResult(bool result)
        {
            if (System.Windows.Interop.ComponentDispatcher.IsThreadModal)
                DialogResult = result;
            else
                Close();
        }

        private void BtnPickPoint_Click(object sender, RoutedEventArgs e)
        {
            var rect = GetTargetRect();
            if (rect == null)
            {
                MessageBox.Show(
                    Properties.Resources.ResourceManager.GetString("Error_SelectWindowOrRegion"),
                    TitleText, MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            _pickRect = rect.Value;

            // 只在选区/目标窗口范围内覆盖一层蒙版并高亮，把拾取的这次点击挡在蒙版内，
            // 避免传递到该区域下方的界面；其余屏幕不遮挡。只把这一次点击作为拾取结果。
            // 蒙版优先用选区控件的 DIP 坐标直接显示，保证与选区同大小同坐标。
            double mL, mT, mW, mH;
            if (_hasMaskDip)
            {
                mL = _maskDipLeft; mT = _maskDipTop; mW = _maskDipWidth; mH = _maskDipHeight;
            }
            else
            {
                mL = rect.Value.Left; mT = rect.Value.Top;
                mW = rect.Value.Right - rect.Value.Left;
                mH = rect.Value.Bottom - rect.Value.Top;
            }

            _pickOverlay = new PickPointOverlay(mL, mT, mW, mH, _hasMaskDip,
                OnPickPoint, OnPickCancelled)
            {
                Owner = this
            };
            _pickOverlay.Show();
        }

        /// <summary>
        /// 蒙版内的那一次点击作为拾取结果，用其物理坐标反算百分比。
        /// </summary>
        private void OnPickPoint(int screenX, int screenY)
        {
            _pickOverlay = null;
            ApplyPickedPoint(screenX, screenY);
            Activate();
        }

        /// <summary>
        /// 蒙版内按 Esc 取消拾取，保留原值。
        /// </summary>
        private void OnPickCancelled()
        {
            _pickOverlay = null;
            Activate();
        }

        /// <summary>
        /// 用拾取到的屏幕坐标反算在选区内的百分比位置并提交
        /// </summary>
        private void ApplyPickedPoint(int screenX, int screenY)
        {
            PreviewPickedPoint(screenX, screenY);
        }

        /// <summary>
        /// 把百分比写入显示并回调给调用方刷新选区框高亮。
        /// </summary>
        private void ApplyClickPercent(double px, double py)
        {
            ClickOffsetPercentX = Math.Min(100, Math.Max(0, px));
            ClickOffsetPercentY = Math.Min(100, Math.Max(0, py));
            TxtClickPos.Text = $"{ClickOffsetPercentX:F1}% , {ClickOffsetPercentY:F1}%";
            OnPercentChanged?.Invoke(ClickOffsetPercentX, ClickOffsetPercentY);
        }

        /// <summary>
        /// 选中「模拟鼠标点击」时，应用该方案的默认点击百分比：
        /// 优先用户手动拾取值，否则用条码计算值，再否则保持 99/15。
        /// 默认选项即模拟鼠标，所以对话框打开后需立即调用一次。
        /// </summary>
        private void ApplyMouseClickPercent()
        {
            if (_hasPickedPercent)
                ApplyClickPercent(_pickedPercentX, _pickedPercentY);
            else if (_hasAutoPercent)
                ApplyClickPercent(_autoPercentX, _autoPercentY);
        }

        private void MethodRadio_CheckedChanged(object sender, RoutedEventArgs e)
        {
            // 只有选中模拟鼠标时才显示/应用点击位置百分比
            if (SelectedMethod == FlipMethod.MouseClick)
                ApplyMouseClickPercent();
        }

        /// <summary>
        /// 预览拾取位置：把拾取坐标换算成选区百分比，记录并应用。
        /// 百分比与蒙版同源：都基于选区控件的 DIP 矩形，拾取坐标(物理)先换算成 DIP 再算，
        /// 保证 Windows 缩放下分子/分母/蒙版三者同一基准，不偏移。
        /// </summary>
        private void PreviewPickedPoint(int screenX, int screenY)
        {
            // 优先用选区控件 DIP 矩形（与蒙版显示同源）
            double l, t, w, h;
            if (_hasMaskDip)
            {
                l = _maskDipLeft; t = _maskDipTop; w = _maskDipWidth; h = _maskDipHeight;
            }
            else
            {
                // 回退物理矩形
                var r = _pickRect;
                l = r.Left; t = r.Top; w = r.Right - r.Left; h = r.Bottom - r.Top;
            }

            if (w <= 0 || h <= 0) return;

            // 拾取坐标是物理像素，换算成 DIP 以匹配选区 DIP 矩形
            double scale = GetDpiScaleFactor();
            double cx = screenX / scale;
            double cy = screenY / scale;

            double px = (cx - l) * 100.0 / w;
            double py = (cy - t) * 100.0 / h;

            // 记住用户手动拾取值，切方案再切回来时优先使用
            _hasPickedPercent = true;
            _pickedPercentX = Math.Min(100, Math.Max(0, px));
            _pickedPercentY = Math.Min(100, Math.Max(0, py));

            ApplyClickPercent(_pickedPercentX, _pickedPercentY);
        }

        /// <summary>
        /// 目标窗口/选区的真实物理像素矩形。
        /// 与拾取光标(GetPhysicalCursorPos)、蒙版(SetWindowPos)、区域选区(LogicalToPhysical) 同基准。
        /// 注意 GetWindowRect 在 DPI-unaware 进程返回虚拟化坐标，须按缩放换算到物理；
        /// TryGetExtendedFrameBounds 本身就是物理像素，优先用它。
        /// </summary>
        private NativeMethods.RECT? GetTargetRect()
        {
            if (_targetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_targetHwnd))
            {
                if (NativeMethods.TryGetExtendedFrameBounds(_targetHwnd, out var dwmRc))
                    return dwmRc;

                NativeMethods.GetWindowRect(_targetHwnd, out var rc);
                return VirtualToPhysicalRect(rc);
            }

            return _regionRect;
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
        /// 取 DIP→物理 缩放比。以 WPF TransformToDevice 为准，与 LogicalToPhysical 同源。
        /// </summary>
        private double GetDpiScaleFactor()
        {
            try
            {
                var source = PresentationSource.FromVisual(this);
                var m = source?.CompositionTarget?.TransformToDevice ?? System.Windows.Media.Matrix.Identity;
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
        /// 目标窗口句柄之外的区域选区（物理坐标），由调用方设置
        /// </summary>
        public void SetRegionRect(int left, int top, int right, int bottom)
        {
            _regionRect = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        /// <summary>
        /// 设置蒙版显示矩形（WPF DIP，直接来自选区控件的 Left/Top/Width/Height），
        /// 蒙版用它原样显示，不重新计算，保证与选区同大小、同坐标。
        /// </summary>
        public void SetMaskRectDip(double left, double top, double width, double height)
        {
            _hasMaskDip = true;
            _maskDipLeft = left;
            _maskDipTop = top;
            _maskDipWidth = width;
            _maskDipHeight = height;
        }

        /// <summary>
        /// 设置选区截图（截图像素），用左右条码框定内容区域后取 99%/15% 处的点，
        /// 作为「模拟鼠标点击」方案的默认百分比。
        /// 若当前已选中模拟鼠标（默认选项即是），立即应用。
        /// </summary>
        public void SetScreenshot(Bitmap screenshot)
        {
            if (screenshot == null) return;

            using (var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(screenshot))
            {
                if (ImageDecoder.TryDetectClickPercent(mat, out double px, out double py))
                {
                    _hasAutoPercent = true;
                    _autoPercentX = px;
                    _autoPercentY = py;

                    // 当前选中模拟鼠标（默认即是）则立即应用
                    if (SelectedMethod == FlipMethod.MouseClick)
                        ApplyMouseClickPercent();
                }
            }
        }

        protected override void OnClosed(EventArgs e)
        {
            // 对话框关闭时确保拾取蒙版一并关闭
            _pickOverlay?.Close();
            _pickOverlay = null;

            base.OnClosed(e);
        }

        private static void SimulateKeyPress(FlipMethod method)
        {
            byte vk;
            switch (method)
            {
                case FlipMethod.UpDown:
                    vk = NativeMethods.VK_DOWN;
                    break;
                case FlipMethod.PageUpDown:
                    vk = NativeMethods.VK_NEXT;
                    break;
                case FlipMethod.Wait:
                    return;
                default:
                    vk = NativeMethods.VK_RIGHT;
                    break;
            }

            NativeMethods.keybd_event(vk, 0, 0, IntPtr.Zero);
            Thread.Sleep(50);
            NativeMethods.keybd_event(vk, 0, NativeMethods.KEYEVENTF_KEYUP, IntPtr.Zero);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
