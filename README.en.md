# Screen File Transmit

[中文](README.md) | English

GitHub: [chang-code-hub/screen-file-transmit](https://github.com/chang-code-hub/screen-file-transmit)

A system for visually transmitting files via QR codes. Binary files are encoded into a DataMatrix QR code grid and displayed on screen; the receiver decodes the QR codes from screenshots or photos to reconstruct the original file. No network connection required — purely visual transmission.

## System Components

- **screen-file-sender** — .NET Framework 4.6.1 WPF desktop app, encodes files into a DataMatrix grid and displays it
- **screen-file-receiver** — .NET Framework 4.8 WPF desktop app, uses OpenCV to decode DataMatrix grids from images

## Build

```bash
# Build the entire solution
dotnet build screen-file-transmit.sln

# Build a specific project
dotnet build screen-file-sender/screen-file-sender.csproj
dotnet build screen-file-receiver/screen-file-receiver.csproj
```

## Usage

### Sender (screen-file-sender)

1. Run `screen-file-sender`.
2. Click **Select File** and choose the file to transmit.
3. Configure parameters:
   - **DataMatrix Size**: Density of each QR code (e.g. 144×144); larger means higher capacity per code
   - **Grid Rows/Columns**: Automatically calculated or manually specified arrangement of QR codes on screen
4. Click **Generate**. The screen will display the QR code grid, metadata barcode on the left, and filename/file ID barcode on the right.
5. After the receiver takes a photo or screenshot, click **Next Page** to continue sending the next page (if the file is large and requires paging).

> **Tip**: After generation, display the QR code grid in full screen, ensure sufficient screen brightness, and avoid moiré patterns.

### Receiver (screen-file-receiver)

1. Run `screen-file-receiver`.
2. Click **Add Image** and select screenshots/photos containing the QR code grid (batch add and drag-and-drop are supported).
3. The table will show parsing status:
   - **File ID**: Used to identify different pages of the same file
   - **Save Filename**: **Must be entered manually**, serves as the final output filename
   - **Metadata Info**: Displays row/column count, color depth, page number, etc.
   - **Status / Progress**: Real-time decoding progress
4. Check the files to export, click **Convert**, select the save directory, and the original file will be reconstructed.

> **Tips**:
> - Multiple images can be added in batch; the program will automatically group them by file ID.
> - If an image fails to decode, try taking a new screenshot and adding it again.

## Encoding Principles

1. **Chunking**: Files are split into chunks based on DataMatrix capacity
2. **Grid Layout**: DataMatrix codes are arranged in a grid, accompanied by side Code 128 barcodes carrying metadata, filename pinyin initials, and file ID

## Key Tech Stack

- **ZXing.Net** — QR code encoding/decoding
- **OpenCvSharp4** — Image preprocessing on the receiver side

## Platform Requirements

- **screen-file-sender**: .NET Framework 4.6.1, Windows, WPF
- **screen-file-receiver**: .NET Framework 4.8, Windows x64, requires OpenCV native runtime

## License

[LICENSE](LICENSE)
