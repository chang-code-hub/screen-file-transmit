using Microsoft.Win32;
using OpenCvSharp;
using OpenCvSharp.WpfExtensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Management;
using System.Windows.Threading;

namespace screen_file_transmit
{
    public partial class CameraCaptureWindow : System.Windows.Window
    {
        private readonly MainWindowViewModel _mainVm;
        private readonly AppConfig _appConfig = new AppConfig();
        private VideoCapture _capture;
        private DispatcherTimer _previewTimer;
        private Mat _currentFrame;
        private bool _isCorrectionEnabled;
        private readonly List<CorrectionPoint> _correctionPoints = new List<CorrectionPoint>();
        private readonly List<Line> _correctionLines = new List<Line>();
        private CorrectionPoint _draggingPoint;
        private bool _isDraggingPoint;
        private double _imageActualWidth;
        private double _imageActualHeight;
        private double _imageOffsetX;
        private double _imageOffsetY;
        private string _currentCameraName;

        private class CameraInfo
        {
            public int Index { get; set; }
            public string Name { get; set; }
        }

        private class CorrectionPoint
        {
            public Ellipse Shape { get; set; }
            public System.Windows.Point NormalizedPosition { get; set; }
        }

        public CameraCaptureWindow(MainWindowViewModel mainVm)
        {
            InitializeComponent();
            _mainVm = mainVm;
            Loaded += Window_Loaded;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            _appConfig.Load();
            RefreshCameraList();
            if (CameraComboBox.Items.Count > 0)
                CameraComboBox.SelectedIndex = 0;
        }

        #region Camera Name Resolution

        private static List<string> GetCameraDeviceNames()
        {
            var names = GetDirectShowCameraNames();
            if (names.Count > 0) return names;
            return GetWmiCameraNames();
        }

        private static List<string> GetDirectShowCameraNames()
        {
            var names = new List<string>();
            try
            {
                var clsid = new Guid("860BB310-5D01-11d0-BD3B-00A0C911CE86");
                string keyPath = $"CLSID\\{{{clsid}}}\\Instance";
                using (var key = Registry.ClassesRoot.OpenSubKey(keyPath))
                {
                    if (key == null) return names;
                    foreach (string subKeyName in key.GetSubKeyNames())
                    {
                        using (var subKey = key.OpenSubKey(subKeyName))
                        {
                            string friendlyName = subKey?.GetValue(null) as string;
                            if (!string.IsNullOrEmpty(friendlyName))
                                names.Add(friendlyName);
                        }
                    }
                }
            }
            catch { }
            return names;
        }

