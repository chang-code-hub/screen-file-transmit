using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;

namespace screen_file_transmit
{
    /// <summary>
    /// MainWindow.xaml 的交互逻辑
    /// </summary>
    public partial class MainWindow : Window
    {
        private MainWindowViewModel viewModel = new MainWindowViewModel();

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = viewModel;

            this.DragOver += MainWindow_DragOver;
            this.Drop += MainWindow_Drop;
            this.Closed += MainWindow_Closed;
        }

        private void MainWindow_Closed(object sender, System.EventArgs e)
        {
            Environment.Exit(0);
        }

        private void FileDataGrid_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            viewModel.SelectedFileItems = FileDataGrid.SelectedItems.Cast<FileItem>().ToList();
        }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            viewModel.Password = PasswordBox.Password;
        }

        private void MainWindow_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }


        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var imageFiles = files.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLower();
                    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
                });
                viewModel.AddFiles(imageFiles);
            }
            e.Handled = true;
        }

        private void Grid_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
                this.DragMove();
        }

        private void PreviewImage_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                var path = viewModel?.SelectedFileItem?.FullPath;
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    System.Diagnostics.Process.Start(path);
                }
            }
        }

        private void ExitButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void Window_Closing(object sender, CancelEventArgs e)
        {
            if (MessageBox.Show(Properties.Resources.ResourceManager.GetString("MsgBox_ConfirmExit"), Properties.Resources.ResourceManager.GetString("MsgBox_Title_Confirm"), MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
            {
                e.Cancel = true;
            }
        }
    }
}
