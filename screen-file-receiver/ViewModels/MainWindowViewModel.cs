using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Media;
using System.Windows;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Input;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace screen_file_transmit
{
    public class MainWindowViewModel : INotifyPropertyChanged
    {
        private ObservableCollection<FileItem> _fileItems = new ObservableCollection<FileItem>();
        private string _password;
        private string _statusText = Properties.Resources.ResourceManager.GetString("Status_Ready");
        private double _progressValue;
        private double _progressMaximum = 100;
        private bool _isBusy;
        private BitmapImage _previewImage;
        private FileItem _selectedFileItem;
        private List<FileItem> _selectedFileItems = new List<FileItem>();
        private bool _isSyncingProperty;
        private string _outputFilePath;
        private readonly AppConfig _appConfig = new AppConfig();
        private CancellationTokenSource _cts;
        private readonly ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);
        private bool _isPaused;
        private string _pauseButtonText = Properties.Resources.ResourceManager.GetString("Button_Pause");
        private bool _isBatchAdding;

        public ObservableCollection<FileItem> FileItems
        {
            get => _fileItems;
            set
            {
                _fileItems = value;
                OnPropertyChanged(nameof(FileItems));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string Password
        {
            get => _password;
            set
            {
                _password = value;
                OnPropertyChanged(nameof(Password));
            }
        }

        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value?.Trim();
                OnPropertyChanged(nameof(StatusText));
            }
        }

        public bool IsBatchAdding
        {
            get => _isBatchAdding;
            set
            {
                _isBatchAdding = value;
                OnPropertyChanged(nameof(IsBatchAdding));
            }
        }

        public double ProgressValue
        {
            get => _progressValue;
            set
            {
                _progressValue = value;
                OnPropertyChanged(nameof(ProgressValue));
            }
        }

        public double ProgressMaximum
        {
            get => _progressMaximum;
            set
            {
                _progressMaximum = value;
                OnPropertyChanged(nameof(ProgressMaximum));
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            set
            {
                _isBusy = value;
                OnPropertyChanged(nameof(IsBusy));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public bool IsPaused
        {
            get => _isPaused;
            set
            {
                _isPaused = value;
                OnPropertyChanged(nameof(IsPaused));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string PauseButtonText
        {
            get => _pauseButtonText;
            set
            {
                _pauseButtonText = value;
                OnPropertyChanged(nameof(PauseButtonText));
            }
        }

        public BitmapImage PreviewImage
        {
            get => _previewImage;
            set
            {
                _previewImage = value;
                OnPropertyChanged(nameof(PreviewImage));
            }
        }

        public FileItem SelectedFileItem
        {
            get => _selectedFileItem;
            set
            {
                _selectedFileItem = value;
                OnPropertyChanged(nameof(SelectedFileItem));
                if (!_isBatchAdding)
                    LoadPreview();
            }
        }

        public List<FileItem> SelectedFileItems
        {
            get => _selectedFileItems;
            set
            {
                _selectedFileItems = value;
                OnPropertyChanged(nameof(SelectedFileItems));
                CommandManager.InvalidateRequerySuggested();
            }
        }

        public string OutputFilePath
        {
            get => _outputFilePath;
            set
            {
                _outputFilePath = value;
                OnPropertyChanged(nameof(OutputFilePath));
                CommandManager.InvalidateRequerySuggested();
                _appConfig.SaveDirectory = value;
                _appConfig.Save();
            }
        }

        private static string GetDownloadsPath()
        {
            try
            {
                var downloadsGuid = new Guid("374DE290-123F-4565-9164-39C4925E467B");
                SHGetKnownFolderPath(downloadsGuid, 0, IntPtr.Zero, out var path);
                return path;
            }
            catch
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        static extern int SHGetKnownFolderPath([MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags,
            IntPtr hToken, out string pszPath);

        public MainWindowViewModel()
        {
            _appConfig.Load();
            // 继承上次的放大尝试排序与历史统计
            ImageDecoder.ImportStats(_appConfig.UpscaleOrder, _appConfig.UpscaleStats);
            _outputFilePath = _appConfig.SaveDirectory;
            if (string.IsNullOrWhiteSpace(_outputFilePath))
            {
                _outputFilePath = Path.Combine(GetDownloadsPath(), "ScreenFileReceiver");
                if (!Directory.Exists(_outputFilePath))
                {
                    Directory.CreateDirectory(_outputFilePath);
                }
            }
        }

        /// <summary>
        /// 把当前放大尝试排序与统计写入配置，程序退出时调用。
        /// </summary>
        public void SaveUpscaleStats()
        {
            var stats = ImageDecoder.ExportStats();
            if (stats.TryGetValue("Order", out var order))
            {
                _appConfig.UpscaleOrder = order;
                stats.Remove("Order");
            }

            _appConfig.UpscaleStats.Clear();
            foreach (var kv in stats) _appConfig.UpscaleStats[kv.Key] = kv.Value;

            _appConfig.Save();
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string GetFriendlyFileSize(long bytes)
        {
            string[] sizes =
            {
                Properties.Resources.ResourceManager.GetString("Unit_B"),
                Properties.Resources.ResourceManager.GetString("Unit_KB"),
                Properties.Resources.ResourceManager.GetString("Unit_MB"),
                Properties.Resources.ResourceManager.GetString("Unit_GB"),
                Properties.Resources.ResourceManager.GetString("Unit_TB")
            };
            double len = bytes;
            int order = 0;
            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:0.##} {sizes[order]}";
        }

        public ICommand AddFileCommand => new RelayCommand(_ => AddFiles(), _ => !IsBusy);

        public ICommand RemoveFileCommand => new RelayCommand(_ => RemoveFile(),
            _ => !IsBusy && SelectedFileItems != null && SelectedFileItems.Count > 0 && FileItems.Count > 0);

        public ICommand ClearFilesCommand => new RelayCommand(_ => ClearFiles(), _ => !IsBusy && FileItems.Count > 0);
        public ICommand BrowseOutputPathCommand => new RelayCommand(_ => BrowseOutputPath(), _ => !IsBusy);
        public ICommand OpenOutputPathCommand => new RelayCommand(_ => OpenOutputPath());

        public ICommand ConvertCommand => new RelayCommand(_ => StartConvert(), _ => !IsBusy);

        public ICommand OpenScreenshotToolCommand => new RelayCommand(_ => OpenScreenshotTool(), _ => !IsBusy);
        public ICommand PauseCommand => new RelayCommand(_ => TogglePause(), _ => IsBusy);
        public ICommand StopCommand => new RelayCommand(_ => StopConvert(), _ => IsBusy);

        private async void AddFiles()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Multiselect = true;
            openFileDialog.Filter =
                $"{Properties.Resources.ResourceManager.GetString("FileFilter_ImageFiles")}|*.png;*.jpg;*.jpeg;*.bmp|{Properties.Resources.ResourceManager.GetString("FileFilter_AllFiles")}|*.*";
            if (openFileDialog.ShowDialog() ?? false)
            {
                await AddFilesAsync(openFileDialog.FileNames);
            }
        }

        // 获取 CPU 线程数：物理核心数 × 2（超线程），非超线程则为物理核心数；失败时回退到 Environment.ProcessorCount
        // 调试器附加时强制单线程（maxDop=1），避免并发任务在断点/单步时互相干扰。
        private static int GetCpuThreadCount()
        {
#if SINGLE_THREAD
                return 1;
#endif

            int physicalCores = GetPhysicalCoreCount();
            if (physicalCores > 0)
            {
                // NumberOfLogicalProcessors > NumberOfCores 表示开启了超线程
                int logical = Environment.ProcessorCount;
                return logical > physicalCores ? logical : physicalCores * 2;
            }
            return Environment.ProcessorCount;
        }

        // 通过 WMI 查询物理核心数，失败返回 0
        private static int GetPhysicalCoreCount()
        {
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT NumberOfCores FROM Win32_Processor"))
                {
                    int cores = 0;
                    foreach (ManagementObject obj in searcher.Get())
                        cores += Convert.ToInt32(obj["NumberOfCores"]);
                    return cores;
                }
            }
            catch
            {
                return 0;
            }
        }

        public async Task AddFilesAsync(IEnumerable<string> files)
        {
            var existingPaths = new HashSet<string>(FileItems.Select(f => f.FullPath));
            var fileList = files.OrderBy(c => c).ToList();

            IsBatchAdding = true;
            try
            {
                var maxDop = Math.Max(1, GetCpuThreadCount() - 2);
                using (var throttle = new SemaphoreSlim(maxDop, maxDop))
                {
                    var tasks = fileList.Select(async file =>
                    {
                        if (existingPaths.Contains(file))
                            return;

                        await throttle.WaitAsync();
                        try
                        {
                            var item = await Task.Run(() => CreateFileItem(file));
                            if (item == null)
                                return;

                            // UI 更新必须在 UI 线程上串行执行
                            await Application.Current.Dispatcher.InvokeAsync(() =>
                            {
                                // 重截替换：同 EffectiveFileId + 同 CurrentPage 视为同一行，覆盖而非新增
                                var dup = FileItems.FirstOrDefault(f =>
                                    f.EffectiveFileId == item.EffectiveFileId &&
                                    f.CurrentPage == item.CurrentPage &&
                                    f.TotalPages == item.TotalPages &&
                                    f.EffectiveFileId.Length > 0);
                                if (dup != null)
                                {
                                    dup.FullPath = item.FullPath;
                                    dup.ImageFileName = item.ImageFileName;
                                    dup.IsUnderfull = item.IsUnderfull;
                                    dup.MetadataInfo = item.MetadataInfo;
                                    dup.Status = item.Status;
                                    existingPaths.Add(file);
                                    return;
                                }

                                // 行号在加入时一次性确定，之后不随排序变化
                                item.RowNumber = FileItems.Count + 1;
                                item.PropertyChanged += FileItem_PropertyChanged;
                                FileItems.Add(item);
                                existingPaths.Add(file);
                                SelectedFileItem = item;
                            }).Task;
                        }
                        finally
                        {
                            throttle.Release();
                        }
                    }).ToList();

                    await Task.WhenAll(tasks);
                }
            }
            finally
            {
                IsBatchAdding = false;
            }

            SortFileItemsByPage();
            RemoveDuplicateItems();
            CheckFileComplete();
        }

        /// <summary>
        /// 批量添加完成后按页码排序：同一文件ID内按 CurrentPage 升序，
        /// 文件ID之间保持首次出现顺序（稳定分组，不打乱不同文件的相对位置）。
        /// </summary>
        private void SortFileItemsByPage()
        {
            // 按 EffectiveFileId 分组，保持组的首次出现顺序（GroupBy 本身是稳定的）
            var groups = FileItems
                .GroupBy(f => f.EffectiveFileId ?? "")
                .Select(g => g
                    .OrderBy(f => f.CurrentPage)
                    .ToList())
                .ToList();

            var sorted = groups.SelectMany(g => g).ToList();

            // ObservableCollection 没有整体重排 API，逐项移动到新位置
            for (int i = 0; i < sorted.Count; i++)
            {
                var currentIndex = FileItems.IndexOf(sorted[i]);
                if (currentIndex != i)
                    FileItems.Move(currentIndex, i);
            }

            // 排序后重新分配行号
            for (int i = 0; i < FileItems.Count; i++)
                FileItems[i].RowNumber = i + 1;
        }

        private bool ShowFileCompletenessWarnings()
        {
            var warnings = new List<string>();

            var groups = FileItems
                .Where(f => !string.IsNullOrEmpty(f.FileId) && f.TotalPages > 0)
                .GroupBy(c => new { c.EffectiveFileId, c.SaveFileName })
                .ToList();

            foreach (var group in groups)
            {
                var first = group.FirstOrDefault();
                if (first == null) continue;

                var pages = group.Select(c => c.CurrentPage).ToList();
                bool hasDuplicate = pages.Count != pages.Distinct().Count();
                bool allPagesPresent = Enumerable.Range(1, first.TotalPages).All(p => pages.Contains(p));

                if (!hasDuplicate && allPagesPresent) continue;

                var sb = new StringBuilder();
                sb.AppendLine(string.Format("{0} (ID: {1})",
                    group.Key.SaveFileName, group.Key.EffectiveFileId));
                sb.AppendLine(string.Format(
                    Properties.Resources.ResourceManager.GetString("MsgBox_FileGroup_ExpectedPages"),
                    first.TotalPages));
                sb.AppendLine(string.Format(
                    Properties.Resources.ResourceManager.GetString("MsgBox_FileGroup_ActualPages"), pages.Count));

                if (!allPagesPresent)
                {
                    var missingPages = Enumerable.Range(1, first.TotalPages)
                        .Where(p => !pages.Contains(p))
                        .ToList();
                    sb.AppendLine(string.Format(
                        Properties.Resources.ResourceManager.GetString("MsgBox_FileGroup_MissingPages"),
                        string.Join(", ", missingPages)));
                }

                if (hasDuplicate)
                {
                    var duplicatePages = pages.GroupBy(p => p)
                        .Where(g => g.Count() > 1)
                        .Select(g => g.Key)
                        .OrderBy(p => p)
                        .ToList();
                    sb.AppendLine(string.Format(
                        Properties.Resources.ResourceManager.GetString("MsgBox_FileGroup_DuplicatePages"),
                        string.Join(", ", duplicatePages)));
                }

                var extraPages = pages.Where(p => p > first.TotalPages || p < 1).Distinct().OrderBy(p => p).ToList();
                if (extraPages.Count > 0)
                {
                    sb.AppendLine(string.Format(
                        Properties.Resources.ResourceManager.GetString("MsgBox_FileGroup_ExtraPages"),
                        string.Join(", ", extraPages)));
                }

                warnings.Add(sb.ToString());
            }

            if (warnings.Count > 0)
            {
                var message = Properties.Resources.ResourceManager.GetString("MsgBox_FileCompletenessWarning")
                              + "\n\n" + string.Join("\n", warnings);
                MessageBox.Show(message,
                    Properties.Resources.ResourceManager.GetString("MsgBox_FileCompletenessTitle"),
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            return true;
        }

        private void CheckFileComplete()
        {
            foreach (var group in FileItems.GroupBy(c => new { c.EffectiveFileId, c.SaveFileName }))
            {
                var first = group.FirstOrDefault();
                if (first == null || first.TotalPages <= 0)
                {
                    foreach (var item in group)
                        item.IsComplete = false;
                    continue;
                }

                var pages = group.Select(c => c.CurrentPage).ToList();
                bool hasDuplicate = pages.Count != pages.Distinct().Count();
                bool allPagesPresent = Enumerable.Range(1, first.TotalPages).All(p => pages.Contains(p));
                bool isComplete = !hasDuplicate && allPagesPresent;

                foreach (var item in group)
                {
                    item.IsComplete = isComplete;
                }
            }
        }

        private FileItem CreateFileItem(string filePath)
        {
            try
            {
                var meta = ImageDecoder.ReadMetadata(filePath);
                var item = new FileItem
                {
                    FullPath = filePath,
                    ImageFileName = Path.GetFileName(filePath),
                    FileId = meta?.FileId ?? "",
                    SaveFileName = meta?.FileName ?? Path.GetFileNameWithoutExtension(filePath),
                    RawMetadata = meta?.Metadata,
                    MaxRows = meta?.MaxRows ?? 0,
                    MaxCols = meta?.MaxCols ?? 0,
                    Colorful = meta?.Colorful ?? false,
                    HasPassword = meta?.HasPassword ?? false,
                    HasErrorCorrection = meta?.HasErrorCorrection ?? false,
                    ErrorCorrectionPercent = meta?.ErrorCorrectionPercent ?? 0,
                    ColorDepth = meta?.ColorDepth ?? 0,
                    CurrentPage = meta?.CurrentPage ?? 0,
                    TotalPages = meta?.TotalPages ?? 0,
                    TotalQrCodeCount = meta?.TotalQrCodeCount ?? 0
                };
                item.EffectiveFileId = item.FileId;
                if (meta.TotalPages > 0)
                {
                    // 添加文件只解析元数据，不做整页二维码解码——
                    // 「未满」状态由自动翻页流程（AutoFlipTick）识别到未满时计算并标记，
                    // 这里不预先解码，避免添加时就触发耗时的整页解码。
                    item.IsUnderfull = false;

                    //item.DeleteCommand = new RelayCommand(_ => DeleteItem(item), _ => !IsBusy);
                    //item.RetryCommand = new RelayCommand(_ => RetryItem(item), _ => !IsBusy);
                    // 页码单独成列，不再混在元数据里
                    item.MetadataInfo =
                        $"{item.MaxRows}x{item.MaxCols} {(item.Colorful ? Properties.Resources.ResourceManager.GetString("Meta_Colorful") : Properties.Resources.ResourceManager.GetString("Meta_BlackWhite"))} D={item.ColorDepth} DM={item.TotalQrCodeCount}{(item.HasPassword ? " " + Properties.Resources.ResourceManager.GetString("Meta_HasPassword") : "")}{(item.HasErrorCorrection ? " " + string.Format(Properties.Resources.ResourceManager.GetString("Meta_ErrorCorrection"), item.ErrorCorrectionPercent) : "")}";
                }
                else
                {
                    item.Status = Properties.Resources.ResourceManager.GetString("Status_MetaParseFailed");
                }

                return item;
            }
            catch (Exception ex)
            {
                var errorItem = new FileItem
                {
                    FullPath = filePath,
                    ImageFileName = Path.GetFileName(filePath),
                    SaveFileName = Path.GetFileNameWithoutExtension(filePath),
                    EffectiveFileId = string.Empty,
                    Status = string.Format(Properties.Resources.ResourceManager.GetString("Status_ReadFailed"),
                        ex.Message)
                };
                //errorItem.DeleteCommand = new RelayCommand(_ => DeleteItem(errorItem), _ => !IsBusy);
                //errorItem.RetryCommand = new RelayCommand(_ => RetryItem(errorItem), _ => !IsBusy);
                return errorItem;
            }
        }

        private void FileItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(FileItem.SaveFileName) && !_isSyncingProperty)
            {
                var changedItem = sender as FileItem;
                if (changedItem == null || string.IsNullOrEmpty(changedItem.FileId))
                    return;

                _isSyncingProperty = true;
                foreach (var item in FileItems)
                {
                    if (item != changedItem && item.EffectiveFileId == changedItem.EffectiveFileId
                        && !string.IsNullOrEmpty(item.EffectiveFileId))
                    {
                        item.SaveFileName = changedItem.SaveFileName;
                    }
                }

                _isSyncingProperty = false;
                CheckFileComplete();
            }
        }

        /// <summary>
        /// 转换前的预处理：先按元数据去重（除页码外元数据相同即视为同一张图，
        /// 每个页码只保留第一个文件仍存在的条目，其余丢弃），
        /// 再对不完整的文件组识别可合并的文件ID。
        /// </summary>
        private void PrepareBeforeConvert()
        {
            RemoveDuplicateItems();
            CheckFileComplete();
        }

        /// <summary>
        /// 去重：元数据特征（不含页码）相同的条目视为同一个文件的不同页，
        /// 每个页码只保留第一个「文件仍存在」的条目，其余从表格中移除。
        /// 元数据完全一致的同页图片只保留一张。
        /// </summary>
        private void RemoveDuplicateItems()
        {
            var removed = new List<FileItem>();

            foreach (var group in FileItems
                         .Where(f => f.TotalPages > 0)
                         .GroupBy(GetMergeKey)
                         .ToList())
            {
                var keptPages = new HashSet<int>();
                foreach (var item in group.OrderBy(f => f.RowNumber))
                {
                    bool fileExists = !string.IsNullOrEmpty(item.FullPath) && File.Exists(item.FullPath);
                    if (!fileExists || !keptPages.Add(item.CurrentPage))
                        removed.Add(item);
                }
            }

            if (removed.Count == 0) return;

            _isSyncingProperty = true;
            foreach (var item in removed)
            {
                item.PropertyChanged -= FileItem_PropertyChanged;
                FileItems.Remove(item);
            }

            _isSyncingProperty = false;
            RenumberRows();
        }

        /// <summary>
        /// 删除/移除后重算行号，保持 1..N 连续。
        /// </summary>
        private void RenumberRows()
        {
            for (int i = 0; i < FileItems.Count; i++)
            {
                var item = FileItems[i];
                int newNumber = i + 1;
                if (item.RowNumber != newNumber)
                    item.RowNumber = newNumber;
            }
        }

        /// <summary>
        /// 归一化分组键：除页码外的全部元数据。相同即视为同一个文件。
        /// </summary>
        private static string GetMergeKey(FileItem item)
        {
            return item.MetaSignature;
        }

        /// <summary>
        /// 对每个不完整的文件组，找出「合并后恰好补齐全部页码且不重复」的合并方案。
        /// </summary>
        private List<MergeCandidateGroup> BuildMergeCandidates()
        {
            var result = new List<MergeCandidateGroup>();

            foreach (var g in FileItems
                         .Where(f => f.TotalPages > 0 && !string.IsNullOrEmpty(f.RawFileId))
                         .GroupBy(GetMergeKey))
            {
                var items = g.ToList();
                var byId = items.GroupBy(c => c.RawFileId).ToList();
                if (byId.Count < 2) continue;

                var total = items[0].TotalPages;
                var selfPages = items.Select(c => c.CurrentPage).ToHashSet();
                bool selfComplete = Enumerable.Range(1, total).All(selfPages.Contains);
                if (selfComplete) continue;

                // 枚举 2~Math.Min(4, ID数) 个原始ID 的组合，找合并后恰好补齐的方案
                MergeCandidateGroup best = null;
                var ids = byId.Select(x => x.Key).OrderBy(k => k, StringComparer.OrdinalIgnoreCase).ToList();
                foreach (var combo in EnumerateIdCombinations(ids, Math.Min(4, ids.Count)))
                {
                    var comboSet = new HashSet<string>(combo);
                    var comboItems = items.Where(i => comboSet.Contains(i.RawFileId)).ToList();
                    var pages = comboItems.Select(i => i.CurrentPage).ToList();
                    if (pages.Count != pages.Distinct().Count()) continue; // 合并后仍有重复页
                    if (!Enumerable.Range(1, total).All(pages.Contains)) continue; // 合并后仍缺页

                    var candidate = new MergeCandidateGroup
                    {
                        SaveFileName = items[0].SaveFileName,
                        MetaSignature = items[0].MetaSignature,
                        TotalPages = total,
                        DistinctIdCount = combo.Count,
                        Items = comboItems.OrderBy(i => i.CurrentPage).ToList(),
                        PreviewId = PickPreferredId(comboItems)
                    };

                    // 优先选涉及 ID 少的方案，其次选图片少的方案
                    if (best == null
                        || candidate.DistinctIdCount < best.DistinctIdCount
                        || (candidate.DistinctIdCount == best.DistinctIdCount && candidate.Items.Count < best.Items.Count))
                    {
                        best = candidate;
                    }
                }

                if (best != null) result.Add(best);
            }

            return result;
        }

        /// <summary>
        /// 枚举 idCount 以内的所有 ID 组合（元素有序，避免重复枚举）。
        /// </summary>
        private static IEnumerable<List<string>> EnumerateIdCombinations(List<string> ids, int maxCount)
        {
            for (int n = 2; n <= maxCount; n++)
            {
                foreach (var combo in EnumerateIdCombinations(ids, n, 0, new List<string>()))
                    yield return combo;
            }
        }

        private static IEnumerable<List<string>> EnumerateIdCombinations(List<string> ids, int n, int start, List<string> current)
        {
            if (current.Count == n)
            {
                yield return new List<string>(current);
                yield break;
            }

            for (int i = start; i <= ids.Count - (n - current.Count); i++)
            {
                current.Add(ids[i]);
                foreach (var combo in EnumerateIdCombinations(ids, n, i + 1, current))
                    yield return combo;
                current.RemoveAt(current.Count - 1);
            }
        }

        /// <summary>
        /// 合并后的目标文件ID取该组中出现次数最多的原始ID，平票取字典序最小者。
        /// </summary>
        private static string PickPreferredId(IList<FileItem> items)
        {
            return items.GroupBy(c => c.RawFileId)
                .OrderByDescending(x => x.Count())
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .First()
                .Key;
        }

        /// <summary>
        /// 检测到可合并的文件ID后弹窗确认；用户同意则把这些图片并入同一个文件ID，
        /// 后续解析（分组、完整性判定、输出拼接）都按合并后的文件ID视为同一个文件。
        /// </summary>
        private bool ConfirmAndApplyMerges()
        {
            var candidates = BuildMergeCandidates();
            if (candidates.Count == 0) return true;

            var dialog = new MergeFileIdDialog(candidates)
            {
                Owner = Application.Current?.MainWindow
            };
            if (dialog.ShowDialog() != true || dialog.SelectedGroups.Count == 0)
                return false;

            foreach (var group in dialog.SelectedGroups)
            {
                foreach (var item in group.Items)
                    item.EffectiveFileId = group.PreviewId;

                // 同组保持保存文件名一致，避免合并后同组分家
                var name = group.Items[0].SaveFileName;
                if (string.IsNullOrEmpty(name)) continue;

                _isSyncingProperty = true;
                foreach (var item in group.Items)
                {
                    if (item.SaveFileName != name)
                        item.SaveFileName = name;
                }

                _isSyncingProperty = false;
            }

            CheckFileComplete();
            return true;
        }

        private void RemoveFile()
        {
            if (SelectedFileItems == null || SelectedFileItems.Count == 0)
                return;

            var itemsToRemove = SelectedFileItems.ToList();
            foreach (var item in itemsToRemove)
            {
                if (FileItems.Contains(item))
                {
                    item.PropertyChanged -= FileItem_PropertyChanged;
                    FileItems.Remove(item);
                }
            }

            RenumberRows();
            CheckFileComplete();
        }

        private void ClearFiles()
        {
            foreach (var item in FileItems)
                item.PropertyChanged -= FileItem_PropertyChanged;
            FileItems.Clear();
            SelectedFileItem = null;
            PreviewImage = null;
        }

        private void DeleteItem(FileItem item)
        {
            if (item == null) return;
            if (SelectedFileItem == item)
            {
                var index = FileItems.IndexOf(item);
                FileItems.Remove(item);
                if (FileItems.Count > 0)
                    SelectedFileItem = FileItems[Math.Min(index, FileItems.Count - 1)];
                else
                    SelectedFileItem = null;
            }
            else
            {
                FileItems.Remove(item);
            }

            item.PropertyChanged -= FileItem_PropertyChanged;
            RenumberRows();
        }

        //private void RetryItem(FileItem item)
        //{
        //    if (item == null || string.IsNullOrEmpty(item.FullPath))
        //        return;

        //    item.Status = "重试中...";
        //    item.ProgressValue = 0;
        //    try
        //    {
        //        using (var tempStream = new FileStream(Path.GetTempFileName(), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 4096, FileOptions.DeleteOnClose))
        //        {
        //            if (DataMatrixReader.ReadToFile(item.FullPath, tempStream, false))
        //            {
        //                item.Status = "就绪";
        //                if (!string.IsNullOrEmpty(extractedName) && string.IsNullOrEmpty(item.SaveFileName))
        //                    item.SaveFileName = extractedName;
        //            }
        //            else
        //            {
        //                item.Status = "解析失败";
        //            }
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        item.Status = $"错误: {ex.Message}";
        //    }
        //}

        private void LoadPreview()
        {
            if (SelectedFileItem != null && !string.IsNullOrEmpty(SelectedFileItem.FullPath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(SelectedFileItem.FullPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 128;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    PreviewImage = bitmap;
                }
                catch
                {
                    PreviewImage = null;
                }
            }
            else
            {
                PreviewImage = null;
            }
        }

        private void BrowseOutputPath()
        {
            var dialog = new FolderPicker
            {
                Title = Properties.Resources.ResourceManager.GetString("Dialog_SelectSaveFolder"),
                SelectedPath = OutputFilePath
            };

            if (dialog.ShowDialog(Application.Current.MainWindow) == true)
            {
                OutputFilePath = dialog.SelectedPath;
            }
        }

        private void OpenOutputPath()
        {
            var path = OutputFilePath;
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SavePathNotExist"));
                return;
            }

            System.Diagnostics.Process.Start("explorer.exe", path);
        }

        private ScreenshotToolWindow screenshotToolWindow;

        private void OpenScreenshotTool()
        {
            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SetSavePathFirst"),
                    Properties.Resources.ResourceManager.GetString("ScreenshotTool_Title"), MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            if (screenshotToolWindow != null)
            {
                screenshotToolWindow.Close();
                screenshotToolWindow = null;
            }
            else
            {
                screenshotToolWindow = new ScreenshotToolWindow(this);
                screenshotToolWindow.Show();
            }
        }

        private void TogglePause()
        {
            if (IsPaused)
            {
                _pauseEvent.Set();
                IsPaused = false;
                PauseButtonText = Properties.Resources.ResourceManager.GetString("Button_Pause");
                StatusText = "正在解析...";
            }
            else
            {
                _pauseEvent.Reset();
                IsPaused = true;
                PauseButtonText = Properties.Resources.ResourceManager.GetString("Button_Resume");
                StatusText = Properties.Resources.ResourceManager.GetString("Status_Paused");
            }
        }

        private void StopConvert()
        {
            _cts?.Cancel();
            _pauseEvent.Set();
        }

        private string GetUniqueFilePath(string basePath)
        {
            if (!File.Exists(basePath))
                return basePath;

            string directory = Path.GetDirectoryName(basePath);
            string fileNameWithoutExt = Path.GetFileNameWithoutExtension(basePath);
            string extension = Path.GetExtension(basePath);
            int index = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(directory, $"{fileNameWithoutExt}({index}){extension}");
                index++;
            } while (File.Exists(newPath));

            return newPath;
        }

        private async void StartConvert()
        {
            if (string.IsNullOrWhiteSpace(OutputFilePath))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_SelectSavePathFirst"));
                return;
            }

            if (!Directory.Exists(OutputFilePath))
            {
                Directory.CreateDirectory(OutputFilePath);
            }

            // 转换前先去重：元数据相同的图片只保留第一个文件仍存在的
            PrepareBeforeConvert();

            // 文件不全时识别可合并的文件ID，合并后恰好补齐则提示用户合并
            if (!ConfirmAndApplyMerges())
            {
                return;
            }

            if (!ShowFileCompletenessWarnings())
            {
                return;
            }

            var completeItems = FileItems.Where(f => f.IsComplete).ToList();
            if (completeItems.Count == 0)
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_NoCompleteFiles"));
                return;

            }

            if (completeItems.Any(f => f.HasPassword && string.IsNullOrEmpty(Password)))
            {
                MessageBox.Show(Properties.Resources.ResourceManager.GetString("Error_PasswordRequired"));
                return;
            }

            var groups = completeItems.GroupBy(f => new { f.EffectiveFileId, f.SaveFileName }).ToList();

            // 重置所有待转换文件的状态和错误信息
            foreach (var item in completeItems)
            {
                item.Status = Properties.Resources.ResourceManager.GetString("Status_Ready");
                item.ProgressValue = 0;
            }

            _cts = new CancellationTokenSource();
            _pauseEvent.Set();
            IsPaused = false;
            IsBusy = true;
            ProgressMaximum = completeItems.Count;
            ProgressValue = 0;
            StatusText = "正在解析...";

            try
            {
                if (!Directory.Exists(OutputFilePath))
                {
                    Directory.CreateDirectory(OutputFilePath);
                }

                bool anyFailed = await Task.Run(() =>
                {
                    bool failed = false;
                    int processedCount = 0;
                    var dispatcher = Application.Current.Dispatcher;

                    try
                    {
                        foreach (var group in groups)
                        {
                            if (_cts.IsCancellationRequested)
                                return failed;
                            string outputFileName = group.Key.SaveFileName;
                            if (string.IsNullOrWhiteSpace(outputFileName))
                                outputFileName =
                                    Properties.Resources.ResourceManager.GetString("Default_DecodeFileName");

                            outputFileName = ScreenCaptureHelper.SanitizeFileName(outputFileName);
                            string outputPath = Path.Combine(OutputFilePath, outputFileName);
                            outputPath = GetUniqueFilePath(outputPath);

                            using (var encryptedMs = new MemoryStream())
                            {
                                var sortedItems = group.OrderBy(f => f.CurrentPage).ToList();
                                foreach (var item in sortedItems)
                                {
                                    // 检查暂停和取消
                                    try
                                    {
                                        _pauseEvent.Wait(_cts.Token);
                                    }
                                    catch (OperationCanceledException)
                                    {
                                        return failed;
                                    }

                                    if (_cts.IsCancellationRequested)
                                        return failed;

                                    processedCount++;
                                    int currentNo = processedCount;

                                    // 主界面底部进度条 + StatusText 按「已解析文件数 / 总文件数」推进，
                                    // 两者同源计算，不依赖 decodeProgress 回调是否触发，避免时有时无。
                                    int totalFiles = completeItems.Count;
                                    dispatcher.Invoke(() =>
                                    {
                                        //item.Status = string.Format(
                                        //    Properties.Resources.ResourceManager.GetString("Status_ParsingFormat"),
                                        //    currentNo, totalFiles);
                                        item.ProgressValue = 0;
                                        ProgressValue = currentNo;
                                        double overallPercent = totalFiles <= 0
                                            ? 0.0
                                            : ((currentNo - 1) * 100.0 / totalFiles);
                                        StatusText = string.Format(
                                            Properties.Resources.ResourceManager.GetString("Status_ParsingFormat"),
                                            currentNo, totalFiles) + $" {overallPercent:0.00}%";
                                    });

                                    // 解码过程中实时刷新该行进度：已解码二维码数 / 总数
                                    double lastPercent = -1;
                                    Action<int, int> decodeProgress = (done, total) =>
                                    {
                                        double percent = total <= 0 ? 0.0 : (done * 100.0 / total);
                                        if (percent == lastPercent) return;
                                        lastPercent = percent;

                                        dispatcher.Invoke(() =>
                                        {
                                            item.Status = string.Format(
                                                Properties.Resources.ResourceManager.GetString("Status_ParsingFormat"),
                                                currentNo, totalFiles) + $" {percent:0.00}%";
                                            // 底部进度条只跟「文件数」走，这里不动 ProgressValue，
                                            // 只用文件进度刷新 StatusText 的百分比（已解析文件数占比）。
                                            int overallPercent = totalFiles <= 0
                                                ? 0
                                                : (int)((currentNo - 1) * 100.0 / totalFiles);
                                            StatusText = string.Format(
                                                Properties.Resources.ResourceManager.GetString("Status_ParsingFormat"),
                                                currentNo, totalFiles) + $" {overallPercent}%";
                                        });
                                    };

                                    if (string.IsNullOrEmpty(item.FullPath))
                                    {
                                        dispatcher.Invoke(() =>
                                            item.Status =
                                                Properties.Resources.ResourceManager.GetString("Status_EmptyPath"));
                                        failed = true;
                                        continue;
                                    }

                                    try
                                    {
                                        if (!ImageDecoder.ReadToFile(item.FullPath, encryptedMs, false, decodeProgress))
                                        {
                                            dispatcher.Invoke(() =>
                                                item.Status =
                                                    Properties.Resources.ResourceManager
                                                        .GetString("Status_ParseFailed"));
                                            failed = true;
                                        }
                                        else
                                        {
                                            dispatcher.Invoke(() =>
                                            {
                                                item.Status =
                                                    Properties.Resources.ResourceManager.GetString("Status_Complete");
                                                item.ProgressValue = 100;
                                            });
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        dispatcher.Invoke(() =>
                                            item.Status =
                                                string.Format(
                                                    Properties.Resources.ResourceManager.GetString("Error_ParseFailed"),
                                                    ex.Message));
                                        failed = true;
                                    }
                                }

                                if (_cts.IsCancellationRequested)
                                    return failed;

                                encryptedMs.Position = 0;
                                using (var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write))
                                {
                                    if (!string.IsNullOrEmpty(Password))
                                    {
                                        CryptoHelper.DecryptStream(encryptedMs, fs, Password);
                                    }
                                    else
                                    {
                                        encryptedMs.CopyTo(fs);
                                    }
                                }
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        return failed;
                    }

                    return failed;
                });

                if (_cts.IsCancellationRequested)
                {
                    StatusText = Properties.Resources.ResourceManager.GetString("Status_Stopped");
                    SystemSounds.Exclamation.Play();
                }
                else if (anyFailed)
                {
                    StatusText = Properties.Resources.ResourceManager.GetString("Status_PartialFailed");
                    SystemSounds.Exclamation.Play();
                }
                else
                {
                    StatusText = Properties.Resources.ResourceManager.GetString("Status_ParseSuccess");
                    SystemSounds.Asterisk.Play();
                }
            }
            catch (Exception e)
            {
                if (_cts == null || !_cts.IsCancellationRequested)
                {
                    MessageBox.Show(e.Message);
                    StatusText = string.Format(Properties.Resources.ResourceManager.GetString("Error_Error"),
                        e.Message);
                }
            }
            finally
            {
                _pauseEvent.Set();
                _cts?.Dispose();
                _cts = null;
                IsPaused = false;
                PauseButtonText = Properties.Resources.ResourceManager.GetString("Button_Pause");
                IsBusy = false;
            }
        }
    }
}