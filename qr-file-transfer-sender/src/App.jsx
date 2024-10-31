import React, { useState, useRef, useEffect } from 'react';
import DataMatrix from './DataMatrix'
import './App.css'

function App() {
  const [selectedFile, setSelectedFile] = useState(null);
  const [fileSize, setFileSize] = useState(null);
  const [isDragging, setIsDragging] = useState(false); // State to track drag status
  const [colorDepth, setColorDepth] = useState(1); // State for QR code color depth
  const [codeScale, setCodeScale] = useState(1); // State for QR code color depth
  const codeRef = useRef(null); // Reference for QR code area
  const dmRef = useRef(null); // Reference for QR code area
  const [codeDimensions, setCodeDimensions] = useState({ width: 0, height: 0 }); // State for QR code dimensions
  const [pageInfo, setPageInfo] = useState({ totalPage: 0, currentPage: 0}); // State for QR code color depth


  const handleFileChange = (event) => {
    const file = event.target.files[0];
    if (file) {
      setFileData(file);
    }
  };

  const handleClearSelection = () => {
    setSelectedFile(null);
  };

  const startEncoding = () => {
    if(!selectedFile) return;
    var code = codeDimensions;
    var chunkSize = code.codeByteCount;
    var pageSize = code.codeCols * code.codeRows;

    dmRef.current.setSelectedFile(selectedFile);
    setPageInfo({
      pageSize: chunkSize * pageSize,
      colorCount: colorDepth * 3,
      currentPage : 1
    }) 
  }


  // Set file data and create object URL
  const setFileData = (file) => {
    setSelectedFile(file);
    setFileSize(file.size); // Get the file size
  };

  // Handle drag over event
  const handleDragOver = (event) => {
    event.preventDefault(); // Prevent default behavior
    event.stopPropagation(); // Stop the event from bubbling
    setIsDragging(true); // Update drag status
  };

  // Handle drag leave event
  const handleDragLeave = (event) => {
    event.preventDefault(); // Prevent default behavior
    event.stopPropagation(); // Stop the event from bubbling
    setIsDragging(false); // Update drag status
  };

  // Handle drop event
  const handleDrop = (event) => {
    event.preventDefault(); // Prevent default behavior
    event.stopPropagation(); // Stop the event from bubbling
    const file = event.dataTransfer.files[0]; // Get the first file from the dropped files
    if (file) {
      setFileData(file);
    }
    setIsDragging(false); // Update drag status
  };


  // 合并的DataMatrix版本信息，包括尺寸和ASCII字符容量
  const dataMatrixVersions = {
    "10x10": { size: 10, capacity: 3 },
    "12x12": { size: 12, capacity: 5 },
    "14x14": { size: 14, capacity: 8 },
    "16x16": { size: 16, capacity: 12 },
    "18x18": { size: 18, capacity: 18 },
    "20x20": { size: 20, capacity: 22 },
    "22x22": { size: 22, capacity: 30 },
    "24x24": { size: 24, capacity: 36 },
    "26x26": { size: 26, capacity: 44 },
    "32x32": { size: 32, capacity: 62 },
    "36x36": { size: 36, capacity: 86 },
    "40x40": { size: 40, capacity: 114 },
    "44x44": { size: 44, capacity: 144 },
    "48x48": { size: 48, capacity: 174 },
    "52x52": { size: 52, capacity: 204 },
    "64x64": { size: 64, capacity: 280 },
    "72x72": { size: 72, capacity: 368 },
    "80x80": { size: 80, capacity: 456 },
    "88x88": { size: 88, capacity: 560 },
    "96x96": { size: 96, capacity: 644 },
    "104x104": { size: 104, capacity: 793 },
    "120x120": { size: 120, capacity: 1050 },
    "132x132": { size: 132, capacity: 1304 },
    "144x144": { size: 144, capacity: 1558 }
  };


  // 计算每个版本能容纳的 byte[] 数量
  function calcBase64ByteLength(capacity){
    // 计算能容纳的原始字节数
    const originalBytes = Math.floor((capacity * 3) / 4); // 反推原始字节数

    // 计算能容纳的 byte[] 数量
    const byteCount = Math.floor(originalBytes / 1); // 假设每个 byte[] 为 1 字节

    // 将结果存储回版本信息中
    return byteCount;
  }
  // 计算最佳版本和可以放置的行和列
  function calculateDataMatrix(screenWidth, screenHeight) {
    const pixelPerPoint = codeScale * 2; // 每个点占2个像素
    const spacing = codeScale * 2; // 间距为4个像素

    let bestVersion = null;
    let maxRows = 0;
    let maxCols = 0;
    let maxAsciiCount = 0;
    let maxByteCount = 0;
    let codeByteCount = 0;
    let codeCapacity = 0;
    let codeSize = 0;

    for (const [version, { size, capacity }] of Object.entries(dataMatrixVersions)) {
      // 计算实际的二维码宽度和高度（像素）
      const qrWidth = size * pixelPerPoint; // 二维码的宽度（像素）
      const qrHeight = size * pixelPerPoint; // 二维码的高度（像素）
      
      // 计算每个二维码占用的总空间
      const totalWidth = qrWidth + spacing; // 每个二维码占用的宽度（包括间距）
      const totalHeight = qrHeight + spacing; // 每个二维码占用的高度（包括间距）

      // 计算能放置的行和列
      const cols = Math.floor((screenWidth - spacing) / totalWidth);
      const rows = Math.floor((screenHeight - spacing) / totalHeight);

      var byteCount = calcBase64ByteLength(capacity)
      var totalCapacity = rows * cols * byteCount;
      // 更新最佳版本和数量
      if (totalCapacity > maxByteCount) {
        maxByteCount = totalCapacity;
        maxRows = rows;
        maxCols = cols;
        codeSize = size ;
        bestVersion = version;
        codeByteCount = byteCount;
        codeCapacity = capacity;
      }
    }

    return {
      bestVersion: bestVersion,
      maxRows: maxRows,
      maxCols: maxCols,
      maxAsciiCount: maxAsciiCount,
      codeSize: codeSize,
      codeByteCount: codeByteCount,
      codeCapacity: codeCapacity
    };
  }
 
  // Effect to get code dimensions after rendering
  useEffect(() => {
    const updateDimensions = () => {
      if (codeRef.current) {
        const { offsetWidth, offsetHeight } = codeRef.current; // Get dimensions

        const result = calculateDataMatrix(offsetWidth, offsetHeight);

        console.log(`最佳版本: ${result.bestVersion}, 能放置的行: ${result.maxRows}, 列: ${result.maxCols}, 能放下的 byte[] 数量: ${result.codeByteCount}`)
        setCodeDimensions({
          width: offsetWidth,
          height: offsetHeight,
          codeVersion: result.bestVersion,
          codeRows: result.maxRows,
          codeCols: result.maxCols,
          codeSize: result.codeSize,
          maxAsciiCount: result.maxAsciiCount,
          codeCapacity: result.codeCapacity,
          codeByteCount: result.codeByteCount,
        }); // Update dimensions state
      }
    }

    // Set initial dimensions
    updateDimensions();

    // Resize event listener
    window.addEventListener('resize', updateDimensions);
    // Cleanup listener on unmount
    return () => {
      window.removeEventListener('resize', updateDimensions);
    };
  }, [selectedFile]); // Run when selectedFile changes


  return (
    <div style={{ display: 'flex', flexDirection: 'column', height: '100vh', width: '100%' }}>
      {/* Top area with drag-and-drop support */}
      <div
        style={{
          padding: '10px',
          backgroundColor: '#f0f0f0',
          display: 'flex',
          alignItems: 'center',
          border: '2px dashed #ccc', // Visual cue for drag area
          borderRadius: '5px',
          cursor: 'pointer' // Change cursor to indicate drag area
        }}
        onDragOver={handleDragOver} // Handle drag over event
        onDragLeave={handleDragLeave} // Handle drag leave event
        onDrop={handleDrop} // Handle drop event
      >
        <input type="file"
          onChange={handleFileChange} />

        <div style={{ marginLeft: '10px' }}>
          {selectedFile ? `Size: ${(fileSize / 1024).toFixed(2)} KB` : ''} {/* Display file size in KB */}
        </div>

        <button onClick={handleClearSelection} style={{ marginLeft: 'auto', padding: '5px 10px' }}>
          Clear
        </button>


        {/* Dropdown for Color Depth */}
        <label style={{ marginLeft: '10px' }}>Color Depth:</label>
        <select
          value={colorDepth}
          onChange={(e) => setColorDepth(e.target.value)}
          style={{ marginLeft: '10px', padding: '5px' }}
        >
          {[1, 2, 3, 4, 5, 6, 7, 8].map((depth) => (
            <option key={depth} value={depth}>{depth}</option> 
          ))}
        </select>
        <label style={{ marginLeft: '10px' }}>Code Scale:</label>
        <select
          value={codeScale}
          onChange={(e) => setCodeScale(e.target.value)}
          style={{ marginLeft: '10px', padding: '5px' }}
        >
          {[1, 2, 3, 4, 5].map((scale) => (
            <option key={scale} value={scale}>{scale}</option> 
          ))}
        </select>


        <label style={{ marginLeft: '10px' }}>Dimensions:</label>
        <div
          style={{ marginLeft: '10px', padding: '5px' }}>
          {codeDimensions.width} * {codeDimensions.height}px {/* Display dimensions */}
        </div>

        <button onClick={startEncoding} style={{ marginLeft: '10px', padding: '5px 10px' }}>
          Encode to QR Code
        </button>
      </div>
      

      {/* Drag-and-drop message */}
      {isDragging && (
        <div style={{
          position: 'absolute',
          top: '20%',
          left: '50%',
          transform: 'translate(-50%, -50%)',
          backgroundColor: 'rgba(255, 255, 255, 0.8)',
          border: '2px dashed #666',
          borderRadius: '5px',
          padding: '10px',
          textAlign: 'center',
          zIndex: 10,
        }}>
          Drop your file here
        </div>
      )}


      {/* QR Code Display */}
      <div style={{ flex: 1, justifyContent: 'center', alignItems: 'center' }} ref={codeRef}>
  
          <div style={{ textAlign: 'center', height: '100%' }} > {/* Attach ref to the QR code container */}
            <DataMatrix ref={dmRef} pageInfo={pageInfo} options={{ scale: codeScale, code: codeDimensions }} size={256} /> {/* Render the QR code */}
          </div>
      </div>
    </div>
  );
}

export default App
