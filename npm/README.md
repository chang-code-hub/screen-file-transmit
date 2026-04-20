# npm 发布说明

本目录包含将 .NET WPF 桌面应用打包并发布到 npm 的配置。

## 包列表

| 包名 | 命令 | 说明 |
|------|------|------|
| `@chang-code-hub/screen-file-sender` | `screen-file-sender` / `sfs` | 发送端，将文件编码为 DataMatrix 网格 |
| `@chang-code-hub/screen-file-receiver` | `screen-file-receiver` / `sfr` | 接收端，从图像解码 DataMatrix 网格 |

## 发布脚本

使用 `publish.js` 自动完成构建、复制产物和发布：

```bash
# 正常发布（构建 + 复制 + npm publish）
node npm/publish.js all
node npm/publish.js sender
node npm/publish.js receiver

# 测试模式：本地打包为 .tgz，不推送
node npm/publish.js all --pack
node npm/publish.js sender --pack

# 测试模式：模拟发布，验证但不上传
node npm/publish.js all --dry-run
node npm/publish.js receiver --dry-run
```

## 手动发布步骤

```bash
# 1. 构建 Release
dotnet build screen-file-sender/screen-file-sender.csproj -c Release
dotnet build screen-file-receiver/screen-file-receiver.csproj -c Release

# 2. 复制产物到 dist/
# screen-file-sender: screen-file-sender.exe
# screen-file-receiver: screen-file-receiver.exe, screen-file-receiver.exe.config,
#                       OpenCvSharpExtern.dll, opencv_videoio_ffmpeg4130_64.dll

# 3. 进入包目录发布
cd npm/screen-file-sender && npm publish --access public
cd npm/screen-file-receiver && npm publish --access public
```

## 包结构

```
npm/
├── publish.js                          # 发布脚本
├── screen-file-sender/
│   ├── package.json                    # 包配置
│   ├── README.md
│   └── bin/
│       └── screen-file-sender.js       # CLI 入口
├── screen-file-receiver/
│   ├── package.json
│   ├── README.md
│   └── bin/
│       └── screen-file-receiver.js     # CLI 入口
```

运行时 CLI 脚本会调用 `dist/` 目录下的 `.exe` 文件（由 publish.js 在构建时复制）。
