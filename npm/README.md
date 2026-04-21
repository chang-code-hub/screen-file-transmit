# npm 发布说明

本目录包含将 .NET WPF 桌面应用打包并发布到 npm 的配置。通过 npm 分发 Windows 桌面应用，用户只需 `npm install -g` 即可获取，无需手动下载安装。

## 项目简介

这是一个通过二维码视觉传输文件的系统：

- **发送端** 将二进制文件编码为 DataMatrix 二维码网格并显示在屏幕上
- **接收端** 通过截屏或拍照解码二维码来重建原始文件

两端的通信不依赖网络，完全通过视觉方式完成。

## 包列表

| 包名 | 命令 | 别名 | 说明 |
|------|------|------|------|
| `@chang-code-hub/screen-file-sender` | `screen-file-sender` | `sfs` | 发送端，将文件编码为 DataMatrix 网格 |
| `@chang-code-hub/screen-file-receiver` | `screen-file-receiver` | `sfr` | 接收端，从图像解码 DataMatrix 网格 |

### 系统要求

| 包 | 操作系统 | .NET Framework |
|----|----------|----------------|
| sender | Windows (x64 / x86) | 4.6.1+ |
| receiver | Windows (x64) | 4.8+ |

## 安装与使用

```bash
# 全局安装发送端
npm install -g @chang-code-hub/screen-file-sender

# 全局安装接收端
npm install -g @chang-code-hub/screen-file-receiver
```

安装后通过 CLI 启动 WPF 桌面应用：

```bash
# 启动发送端
screen-file-sender
# 或简写
sfs

# 启动接收端
screen-file-receiver
# 或简写
sfr
```

> CLI 脚本会调用 `dist/` 目录下的 `.exe` 文件并以后台方式启动 GUI。

## 发布脚本

使用 `publish.js` 自动完成构建、复制产物、版本管理和发布：

```bash
# 发布所有包（自动递增 patch 版本）
node npm/publish.js all

# 发布单个包（自动递增 patch 版本）
node npm/publish.js sender
node npm/publish.js receiver

# 指定固定版本号发布（仅支持单个包）
node npm/publish.js sender 2.1.0
node npm/publish.js receiver 1.5.0

# 本地打包为 .tgz，不推送
node npm/publish.js sender --pack

# 模拟发布，验证但不上传
node npm/publish.js receiver --dry-run
```

### 发布流程

```
构建 .NET Release → 复制产物到 dist/ → 自动递增版本号
      → npm publish → git commit → git tag
```

### 版本号策略

- **自动模式**（默认）：对比本地和远程版本，以较大者为基线，自动递增 patch 位
- **固定模式**：通过第二个参数指定版本号，如 `node npm/publish.js sender 2.1.0`
- 同时发布多个包（`all`）时不允许指定固定版本号

### Git 自动提交

正式发布（非 `--dry-run` / `--pack`）成功后会自动执行：

1. `git add` 变更的 `package.json`
2. `git commit`（message 包含版本变化明细）
3. `git tag` 打标签，格式为 `sender-2.0.1`、`receiver-1.0.0`

## 手动发布

如需手动控制发布流程：

```bash
# 1. 构建 Release
dotnet build screen-file-sender/screen-file-sender.csproj -c Release
dotnet build screen-file-receiver/screen-file-receiver.csproj -c Release

# 2. 复制产物到 dist/
# screen-file-sender → npm/screen-file-sender/dist/
# screen-file-receiver → npm/screen-file-receiver/dist/

# 3. 进入包目录发布
cd npm/screen-file-sender && npm publish --access public
cd npm/screen-file-receiver && npm publish --access public
```

## 目录结构

```
npm/
├── publish.js                          # 发布脚本
├── README.md                           # 本文件
├── screen-file-sender/
│   ├── package.json                    # npm 包配置
│   ├── README.md                       # 包的说明文档
│   ├── bin/
│   │   └── screen-file-sender.js       # CLI 入口（Node.js 调用 .exe）
│   └── dist/                           # 构建产物（.exe 及依赖，发布前由脚本复制）
│       ├── screen-file-sender.exe
│       ├── screen-file-sender.exe.config
│       └── de, en, es, fr, it, ja, ko, ru/
└── screen-file-receiver/
    ├── package.json
    ├── README.md
    ├── bin/
    │   └── screen-file-receiver.js     # CLI 入口
    └── dist/                           # 构建产物
        ├── screen-file-receiver.exe
        ├── OpenCvSharp.dll
        ├── zxing.dll
        ├── dll/x64/OpenCvSharpExtern.dll
        ├── dll/x64/opencv_videoio_ffmpeg4130_64.dll
        └── de, en, es, fr, it, ja, ko, ru/
```

## 注意事项

- `dist/` 目录由 `publish.js` 在构建时自动生成，无需手动维护
- 发布前请确保已登录 npm 并拥有 `@chang-code-hub` 组织的发布权限
- `.gitignore` 已排除 `dist/` 和 `.tgz` 文件，避免将二进制产物提交到 git
