using System;
using System.ComponentModel;
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
        private bool _picking;

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

        /// <summary>点击位置横向百分比（0-100）</summary>
        public int ClickOffsetPercentX { get; set; } = 95;

        /// <summary>点击位置纵向百分比（0-100）</summary>
        public int ClickOffsetPercentY { get; set; } = 50;

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
            _picking = true;

            // 不隐藏对话框：保持 ShowDialog 的模态状态，改为捕获鼠标，
            // 用户在发送端画面上点击一次即记录落点（对话框始终可见）。
            Mouse.Capture(this);
        }

        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseDown(e);
            if (!_picking) return;

            // Preview 阶段吞掉按下事件，避免冒泡到子控件（OK/Cancel 等）。
            // 这里不结束拾取——抬起才算一次完整点击。
            e.Handled = true;
        }

        protected override void OnPreviewMouseUp(MouseButtonEventArgs e)
        {
            base.OnPreviewMouseUp(e);
            if (!_picking) return;

            // 抬起时完成拾取并记录落点，同时吞掉抬起事件，避免其冒泡触发按钮 Click
            EndPick(true);
            e.Handled = true;
        }

        protected override void OnPreviewMouseMove(MouseEventArgs e)
        {
            base.OnPreviewMouseMove(e);
            if (!_picking) return;

            // 捕获期间鼠标可能移出窗口，移动时跟随更新预览位置
            if (NativeMethods.GetCursorPos(out var p))
                PreviewPickedPoint(p.X, p.Y);

            e.Handled = true;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!_picking) return;
            if (e.Key == Key.Escape)
            {
                EndPick(false);
                e.Handled = true;
            }
        }

        /// <summary>
        /// 结束鼠标拾取。成功则用当前光标位置计算百分比，取消则保留原值。
        /// </summary>
        private void EndPick(bool apply)
        {
            if (!_picking) return;
            _picking = false;

            var h = Mouse.Captured;
            if (h != null) Mouse.Capture(null);

            if (apply && NativeMethods.GetCursorPos(out var pt))
                ApplyPickedPoint(pt.X, pt.Y);

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
        /// 预览拾取位置（不改变已确认的百分比，仅更新显示）
        /// </summary>
        private void PreviewPickedPoint(int screenX, int screenY)
        {
            var r = _pickRect;
            int w = r.Right - r.Left;
            int h = r.Bottom - r.Top;
            if (w <= 0 || h <= 0) return;

            double px = (screenX - r.Left) * 100.0 / w;
            double py = (screenY - r.Top) * 100.0 / h;

            ClickOffsetPercentX = (int)Math.Round(Math.Min(100, Math.Max(0, px)));
            ClickOffsetPercentY = (int)Math.Round(Math.Min(100, Math.Max(0, py)));

            TxtClickPos.Text = $"{ClickOffsetPercentX}% , {ClickOffsetPercentY}%";
        }

        /// <summary>
        /// 目标窗口/选区的物理屏幕坐标矩形
        /// </summary>
        private NativeMethods.RECT? GetTargetRect()
        {
            if (_targetHwnd != IntPtr.Zero && NativeMethods.IsWindow(_targetHwnd))
            {
                NativeMethods.GetWindowRect(_targetHwnd, out var rc);
                if (NativeMethods.TryGetExtendedFrameBounds(_targetHwnd, out var dwmRc))
                    rc = dwmRc;
                return rc;
            }

            return _regionRect;
        }

        /// <summary>
        /// 目标窗口句柄之外的区域选区（物理坐标），由调用方设置
        /// </summary>
        public void SetRegionRect(int left, int top, int right, int bottom)
        {
            _regionRect = new NativeMethods.RECT { Left = left, Top = top, Right = right, Bottom = bottom };
        }

        protected override void OnClosed(EventArgs e)
        {
            _picking = false;
            if (Mouse.Captured != null)
                Mouse.Capture(null);

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
