import React, { useImperativeHandle, forwardRef, useRef, useState, useEffect } from 'react';
import BWIPJS from 'bwip-js';
import { Buffer } from 'buffer';

// eslint-disable-next-line react/display-name
const DataMatrix = forwardRef((props, ref) => {
    const canvasRef = useRef(null);
    const { options, pageInfo } = props;
    const [codeByteData, setCodeByteData] = useState('');
    const [selectedFile, setSelectedFile] = useState(null);
    var canvas = canvasRef.current;
    // Function to encode file to QR code
    function readFileChunk(file, offset, length) {
        const slice = file.slice(offset, offset + length);
        if (file) {
            const reader = new FileReader(); // Create a FileReader to read the file
            reader.onload = (event) => {
                const byteArray = new Uint8Array(event.target.result); // Convert file to byte array
                setCodeByteData(byteArray);
            };
            reader.readAsArrayBuffer(slice);
        }
    };


    // 字节数组转 Base64 字符串
    function byteArrayToBase64(byteArray) {
        return Buffer.from(byteArray).toString('base64');
    }
    function splitByteArray(byteArray, chunkSize) {
        const result = [];

        for (let i = 0; i < byteArray.length; i += chunkSize) {
            const chunk = byteArray.slice(i, i + chunkSize);
            result.push(chunk);
        }

        return result;
    }

    function show(page) {
        var code = options.code;
        var pageSize = code.codeCols * code.codeRows * pageInfo.colorCount;
        var chunkSize = code.codeByteCount;
        if (canvas) {
            const ctx = canvas.getContext('2d');
            ctx.clearRect(0, 0, canvas.width, canvas.height);
            canvas.width = (code.codeCols * (code.codeSize + 1) + 2) * options.scale * 2;
            canvas.height = (code.codeRows * (code.codeSize + 1) + 2) * options.scale * 2;
            
            // 填充黑色背景
            ctx.fillStyle = 'black';
            ctx.fillRect(0, 0, canvas.width, canvas.height);
 

            console.log('screen', { width: canvas.width, height: canvas.height, size: code.codeSize, scale: options.scale })
        }
        readFileChunk(selectedFile, chunkSize * pageSize * (page - 1), chunkSize * pageSize)
    }

    useEffect(() => {
        if (selectedFile && pageInfo && pageInfo.currentPage)
            show(pageInfo.currentPage)
    }, [pageInfo]);

    function binaryToHex(binary) {
        // 确保输入是 8 位二进制字符串
        if (!/^[01]{8}$/.test(binary)) {
            throw new Error('Input must be an 8-bit binary string');
        }

        // 将二进制字符串分成高 4 位和低 4 位
        const highNibble = binary.slice(0, 4);
        const lowNibble = binary.slice(4, 8);

        // 将每个部分转换为十六进制
        const hexHigh = parseInt(highNibble, 2).toString(16).toUpperCase();
        const hexLow = parseInt(lowNibble, 2).toString(16).toUpperCase();

        // 返回组合的十六进制字符串
        return hexHigh + hexLow;
    }
    function generateBinary( index, length) {
        // 确保参数有效
        if (length < 0 || length > 8 || index < 0 || index >= 8) {
            throw new Error('Length must be between 0 and 8, and index must be between 0 and 7.');
        }

        // 创建一个 8 位的数组，初始化为 '0'
        const binaryArray = Array(8).fill('0'); 

        // 设置前 length 位为 '0'，在 index 位设置为 '1'
        if (index < length) {
            binaryArray[index] = '1';
        } else { 
            throw new Error('Index must be greater than or equal to length.');
        }

        // 将数组转换为字符串
        return binaryArray.join('');
    }

  
    useEffect(() => {
        if (!codeByteData) return
        var code = options.code;

        var chunks = splitByteArray(codeByteData, code.codeByteCount);
        // 遍历每一段
        const mainCtx = canvas.getContext("2d");
        chunks.forEach((chunk, index) => {
            var colorCount = pageInfo.colorCount;
            var color = index % colorCount;
            var column =  Math.floor( index % (code.codeCols * colorCount) / code.codeCols);
            var row = Math.floor(index / (code.codeCols * colorCount));
            var left = (((code.codeSize + 1)) * column + 1) * options.scale * 2;
            var top = (((code.codeSize + 1)) * row + 1)* options.scale * 2;

            const base64Str = byteArrayToBase64(chunk)
            //console.log('base64', base64Str)
            var colorText = '000000';
            var colorBin = '';
            var rgbDepth = Math.floor(color / 3);
            if (colorCount > 1) {
                var rgb = color % 3;
                var totalDepth = colorCount / 3;
                var hex = binaryToHex(colorBin = generateBinary(rgbDepth, totalDepth))
                switch (rgb) {
                    case 0:
                        colorText = hex + '0000'
                        break;
                    case 1:
                        colorText = '00' + hex + '00'
                        break;
                    case 2:
                        colorText = '0000' + hex
                        break;
                }
            }
            console.log('print', { color, colorBin, colorText, row, column, left, top })


            const tempCanvas = document.createElement("canvas");
            BWIPJS.toCanvas(tempCanvas, {
                bcid: 'datamatrix',// 'datamatrix',    // 条码类型
                //format: 'rectangle',
                version: code.codeVersion,
                text: base64Str,      // 要编码的数据
                scale: options.scale,//options.scale,              // 放大倍数
                //height: 10,            // 条码高度
                includetext: false,     // 是否包含文本
                textxalign: 'center',  // 文本对齐方式
                // paddingleft:left,                // 设置整体边距，单位为像素
                // paddingtop: top,
                //backgroundcolor: 'FFFFFF',         // 设置背景颜色为白色
                barcolor: colorText,// '000000',               // 设置条码颜色为黑色
            })
            //mainCtx.globalCompositeOperation = 'xor';
            //drawImageWithOrMode(mainCtx, tempCanvas, left, top)
            mainCtx.globalCompositeOperation = 'lighter';
            mainCtx.drawImage(tempCanvas, left, top);
        });

    }, [codeByteData]); // Run when selectedFile changes


    // 暴露子组件的方法
    useImperativeHandle(ref, () => ({
        setSelectedFile,
    }));

    return <canvas ref={canvasRef} style={{ float: 'left' }} />;
});
DataMatrix.defaultProps = {
    options: {}
}
export default DataMatrix;