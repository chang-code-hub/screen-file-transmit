# 计划：为 screen-file-receiver 添加截图工具

## 上下文

`screen-file-receiver` 是一个通过 DataMatrix 二维码视觉传输文件的 WPF 接收端。当前用户需要手动将屏幕截图保存为图片后拖入软件进行解码。为了提高效率，需要在接收端内置一个截图工具，允许用户直接在桌面上选择窗口或框选区域进行截图，并自动尝试解码和命名。

## 实现方案

### 1. 主界面添加启动入口

在 `MainWindow.xaml` 底部（Grid.Row 6，与"转换"按钮同行）添加一个"截图工具"按钮，绑定到 `MainWindowViewModel` 的新命令 `OpenScreenshotToolCommand`。点击后打开一个独立的截图工具窗口。

### 2. 截图工具窗口 (`ScreenshotToolWindow`)

创建一个窄条形的 WPF 工具窗口：
- `WindowStyle="ToolWindow"`，`Topmost="True"`，`ResizeMode="NoResize"`
- 包含 5 个功能按钮：**选择窗口(Ctrl+F2)**、**选择区域(Ctrl+F3)**、**截图(Ctrl+F4)**、**解码测试(Ctrl+F5)**、**取消选择(Ctrl+F6)**
- 通过 `PreviewKeyDown` 处理 Ctrl+F2~F6 和 Esc 快捷键
- 持有对 `MainWindowViewModel` 的引用，以便读取保存路径

### 3. 选择窗口 (`WindowSelectOverlay`)

创建一个全屏透明覆盖窗口（覆盖整个虚拟桌面）：
- 鼠标移动时通过 `NativeMethods.GetCursorPos` 获取物理坐标，再用 `WindowFromPoint` 命中窗口/控件
- 支持选择**子窗口/控件**（不再强制 `GetAncestor(GA_ROOT)`）
- 子窗口/控件过滤：高或宽小于 **40 像素** 的不选中
- 使用 `DwmGetWindowAttribute(DWMWA_EXTENDED_FRAME_BOUNDS)` 获取不含阴影的精确窗口边界
- 高亮边框比目标大 **2px**（包围在外侧）
- 点击鼠标左键确认选择，记录 `HWND`，关闭覆盖层
- 按 Esc 取消

### 4. 边框跟随 (`SelectionBorderWindow`)

选择窗口/控件后，显示一个红色边框窗口：
- `WindowStyle="None"`，`AllowsTransparency="True"`，`Background="Transparent"`，`ShowInTaskbar="False"`，**无 `Topmost`**
- 边框比目标大 2px
- 使用 `DispatcherTimer`（间隔 **16ms**）持续调用 `GetWindowRect` 更新位置
- **z-order 管理**：通过 `SetWindowPos` 将边框插入到目标顶层窗口的**上一层**
  - 先通过 `GetWindow(zAnchor, GW_HWNDPREV)` 找到目标窗口上方的窗口
  - 边框插入到该窗口之后，确保显示在目标窗口上一层
  - 边框**不置顶**，也不被目标窗口自身遮挡
  - 只有当 z-order 偏离时才调用 `SetWindowPos`（`SWP_NOMOVE | SWP_NOSIZE`），避免闪烁

### 5. 选择区域 (`RegionSelectOverlay`)

创建一个全屏半透明遮罩窗口（覆盖整个物理桌面）：
- 背景为半透明黑色 `#80000000`
- 鼠标拖拽时绘制白色虚线矩形 (`Rectangle`)
- 松开鼠标后记录屏幕坐标区域（物理像素），关闭覆盖层
- 关闭后 `SelectionBorderWindow` 静态显示在选定区域上
- 按 Esc 取消

### 6. DPI 缩放兼容

在 `ScreenCaptureHelper` 中新增 DPI 转换辅助方法：
- `GetDpiScale(Visual)`：获取 WPF 逻辑单位到物理像素的转换矩阵
- `LogicalToPhysical` / `PhysicalToLogical`：坐标方向转换
- `WindowSelectOverlay`、`RegionSelectOverlay`、`SelectionBorderWindow` 的坐标全部通过物理/逻辑转换处理

### 7. 屏幕捕获与保存 (`ScreenCaptureHelper`)

新建静态辅助类，提供：
- `CaptureWindow(hwnd)`：使用 `PrintWindow` (`PW_RENDERFULLCONTENT`) 捕获窗口，失败时回退到 `BitBlt`
- `CaptureRegion(rect)`：使用 `Graphics.CopyFromScreen` 捕获屏幕区域
- `ToBitmapSource(bitmap)`：将 `System.Drawing.Bitmap` 转为 WPF `BitmapSource`
- `SavePng(bitmapSource, path)`：使用 `PngBitmapEncoder` 无损保存 PNG
- `GetUniqueFilePath(basePath)`：避免文件名冲突

