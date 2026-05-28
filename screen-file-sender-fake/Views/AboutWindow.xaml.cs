using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Configuration;
using screen_file_transmit;

namespace about
{
    public partial class AboutWindow : Window
    {
        private readonly List<Key> _konamiSequence = new List<Key>
        {
            Key.Up, Key.Up, Key.Down, Key.Down,
            Key.Left, Key.Right, Key.Left, Key.Right,
            Key.B, Key.A, Key.B, Key.A
        };

        private int _konamiIndex;

        public string ProductName { get; set; }
        public string VersionDisplay { get; set; }
        public string CopyrightText { get; set; }
        public string DescriptionText { get; set; }

        public AboutWindow()
        {
            InitializeComponent();

            var config = new AppConfig();
            config.Load();

            var companyName = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["CompanyName"])
                ? " "
                : ConfigurationManager.AppSettings["CompanyName"];

            var productName = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["ProductName"])
                ? " "
                : ConfigurationManager.AppSettings["ProductName"];

            var version = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["Version"])
                ? " "
                : ConfigurationManager.AppSettings["Version"];

            var description = string.IsNullOrWhiteSpace(ConfigurationManager.AppSettings["Description"])
                ? " "
                : ConfigurationManager.AppSettings["Description"];

            ProductName = productName;
            VersionDisplay = $"Version {version}";
            CopyrightText = $"Copyright {companyName} 2026";
            DescriptionText = description;

            DataContext = this;
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == _konamiSequence[_konamiIndex])
            {
                _konamiIndex++;
                if (_konamiIndex == _konamiSequence.Count)
                {
                    _konamiIndex = 0;
                    LaunchSender();
                }
            }
            else
            {
                _konamiIndex = e.Key == _konamiSequence[0] ? 1 : 0;
            }
        }

        private void LaunchSender()
        {
            var window = new MainWindow();
            window.Show();
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void AboutWindow_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            this.DragMove();
        }
    }
}