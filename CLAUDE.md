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
2. **Base64 编码** - 每个块使用 Base64 编码，并添加 2 字符前缀表示网格位置（行/列使用 `0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz` base62 编码）
3. **色深** - 支持灰度或 RGB 模式，可配置位深度（1-8）。RGB 模式下每个颜色通道使用加法混色携带独立数据层
4. **网格布局** - DataMatrix 二维码按网格排列，计算最优布局以最大化屏幕利用率

关键编码类：
- `screen-file-sender/DataMatrixEncoder.cs` - 核心编码逻辑、颜色混合、网格计算
- `qr-file-transfer-sender/src/DataMatrix.jsx` - 基于浏览器的编码，使用 bwip-js 库

### 解码流程（接收端）

1. **图像处理** - 使用 OpenCV (OpenCvSharp) 检测轮廓并定位二维码
2. **排序** - 轮廓按大小排序，然后按 Y 坐标、X 坐标排序以确定网格顺序
3. **解码** - ZXing 库解码每个 DataMatrix。解码失败时触发重试，使用缩放和锐化处理
4. **重建** - 数据块按行/列前缀排序后写入输出文件

关键解码类：
- `screen-file-receiver/DataMatrixReader.cs` - OpenCV 轮廓检测、ZXing 解码、重试逻辑

### DataMatrix 版本映射

发送端实现使用硬编码版本表映射尺寸到 ASCII 容量（例如 144x144 → 1558 字符）。每个二维码的实际字节容量计算公式为 `Math.floor((capacity * 3) / 4)`（考虑 Base64 开销）。

### 包管理

- **.NET**: 使用中央包管理，配置在 `Directory.Packages.props`。关键包：
  - `ZXing.Net` (0.16.9) - 二维码编码/解码
  - `OpenCvSharp4` - 接收端的计算机视觉
  - `PropertyChanged.Fody` - 自动实现 INotifyPropertyChanged
  - `Costura.Fody` - IL 合并，实现单文件部署

- **Node**: 使用 pnpm，锁文件位于 `qr-file-transfer-sender/pnpm-lock.yaml`

## 平台说明

- **screen-file-sender**: 目标框架 .NET Framework 4.6.1，使用 WPF + Windows Forms 混合
- **screen-file-receiver**: 目标框架 .NET Framework 4.8，x64 平台，需要 OpenCV 原生运行时
- 解决方案文件格式为 Visual Studio 2017+（格式版本 12.00）