        private static List<string> GetWmiCameraNames()
        {
            var names = new List<string>();
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name FROM Win32_PnPEntity WHERE " +
                    "(Service='usbvideo' OR Service='ksthunk') AND " +
                    "ClassGuid='{4d36e96c-e325-11ce-bfc1-08002be10318}'"))
                {
                    foreach (ManagementObject device in searcher.Get())
                    {
                        string name = device["Name"]?.ToString();
                        if (!string.IsNullOrEmpty(name))
                            names.Add(name);
                    }
                }
                if (names.Count == 0)
                {
                    using (var searcher = new ManagementObjectSearcher(
                        "SELECT Name FROM Win32_PnPEntity WHERE " +
                        "ClassGuid='{4d36e96c-e325-11ce-bfc1-08002be10318}'"))
                    {
                        foreach (ManagementObject device in searcher.Get())
                        {
                            string name = device["Name"]?.ToString();
                            if (!string.IsNullOrEmpty(name) &&
                                !name.Contains("Audio") &&
                                !name.Contains("Microphone") &&
                                !name.Contains("Speaker") &&
                                !name.Contains("Sound"))
                                names.Add(name);
                        }
                    }
                }
            }
            catch { }
            return names;
        }

        #endregion

        #region Camera List & Preview

        private void RefreshCameraList()
        {
            var selected = CameraComboBox.SelectedItem as CameraInfo;
            var cameras = new List<CameraInfo>();
            var deviceNames = GetCameraDeviceNames();

            for (int i = 0; i < 10; i++)
            {
                try
                {
                    using (var cap = new VideoCapture(i))
                    {
                        if (cap.IsOpened())
                        {
                            string name = i < deviceNames.Count ? deviceNames[i] : null;
                            cameras.Add(new CameraInfo
                            {
                                Index = i,
                                Name = name ?? $"{Properties.Resources.ResourceManager.GetString("Camera_Device")} {i}"
                            });
                        }
                    }
                }
                catch { }
            }

            CameraComboBox.ItemsSource = cameras;
            if (selected != null)
            {
                var match = cameras.FirstOrDefault(c => c.Index == selected.Index);
                if (match != null)
                    CameraComboBox.SelectedItem = match;
            }
            if (CameraComboBox.SelectedItem == null && cameras.Count > 0)
            {
                CameraComboBox.SelectedItem = cameras[0];
            }

            if (cameras.Count == 0)
            {
                StatusText.Text = Properties.Resources.ResourceManager.GetString("Status_NoCamera");
            }
            else
            {
                StartPreview();
            }
        }

        private static (int width, int height) GetMaxSupportedResolution(VideoCapture cap)
        {
            var candidates = new (int w, int h)[]
            {
                (3840, 2160), (2560, 1440), (1920, 1080),
                (1280, 720),  (640, 480),   (320, 240)
            };

            int bestW = 0, bestH = 0;
            foreach (var (w, h) in candidates)
            {
                cap.Set(VideoCaptureProperties.FrameWidth, w);
                cap.Set(VideoCaptureProperties.FrameHeight, h);
                int actualW = (int)cap.Get(VideoCaptureProperties.FrameWidth);
                int actualH = (int)cap.Get(VideoCaptureProperties.FrameHeight);
                if (actualW >= w * 0.9 && actualH >= h * 0.9)
                {
                    if (actualW * actualH > bestW * bestH)
                    {
                        bestW = actualW;
                        bestH = actualH;
                    }
                }
            }
            if (bestW > 0)
            {
                cap.Set(VideoCaptureProperties.FrameWidth, bestW);
                cap.Set(VideoCaptureProperties.FrameHeight, bestH);
            }
            return (bestW, bestH);
        }

        private void StartPreview()
        {
            StopPreview();

            if (CameraComboBox.SelectedItem is not CameraInfo camera)
                return;

            _currentCameraName = camera.Name;

            try
            {
                _capture = new VideoCapture(camera.Index);
                if (!_capture.IsOpened())
                {
                    StatusText.Text = Properties.Resources.ResourceManager.GetString("Status_CameraOpenFailed");
                    return;
                }

                var (maxW, maxH) = GetMaxSupportedResolution(_capture);
                if (maxW > 0 && maxH > 0)
                {
                    _capture.Set(VideoCaptureProperties.FrameWidth, maxW);
                    _capture.Set(VideoCaptureProperties.FrameHeight, maxH);
                }

                // Load saved correction for this camera
                LoadCorrectionConfig();

                _previewTimer = new DispatcherTimer(DispatcherPriority.Render)
                {
                    Interval = TimeSpan.FromMilliseconds(33)
                };
                _previewTimer.Tick += PreviewTimer_Tick;
                _previewTimer.Start();

                StatusText.Text = string.Format(Properties.Resources.ResourceManager.GetString("Status_CameraPreviewing"), camera.Name);
            }
            catch (Exception ex)
            {
                StatusText.Text = string.Format(Properties.Resources.ResourceManager.GetString("Status_CameraError"), ex.Message);
            }
        }

        private void StopPreview()
        {
            SaveCorrectionConfig();
            _previewTimer?.Stop();
            _previewTimer = null;
            _capture?.Dispose();
            _capture = null;
            _currentFrame?.Dispose();
            _currentFrame = null;
        }

        private void PreviewTimer_Tick(object sender, EventArgs e)
        {
            if (_capture == null || !_capture.IsOpened())
                return;

            var frame = new Mat();
            if (!_capture.Read(frame) || frame.Empty())
            {
                frame.Dispose();
                return;
            }

            _currentFrame?.Dispose();
            _currentFrame = frame;

            Mat displayFrame = frame;
            Mat corrected = null;

            if (_correctionPoints.Count == 4 && HasValidCorrection() && !_isDraggingPoint)
            {
                corrected = WarpCorrect(frame);
                if (corrected != null)
                    displayFrame = corrected;
            }

            var bitmapSource = displayFrame.ToBitmapSource();
            PreviewImage.Source = bitmapSource;

            if (corrected != null && corrected != frame)
                corrected.Dispose();

            UpdateImageLayoutInfo();
            UpdateCorrectionOverlay();
        }

        private bool HasValidCorrection()
        {
            if (_correctionPoints.Count < 4) return false;
            double inset = 0.001;
            foreach (var pt in _correctionPoints)
            {
                if (pt.NormalizedPosition.X < inset && pt.NormalizedPosition.Y < inset) continue;
                if (pt.NormalizedPosition.X > 1 - inset && pt.NormalizedPosition.Y < inset) continue;
                if (pt.NormalizedPosition.X > 1 - inset && pt.NormalizedPosition.Y > 1 - inset) continue;
                if (pt.NormalizedPosition.X < inset && pt.NormalizedPosition.Y > 1 - inset) continue;
                return true;
            }
            return false;
        }

        #endregion

        #region Image Layout & Overlay

        private void UpdateImageLayoutInfo()
        {
            if (PreviewImage.Source == null) return;

            double containerW = PreviewImage.ActualWidth;
            double containerH = PreviewImage.ActualHeight;
            double imgW = PreviewImage.Source.Width;
            double imgH = PreviewImage.Source.Height;

            if (containerW <= 0 || containerH <= 0 || imgW <= 0 || imgH <= 0) return;

            double scale = Math.Min(containerW / imgW, containerH / imgH);
            _imageActualWidth = imgW * scale;
            _imageActualHeight = imgH * scale;
            _imageOffsetX = (containerW - _imageActualWidth) / 2;
            _imageOffsetY = (containerH - _imageActualHeight) / 2;
        }

        private void OverlayCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateImageLayoutInfo();
            UpdateCorrectionOverlay();
        }

        private void InitializeCorrectionPoints()
        {
            if (_correctionPoints.Count > 0) return;

            var saved = GetSavedCorrectionData();
            var defaults = saved ?? GetDefaultCorrectionPoints();

            for (int i = 0; i < 4; i++)
            {
                var ellipse = new Ellipse
                {
                    Width = 16,
                    Height = 16,
                    Fill = new SolidColorBrush(Colors.Red),
                    Stroke = new SolidColorBrush(Colors.White),
                    StrokeThickness = 2,
                    Cursor = Cursors.Hand,
                    Tag = i
                };
                ellipse.MouseLeftButtonDown += Point_MouseLeftButtonDown;
                ellipse.MouseMove += Point_MouseMove;
                ellipse.MouseLeftButtonUp += Point_MouseLeftButtonUp;

                _correctionPoints.Add(new CorrectionPoint
                {
                    Shape = ellipse,
                    NormalizedPosition = defaults[i]
                });

                OverlayCanvas.Children.Add(ellipse);
            }

            for (int i = 0; i < 4; i++)
            {
                var line = new Line
                {
                    Stroke = new SolidColorBrush(Colors.Yellow),
                    StrokeThickness = 2,
                    StrokeDashArray = new DoubleCollection { 4, 2 }
                };
                _correctionLines.Add(line);
                OverlayCanvas.Children.Add(line);
            }
        }

        private List<System.Windows.Point> GetDefaultCorrectionPoints()
        {
            double inset = 0.05;
            return new List<System.Windows.Point>
            {
                new System.Windows.Point(inset, inset),
                new System.Windows.Point(1 - inset, inset),
                new System.Windows.Point(1 - inset, 1 - inset),
                new System.Windows.Point(inset, 1 - inset)
            };
        }

        private List<System.Windows.Point> GetSavedCorrectionData()
        {
            if (string.IsNullOrEmpty(_currentCameraName)) return null;
            if (!_appConfig.CameraCorrections.TryGetValue(_currentCameraName, out var data)) return null;

            return new List<System.Windows.Point>
            {
                new System.Windows.Point(data.X0, data.Y0),
                new System.Windows.Point(data.X1, data.Y1),
                new System.Windows.Point(data.X2, data.Y2),
                new System.Windows.Point(data.X3, data.Y3)
            };
        }

        private void ClearCorrectionPoints()
        {
            foreach (var pt in _correctionPoints)
                OverlayCanvas.Children.Remove(pt.Shape);
            foreach (var line in _correctionLines)
                OverlayCanvas.Children.Remove(line);
            _correctionPoints.Clear();
            _correctionLines.Clear();
        }

        private void UpdateCorrectionOverlay()
        {
            if (!_isCorrectionEnabled || _correctionPoints.Count == 0) return;

            OverlayCanvas.Width = PreviewImage.ActualWidth;
            OverlayCanvas.Height = PreviewImage.ActualHeight;

            bool isPreviewCorrected = HasValidCorrection() && !_isDraggingPoint;

            for (int i = 0; i < _correctionPoints.Count; i++)
            {
                var pt = _correctionPoints[i];
                System.Windows.Point displayPos;

                if (isPreviewCorrected && _currentFrame != null)
                {
                    displayPos = MapOriginalToCorrected(pt.NormalizedPosition);
                }
                else
                {
                    displayPos = pt.NormalizedPosition;
                }

                double x = _imageOffsetX + displayPos.X * _imageActualWidth - pt.Shape.Width / 2;
                double y = _imageOffsetY + displayPos.Y * _imageActualHeight - pt.Shape.Height / 2;
                Canvas.SetLeft(pt.Shape, x);
                Canvas.SetTop(pt.Shape, y);
            }

            for (int i = 0; i < 4; i++)
            {
                var pt1 = _correctionPoints[i];
                var pt2 = _correctionPoints[(i + 1) % 4];
                var line = _correctionLines[i];

                System.Windows.Point dp1 = isPreviewCorrected && _currentFrame != null
                    ? MapOriginalToCorrected(pt1.NormalizedPosition)
                    : pt1.NormalizedPosition;
                System.Windows.Point dp2 = isPreviewCorrected && _currentFrame != null
                    ? MapOriginalToCorrected(pt2.NormalizedPosition)
                    : pt2.NormalizedPosition;

                line.X1 = _imageOffsetX + dp1.X * _imageActualWidth;
                line.Y1 = _imageOffsetY + dp1.Y * _imageActualHeight;
                line.X2 = _imageOffsetX + dp2.X * _imageActualWidth;
                line.Y2 = _imageOffsetY + dp2.Y * _imageActualHeight;
            }
        }

        #endregion

        #region Point Dragging with Inverse Mapping

        private void Point_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is Ellipse ellipse)
            {
                _draggingPoint = _correctionPoints.First(p => p.Shape == ellipse);
                _draggingPoint.Shape.CaptureMouse();
                _isDraggingPoint = true;
                e.Handled = true;
            }
        }

        private void Point_MouseMove(object sender, MouseEventArgs e)
        {
            if (_draggingPoint == null || !_draggingPoint.Shape.IsMouseCaptured) return;

            var pos = e.GetPosition(OverlayCanvas);
            double displayNx = (pos.X - _imageOffsetX) / _imageActualWidth;
            double displayNy = (pos.Y - _imageOffsetY) / _imageActualHeight;
            displayNx = Math.Max(0, Math.Min(1, displayNx));
            displayNy = Math.Max(0, Math.Min(1, displayNy));

            System.Windows.Point originalPos;
            if (HasValidCorrection() && _currentFrame != null && !_isDraggingPoint)
            {
                originalPos = MapCorrectedToOriginal(new System.Windows.Point(displayNx, displayNy));
            }
            else
            {
                originalPos = new System.Windows.Point(displayNx, displayNy);
            }

            originalPos = new System.Windows.Point(
                Math.Max(0, Math.Min(1, originalPos.X)),
                Math.Max(0, Math.Min(1, originalPos.Y)));

            _draggingPoint.NormalizedPosition = originalPos;
            UpdateCorrectionOverlay();
        }

        private void Point_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_draggingPoint != null)
            {
                _draggingPoint.Shape.ReleaseMouseCapture();
                _isDraggingPoint = false;
                _draggingPoint = null;
                SaveCorrectionConfig();
            }
        }

        #endregion

        #region Perspective Transform Mapping

        private Mat WarpCorrect(Mat source)
        {
            var srcPoints = new[]
            {
                new Point2f((float)(_correctionPoints[0].NormalizedPosition.X * source.Width),
                            (float)(_correctionPoints[0].NormalizedPosition.Y * source.Height)),
                new Point2f((float)(_correctionPoints[1].NormalizedPosition.X * source.Width),
                            (float)(_correctionPoints[1].NormalizedPosition.Y * source.Height)),
                new Point2f((float)(_correctionPoints[2].NormalizedPosition.X * source.Width),
                            (float)(_correctionPoints[2].NormalizedPosition.Y * source.Height)),
                new Point2f((float)(_correctionPoints[3].NormalizedPosition.X * source.Width),
                            (float)(_correctionPoints[3].NormalizedPosition.Y * source.Height))
            };

            int dstW = source.Width;
            int dstH = source.Height;
            var dstPoints = new[]
            {
                new Point2f(0, 0),
                new Point2f(dstW, 0),
                new Point2f(dstW, dstH),
                new Point2f(0, dstH)
            };

            using (var matrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints))
            {
                Mat corrected = new Mat();
                Cv2.WarpPerspective(source, corrected, matrix, new OpenCvSharp.Size(dstW, dstH));
                return corrected;
            }
        }

        private System.Windows.Point MapOriginalToCorrected(System.Windows.Point originalNormalized)
        {
            if (_currentFrame == null || _currentFrame.Empty() || _correctionPoints.Count < 4)
                return originalNormalized;

            int w = _currentFrame.Width;
            int h = _currentFrame.Height;

            var srcPoints = new[]
            {
                new Point2f((float)(_correctionPoints[0].NormalizedPosition.X * w),
                            (float)(_correctionPoints[0].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[1].NormalizedPosition.X * w),
                            (float)(_correctionPoints[1].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[2].NormalizedPosition.X * w),
                            (float)(_correctionPoints[2].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[3].NormalizedPosition.X * w),
                            (float)(_correctionPoints[3].NormalizedPosition.Y * h))
            };

            var dstPoints = new[]
            {
                new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h)
            };

            using (var matrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints))
            {
                var orig = new Point2f((float)(originalNormalized.X * w), (float)(originalNormalized.Y * h));
                var result = Cv2.PerspectiveTransform(new[] { orig }, matrix);
                return new System.Windows.Point(result[0].X / w, result[0].Y / h);
            }
        }

        private System.Windows.Point MapCorrectedToOriginal(System.Windows.Point correctedNormalized)
        {
            if (_currentFrame == null || _currentFrame.Empty() || _correctionPoints.Count < 4)
                return correctedNormalized;

            int w = _currentFrame.Width;
            int h = _currentFrame.Height;

            var srcPoints = new[]
            {
                new Point2f((float)(_correctionPoints[0].NormalizedPosition.X * w),
                            (float)(_correctionPoints[0].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[1].NormalizedPosition.X * w),
                            (float)(_correctionPoints[1].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[2].NormalizedPosition.X * w),
                            (float)(_correctionPoints[2].NormalizedPosition.Y * h)),
                new Point2f((float)(_correctionPoints[3].NormalizedPosition.X * w),
                            (float)(_correctionPoints[3].NormalizedPosition.Y * h))
            };

            var dstPoints = new[]
            {
                new Point2f(0, 0), new Point2f(w, 0), new Point2f(w, h), new Point2f(0, h)
            };

            using (var matrix = Cv2.GetPerspectiveTransform(srcPoints, dstPoints))
            using (var invMatrix = matrix.Inv())
            {
                var corr = new Point2f((float)(correctedNormalized.X * w), (float)(correctedNormalized.Y * h));
                var result = Cv2.PerspectiveTransform(new[] { corr }, invMatrix);
                return new System.Windows.Point(result[0].X / w, result[0].Y / h);
            }
        }

        #endregion

        #region Config Save/Load/Reset

        private void LoadCorrectionConfig()
        {
            if (string.IsNullOrEmpty(_currentCameraName)) return;
            if (!_appConfig.CameraCorrections.TryGetValue(_currentCameraName, out var data)) return;

            if (_correctionPoints.Count == 4)
            {
                _correctionPoints[0].NormalizedPosition = new System.Windows.Point(data.X0, data.Y0);
                _correctionPoints[1].NormalizedPosition = new System.Windows.Point(data.X1, data.Y1);
                _correctionPoints[2].NormalizedPosition = new System.Windows.Point(data.X2, data.Y2);
                _correctionPoints[3].NormalizedPosition = new System.Windows.Point(data.X3, data.Y3);
            }
        }

        private void SaveCorrectionConfig()
        {
            if (string.IsNullOrEmpty(_currentCameraName) || _correctionPoints.Count < 4) return;

            _appConfig.CameraCorrections[_currentCameraName] = new CameraCorrectionData
            {
                X0 = _correctionPoints[0].NormalizedPosition.X,
                Y0 = _correctionPoints[0].NormalizedPosition.Y,
                X1 = _correctionPoints[1].NormalizedPosition.X,
                Y1 = _correctionPoints[1].NormalizedPosition.Y,
                X2 = _correctionPoints[2].NormalizedPosition.X,
                Y2 = _correctionPoints[2].NormalizedPosition.Y,
                X3 = _correctionPoints[3].NormalizedPosition.X,
                Y3 = _correctionPoints[3].NormalizedPosition.Y
            };
            _appConfig.Save();
        }

        private void ResetCorrectionPoints()
        {
            if (_correctionPoints.Count < 4) return;

            var defaults = GetDefaultCorrectionPoints();
            for (int i = 0; i < 4; i++)
            {
                _correctionPoints[i].NormalizedPosition = defaults[i];
            }

            if (!string.IsNullOrEmpty(_currentCameraName))
            {
                _appConfig.CameraCorrections.Remove(_currentCameraName);
                _appConfig.Save();
            }

            UpdateCorrectionOverlay();
        }

        #endregion

        #region Button Handlers

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshCameraList();
        }

        private void BtnCorrection_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _isCorrectionEnabled = BtnCorrection.IsChecked == true;
            OverlayCanvas.IsHitTestVisible = _isCorrectionEnabled;

            if (_isCorrectionEnabled)
            {
                InitializeCorrectionPoints();
                UpdateImageLayoutInfo();
                UpdateCorrectionOverlay();
            }
            else
            {
                ClearCorrectionPoints();
            }
        }

        private void BtnResetCorrection_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                Properties.Resources.ResourceManager.GetString("MsgBox_ResetCorrectionConfirm"),
                Properties.Resources.ResourceManager.GetString("CameraCapture_Title"),
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                ResetCorrectionPoints();
            }
        }

        private void BtnCapture_Click(object sender, RoutedEventArgs e)
        {
            ExecuteCapture();
        }

        private void BtnDecode_Click(object sender, RoutedEventArgs e)
        {
            ExecuteDecode();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #endregion

        #region Capture & Decode

        private Mat GetCaptureFrame()
        {
            if (_currentFrame == null || _currentFrame.Empty())
                return null;

            if (_correctionPoints.Count == 4 && HasValidCorrection())
                return WarpCorrect(_currentFrame);

            return _currentFrame.Clone();
        }

        private void ExecuteCapture()
        {
            string outputDir = _mainVm?.OutputFilePath;
            if (string.IsNullOrWhiteSpace(outputDir) || !Directory.Exists(outputDir))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"),
                    Properties.Resources.ResourceManager.GetString("CameraCapture_Title"), MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            using (Mat frame = GetCaptureFrame())
            {
                if (frame == null)
                {
                    MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_CaptureFailed"),
                        Properties.Resources.ResourceManager.GetString("CameraCapture_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string fileName = null;
                try
                {
                    using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame))
                    {
                        var meta = ImageDecoder.ReadMetadata(bitmap);
                        if (!string.IsNullOrWhiteSpace(meta.FileName))
                            fileName = meta.FileName;
                    }
                }
                catch { }

                if (string.IsNullOrWhiteSpace(fileName))
                    fileName = $"{Properties.Resources.ResourceManager.GetString("Screenshot_FileNamePrefix")}{DateTime.Now:yyyyMMdd_HHmmss}.png";
                else
                    fileName = System.IO.Path.ChangeExtension(fileName, ".png");

                string fullPath = System.IO.Path.Combine(outputDir, fileName);
                fullPath = ScreenCaptureHelper.GetUniqueFilePath(fullPath);

                try
                {
                    var bitmapSource = frame.ToBitmapSource();
                    ScreenCaptureHelper.SavePng(bitmapSource, fullPath);
                    ToastNotification.Show(string.Format(Properties.Resources.ResourceManager.GetString("Toast_SavedTo"), fullPath),
                        Properties.Resources.ResourceManager.GetString("Toast_ScreenshotSuccess"), MessageBoxImage.Information);
                    _mainVm?.AddFiles(new[] { fullPath });
                    StatusText.Text = string.Format(Properties.Resources.ResourceManager.GetString("Status_Captured"), System.IO.Path.GetFileName(fullPath));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Properties.Resources.ResourceManager.GetString("Error_SaveFailed"), ex.Message),
                        Properties.Resources.ResourceManager.GetString("CameraCapture_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExecuteDecode()
        {
            using (Mat frame = GetCaptureFrame())
            {
                if (frame == null)
                {
                    MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_CaptureFailed"),
                        Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                try
                {
                    string metaInfo;
                    string decodeInfo;
                    try
                    {
                        using (var bitmap = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(frame))
                        {
                            var meta = ImageDecoder.ReadMetadata(bitmap);
                            metaInfo = meta?.Metadata != null && meta.Metadata.Length >= 4
                                ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaOk"), meta.FileName, meta.MaxRows, meta.MaxCols, meta.CurrentPage, meta.TotalPages)
                                : Properties.Resources.ResourceManager.GetString("DecodeTest_MetaNotFound");

                            var decodeResult = ImageDecoder.DecodeImageWithMetadata(bitmap, false);
                            bool decodeOk = decodeResult?.DataBlocks?.Count > 0;
                            decodeInfo = decodeOk
                                ? string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeSuccess"), decodeResult.DataBlocks.Count)
                                : Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeFailed");
                        }
                    }
                    catch (Exception ex)
                    {
                        metaInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_MetaError"), ex.Message);
                        decodeInfo = string.Format(Properties.Resources.ResourceManager.GetString("DecodeTest_DecodeError"), ex.Message);
                    }

                    MessageBox.Show($"{metaInfo}\n{decodeInfo}",
                        Properties.Resources.ResourceManager.GetString("MsgBox_Title_DecodeTestResult"), MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(string.Format(Properties.Resources.ResourceManager.GetString("Error_TestFailed"), ex.Message),
                        Properties.Resources.ResourceManager.GetString("DecodeTest_Title"), MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion

        #region Window Lifecycle

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            SaveCorrectionConfig();
            StopPreview();
            ClearCorrectionPoints();
        }

        #endregion
    }
}