截图时：
1. 根据之前选中的窗口句柄或区域执行捕获
2. 尝试调用 `ImageDecoder.ReadMetadata(bitmap)` 读取文件名
3. 如识别到元数据中的文件名，则以此命名；否则使用默认时间戳命名
4. 保存到 `MainWindowViewModel.OutputFilePath`（主界面的保存路径）
5. 保存成功后使用**网页风格弹窗**（非 `MessageBox`）：
   - 弹窗自动倒计时 10 秒后消失
   - 点击弹窗外区域自动关闭

### 8. 解码测试

截图后，将最近一次捕获的 `Bitmap` 临时保存为 `%TEMP%` 下的 PNG，调用现有的 `ImageDecoder.DecodeImageWithMetadata(tempPath)` 进行完整解码测试，测试完成后删除临时文件。结果通过**网页风格弹窗**展示元数据识别情况和数据块解码情况。

### 9. `ImageDecoder` 支持内存图片

为 `ImageDecoder` 添加 `Mat` 和 `Bitmap` 重载：
- 提取 `DetectBarcodes(Mat, ...)` 和 `ReadMetadata(Mat, ...)` 的公共逻辑
- 添加 `ReadMetadata(Bitmap bitmap)` 以便截图后在不保存文件的情况下测试元数据
- 现有基于文件路径的方法保持不变，内部复用新的 `Mat` 重载

## 关键文件与修改

### 新建文件
- `screen-file-receiver/NativeMethods.cs` — P/Invoke 签名（含 `SetWindowPos`、`GetWindow`、`DwmGetWindowAttribute`、`GetCursorPos` 等）
- `screen-file-receiver/ScreenCaptureHelper.cs` — 截图与保存辅助类（含 DPI 转换）
- `screen-file-receiver/ScreenshotToolWindow.xaml` — 截图工具主窗口
- `screen-file-receiver/ScreenshotToolWindow.xaml.cs` — 工具窗口逻辑（含边框跟随、弹窗调用）
- `screen-file-receiver/WindowSelectOverlay.xaml` — 窗口选择覆盖层
- `screen-file-receiver/WindowSelectOverlay.xaml.cs` — 窗口选择逻辑（含子控件、DWM边界、小尺寸过滤）
- `screen-file-receiver/RegionSelectOverlay.xaml` — 区域选择覆盖层
- `screen-file-receiver/RegionSelectOverlay.xaml.cs` — 区域选择逻辑
- `screen-file-receiver/SelectionBorderWindow.xaml` — 红色边框窗口
- `screen-file-receiver/SelectionBorderWindow.xaml.cs` — 边框窗口代码

### 修改文件
- `screen-file-receiver/MainWindow.xaml` — 底部添加"截图工具"按钮
- `screen-file-receiver/MainWindowViewModel.cs` — 添加 `OpenScreenshotToolCommand` 及打开方法
- `screen-file-receiver/ImageDecoder.cs` — 添加 `DetectBarcodes(Mat)`、`ReadMetadata(Mat)`、`ReadMetadata(Bitmap)` 重载

## 复用的现有类/方法

- `ImageDecoder.ReadMetadata(string)` / `ImageDecoder.DecodeImageWithMetadata(string)` — 解码核心逻辑
- `ImageDecoder.DetectBarcodesInRoi` — 条码检测算法
- `MainWindowViewModel.OutputFilePath` — 截图保存路径
- `OpenCvSharp.Extensions.BitmapConverter.ToMat` — 将 GDI Bitmap 转为 OpenCV Mat

## 验证方案

1. 构建项目：`dotnet build screen-file-receiver/screen-file-receiver.csproj`
2. 运行接收端，确认主界面底部出现"截图工具"按钮
3. 点击按钮，确认浮动工具窗口出现在最上层
4. 按 Ctrl+F2，移动鼠标到不同窗口/控件：
   - 确认红色边框高亮不含阴影
   - 确认小控件（如按钮）不会被选中
   - 点击后边框跟随目标移动，目标在其他窗口上方时边框也在上方
5. 按 Ctrl+F3，拖拽框选桌面区域，确认边框静态显示
6. 按 Ctrl+F4，确认能截图并保存到保存路径
7. 对一个包含 DataMatrix 的画面截图，确认保存的文件名能自动识别为原始文件名
8. 按 Ctrl+F5 进行解码测试，确认弹窗展示元数据和数据块解码结果
9. 在 150% DPI 屏幕上重复上述步骤，确认坐标和边框位置正确
