using System;
using System.ComponentModel;
using System.Threading;
using System.Windows;

namespace screen_file_transmit
{
    public enum FlipMethod
    {
        LeftRight,
        UpDown,
        PageUpDown
    }

    public partial class AutoFlipConfigDialog : Window, INotifyPropertyChanged
    {
        private readonly IntPtr _targetHwnd;

        public FlipMethod SelectedMethod
        {
            get
            {
                if (RbUpDown.IsChecked == true)
                    return FlipMethod.UpDown;
                if (RbPageUpDown.IsChecked == true)
                    return FlipMethod.PageUpDown;
                return FlipMethod.LeftRight;
            }
        }

        public string TitleText { get; set; } = "自动翻页设置";
        public string InstructionText { get; set; } = "请先在发送端窗口中测试键盘翻页，然后选择要使用的翻页方式：";
        public string MethodLeftRightText { get; set; } = "左右方向键 (→)";
        public string MethodUpDownText { get; set; } = "上下方向键 (↓)";
        public string MethodPageUpDownText { get; set; } = "PageUp / PageDown";
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
            TestButtonText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_TestButton") ?? TestButtonText;
            TestSentText = Properties.Resources.ResourceManager.GetString("AutoFlipConfigDialog_TestSent") ?? TestSentText;
            OnPropertyChanged(nameof(TitleText));
            OnPropertyChanged(nameof(InstructionText));
            OnPropertyChanged(nameof(MethodLeftRightText));
            OnPropertyChanged(nameof(MethodUpDownText));
            OnPropertyChanged(nameof(MethodPageUpDownText));
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
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
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
