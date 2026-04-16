# CLAUDE.md

此文件为 Claude Code (claude.ai/code) 提供本仓库的代码开发指南。

## 项目概述

这是一个通过二维码视觉传输文件的系统。将二进制文件编码为 DataMatrix 二维码网格并显示在屏幕上，接收方通过截屏或拍照解码二维码来重建原始文件。

解决方案包含三个组件：

1. **screen-file-sender** - .NET Framework 4.6.1 WPF 桌面应用，将文件编码为 DataMatrix 网格
2. **screen-file-receiver** - .NET Framework 4.8 WPF 桌面应用，使用 OpenCV 从图像解码 DataMatrix 网格

## 构建命令

### .NET 解决方案

```bash
# 构建整个解决方案
dotnet build screen-file-transmit.sln

# 构建特定项目
dotnet build screen-file-sender/screen-file-sender.csproj
dotnet build screen-file-receiver/screen-file-receiver.csproj

# 以调试模式运行
dotnet run --project screen-file-sender
dotnet run --project screen-file-receiver
```

## 架构

### 编码流程（发送端）

文件编码使用多步流程：

1. **分块** - 根据 DataMatrix 容量将文件分割成块（容量因版本而异，例如 144x144 可存储 1558 个 ASCII 字符）
2. **Base256 编码** - 每个块使用 Base256 编码，并添加 2 字符前缀表示网格位置（行/列使用 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz` base62 编码）
3. **色深** - 支持灰度或 RGB 模式，可配置位深度（1-8）。RGB 模式下每个颜色通道使用加法混色携带独立数据层
4. **网格布局** - DataMatrix 二维码按网格排列，计算最优布局以最大化屏幕利用率

#### 条码与二维码格式说明

**DataMatrix（数据二维码）**
- 格式：`DATA_MATRIX`，使用 ZXing 生成
- 内容格式：`[行坐标][列坐标][Base256数据块]`
- 坐标编码：使用 62 进制字符集 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz`
- 每个数据块前 2 个字符为网格坐标前缀，剩余为 Base256 编码的二进制数据
- 首个块 `(0,0)` 可选包含原始文件名：以 UTF-8 编码的文件名字符串 + `\0` 分隔符开头

**Code 128 元数据条码**
- 格式：`CODE_128`
- 位置：画面左侧，顺时针旋转 90 度
- 内容：`$` + 4 个字节的Base64字符串
  - 字节 0：高 4 位为行数，低 4 位为列数（`rowCount << 4 | colCount`）
  - 字节 1：最高位为彩色模式标志（`0x80`），高位第二位表示是否有密码，高位第三位表示是否开启纠错，低 6 位为色深度（`1-8`）
  - 字节 2：当前页码
  - 字节 3：总页数

**Code 128 文件名条码**
- 格式：`CODE_128`
- 位置：画面右侧，顺时针旋转 90 度
- 内容：文件名（不含扩展名）转拼音首字母大写，非中文转大写字母/数字，超长截断至 20 字符

**Code 128 时间戳条码**
- 格式：`CODE_128`
- 位置：画面右侧最边缘，顺时针旋转 90 度
- 内容：文件ID，GUID 的最后一段，前面加#

**侧边信息区域布局**
- 左侧（从左到右）：垂直文件名文本 → 垂直元数据条码
- 右侧（从左到右）：垂直文件名条码 → 垂直文件ID条码

关键编码类：
- `screen-file-sender/DataMatrixEncoder.cs` - 核心编码逻辑、颜色混合、网格计算

### 解码流程（接收端）

1. **图像处理** - 使用 OpenCV (OpenCvSharp) 检测轮廓并定位二维码
2. **排序** - 轮廓按大小排序，然后按 Y 坐标、X 坐标排序以确定网格顺序
3. **解码** - ZXing 库解码每个 DataMatrix。解码失败时触发重试，使用缩放和锐化处理
4. **重建** - 数据块按行/列前缀排序后写入输出文件

#### 交互界面

1. 主界面又文件名和添加按钮，添加后会增加到软件下方的表格中
2. 允许进行批量添加，支持拖拽添加
4. 表格有：复选框，图片文件名，文件ID，保存文件名，元数据信息，状态，解析进度，操作按钮
5. 根据元数据判断页码齐全后，底色变黄，复选框选中
6. 保存文件名要用户输入，其他列只读
8. 增加转换按钮，转换时更新表格中的进度和状态

关键解码类：
- `screen-file-receiver/DataMatrixReader.cs` - OpenCV 轮廓检测、ZXing 解码、重试逻辑

### DataMatrix 版本映射

发送端实现使用硬编码版本表映射尺寸到 ASCII 容量（例如 144x144 → 1558 字符）。

### 包管理

- **.NET**: 使用中央包管理，配置在 `Directory.Packages.props`。关键包：
  - `ZXing.Net` (0.16.9) - 二维码编码/解码
  - `OpenCvSharp4` - 接收端的计算机视觉
  - `PropertyChanged.Fody` - 自动实现 INotifyPropertyChanged
  - `Costura.Fody` - IL 合并，实现单文件部署

## 平台说明

- **screen-file-sender**: 目标框架 .NET Framework 4.6.1，使用 WPF
- **screen-file-receiver**: 目标框架 .NET Framework 4.8，x64 平台，需要 OpenCV 原生运行时
- 解决方案文件格式为 Visual Studio 2017+（格式版本 12.00）
