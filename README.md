# Screen File Transmit

通过二维码视觉传输文件的系统。将二进制文件编码为 DataMatrix 二维码网格并显示在屏幕上，接收方通过截屏或拍照解码二维码来重建原始文件。无需网络连接，纯视觉传输。

## 系统组成

- **screen-file-sender** — .NET Framework 4.6.1 WPF 桌面应用，将文件编码为 DataMatrix 网格并显示
- **screen-file-receiver** — .NET Framework 4.8 WPF 桌面应用，使用 OpenCV 从图像解码 DataMatrix 网格

## 构建

```bash
# 构建整个解决方案
dotnet build screen-file-transmit.sln

# 构建特定项目
dotnet build screen-file-sender/screen-file-sender.csproj
dotnet build screen-file-receiver/screen-file-receiver.csproj
```

## 使用说明

### 发送端 (screen-file-sender)

1. 运行 `screen-file-sender`。
2. 点击 **选择文件**，选择要传输的文件。
3. 配置参数：
   - **DataMatrix 尺寸**：每个二维码的密度（如 144×144），越大单码容量越高
   - **网格行列数**：自动计算或手动指定屏幕上的二维码排列
4. 点击 **生成**，屏幕上会显示二维码网格、左侧元数据条码和右侧文件名/文件ID条码。
5. 接收端拍摄或截图后，点击 **下一页** 继续发送下一页（如文件较大需要分页）。

> **提示**：生成后可全屏显示二维码网格，确保屏幕亮度足够，避免摩尔纹。

### 接收端 (screen-file-receiver)

1. 运行 `screen-file-receiver`。
2. 点击 **添加图片**，选择包含二维码网格的截图/照片（支持批量添加和拖拽）。
3. 表格中会显示解析状态：
   - **文件ID**：用于识别同一文件的不同页面
   - **保存文件名**：**需要手动输入**，作为最终输出文件名
   - **元数据信息**：显示行列数、色深、页码等
   - **状态 / 进度**：实时显示解码进度
4. 当某文件的所有页码齐全后，该文件行会变为**黄色背景**，且复选框自动选中。
5. 勾选要导出的文件，点击 **转换**，选择保存目录，即可重建原始文件。

> **提示**：
> - 可批量添加多张图片，程序会自动按文件ID归类。
> - 如果某张图片解码失败，可尝试重新截图后再次添加。

## 编码原理

1. **分块**：根据 DataMatrix 容量将文件分割成块
2. **网格布局**：DataMatrix 按网格排列，并配合侧边 Code 128 条码携带元数据、文件名拼音首字母和文件ID

## 关键技术栈

- **ZXing.Net** — 二维码编码/解码
- **OpenCvSharp4** — 接收端图像处理与轮廓检测
- **PropertyChanged.Fody** — 自动实现 `INotifyPropertyChanged`
- **Costura.Fody** — IL 合并，单文件部署

## 平台要求

- **screen-file-sender**：.NET Framework 4.6.1，Windows，WPF
- **screen-file-receiver**：.NET Framework 4.8，Windows x64，需要 OpenCV 原生运行时

## License

[LICENSE](LICENSE)
