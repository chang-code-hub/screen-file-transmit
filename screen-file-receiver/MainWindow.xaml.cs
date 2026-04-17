using System.IO;
using System.Linq;
using System.Windows;

namespace screen_file_receiver
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
    }
}