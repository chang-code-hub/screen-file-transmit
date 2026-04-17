using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace screen_file_receiver
{
    public partial class RegionSelectOverlay : Window
    {
        private Point _startPoint;
        private bool _isDragging;

        public Rect SelectedRegion { get; private set; }

        public RegionSelectOverlay()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging = true;
            _startPoint = e.GetPosition(this);
            SelectionRect.Visibility = Visibility.Visible;
            SelectionRect.Width = 0;
            SelectionRect.Height = 0;
            Canvas.SetLeft(SelectionRect, _startPoint.X);
            Canvas.SetTop(SelectionRect, _startPoint.Y);
            CaptureMouse();
        }

        private void Window_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging)
                return;

            var current = e.GetPosition(this);
            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double w = Math.Abs(current.X - _startPoint.X);
            double h = Math.Abs(current.Y - _startPoint.Y);

            Canvas.SetLeft(SelectionRect, x);
            Canvas.SetTop(SelectionRect, y);
            SelectionRect.Width = w;
            SelectionRect.Height = h;
        }

        private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isDragging)
                return;

            _isDragging = false;
            ReleaseMouseCapture();

            var current = e.GetPosition(this);
            double x = Math.Min(_startPoint.X, current.X);
            double y = Math.Min(_startPoint.Y, current.Y);
            double w = Math.Abs(current.X - _startPoint.X);
            double h = Math.Abs(current.Y - _startPoint.Y);

            SelectedRegion = new Rect(Left + x, Top + y, w, h);
            Close();
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                _isDragging = false;
                ReleaseMouseCapture();
                SelectedRegion = Rect.Empty;
                Close();
                e.Handled = true;
            }
        }
    }
}
