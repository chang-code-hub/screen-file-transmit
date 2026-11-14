using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        /// <summary>
        /// 处理表头排序。页码按数值排序（WPF 默认按字符串排序会让 10 排在 2 前面）。
        /// 行号是每行固定的标识，不随排序变化。
        /// </summary>
        private void FileDataGrid_Sorting(object sender, DataGridSortingEventArgs e)
        {
            var column = e.Column;

            // 只接管页码列，其余列交给 DataGrid 默认排序
            if (column.SortMemberPath != "CurrentPage")
                return;

            // 自行按数值排序，避免 DataGrid 的字符串排序
            e.Handled = true;

            var direction = column.SortDirection == ListSortDirection.Ascending
                ? ListSortDirection.Descending
                : ListSortDirection.Ascending;

            var view = System.Windows.Data.CollectionViewSource.GetDefaultView(FileDataGrid.Items);
            using (view.DeferRefresh())
            {
                view.SortDescriptions.Clear();
                view.SortDescriptions.Add(new SortDescription("CurrentPage", direction));
            }

            column.SortDirection = direction;
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


        private async void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var imageFiles = files.Where(f =>
                {
                    var ext = Path.GetExtension(f).ToLower();
                    return ext == ".png" || ext == ".jpg" || ext == ".jpeg" || ext == ".bmp";
                });
                await viewModel.AddFilesAsync(imageFiles);
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
                return;
            }

            // 退出时把放大尝试的排序与统计持久化，下次启动继承
            try { viewModel?.SaveUpscaleStats(); } catch { }
        }
    }
}
