#!/usr/bin/env node
/**
 * Build and publish script for npm packages.
 *
 * Usage:
 *   node npm/publish.js [sender|receiver|all] [--dry-run|--pack]
 *
 * Modes:
 *   (default)  - Build, copy artifacts, and publish to npm
 *   --dry-run  - Run npm publish --dry-run (validate without uploading)
 *   --pack     - Build and create local .tgz tarball only (no publish)
 *
 * Steps:
 *   1. Build the .NET project in Release mode
 *   2. Copy build artifacts to npm/<package>/dist/
 *   3. Run npm publish / npm publish --dry-run / npm pack
 */
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');

const rootDir = path.resolve(__dirname, '..');

function copyDirectorySync(src, dest) {
  if (!fs.existsSync(dest)) {
    fs.mkdirSync(dest, { recursive: true });
  }
  const entries = fs.readdirSync(src, { withFileTypes: true });
  for (const entry of entries) {
    const srcPath = path.join(src, entry.name);
    const destPath = path.join(dest, entry.name);
    if (entry.isDirectory()) {
      copyDirectorySync(srcPath, destPath);
    } else {
      fs.copyFileSync(srcPath, destPath);
    }
  }
}

const packages = {
  sender: {
    dir: 'screen-file-sender',
    name: '@chang-code-hub/screen-file-sender',
    proj: 'screen-file-sender/screen-file-sender.csproj',
    sourceDir: 'screen-file-sender/bin/Release/net461',
    files: [
      'screen-file-sender.exe',
      'screen-file-sender.exe.config',
    ],
    directories: ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'ru'],
  },
  receiver: {
    dir: 'screen-file-receiver',
    name: '@chang-code-hub/screen-file-receiver',
    proj: 'screen-file-receiver/screen-file-receiver.csproj',
    sourceDir: 'screen-file-receiver/bin/Release/net48',
    files: [
      'screen-file-receiver.exe',
      'screen-file-receiver.exe.config',
      'OpenCvSharp.dll',
      'OpenCvSharp.Extensions.dll',
      'ReedSolomon.dll',
      'System.Buffers.dll',
      'System.Drawing.Common.dll',
      'System.Memory.dll',
      'System.Numerics.Vectors.dll',
      'System.Runtime.CompilerServices.Unsafe.dll',
      'zxing.dll',
      'zxing.presentation.dll',
    ],
    nativeFiles: {
      'dll/x64/OpenCvSharpExtern.dll': 'dll/x64/OpenCvSharpExtern.dll',
      'dll/x64/opencv_videoio_ffmpeg4130_64.dll': 'dll/x64/opencv_videoio_ffmpeg4130_64.dll',
    },
    directories: ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'ru'],
  },
};

function buildProject(projPath) {
  console.log(`Building ${projPath} ...`);
  execSync(`dotnet build "${path.join(rootDir, projPath)}" -c Release`, {
    cwd: rootDir,
    stdio: 'inherit',
  });
}

function copyFiles(pkgKey) {
  const pkg = packages[pkgKey];
  const distDir = path.join(rootDir, 'npm', pkg.dir, 'dist');

  if (fs.existsSync(distDir)) {
    fs.rmSync(distDir, { recursive: true });
  }
  fs.mkdirSync(distDir, { recursive: true });

  for (const file of pkg.files) {
    const src = path.join(rootDir, pkg.sourceDir, file);
    const dest = path.join(distDir, file);

    if (!fs.existsSync(src)) {
      console.error(`Missing build artifact: ${src}`);
      console.error('Please ensure the Release build succeeded.');
      process.exit(1);
    }

    fs.mkdirSync(path.dirname(dest), { recursive: true });
    fs.copyFileSync(src, dest);
    console.log(`Copied: ${file}`);
  }

  if (pkg.nativeFiles) {
    for (const [srcRel, destName] of Object.entries(pkg.nativeFiles)) {
      const src = path.join(rootDir, pkg.sourceDir, srcRel);
      const dest = path.join(distDir, destName);

      if (!fs.existsSync(src)) {
        console.error(`Missing native file: ${src}`);
        process.exit(1);
      }

      fs.mkdirSync(path.dirname(dest), { recursive: true });
      fs.copyFileSync(src, dest);
      console.log(`Copied native: ${destName}`);
    }
  }

  if (pkg.directories) {
    for (const dir of pkg.directories) {
      const src = path.join(rootDir, pkg.sourceDir, dir);
      const dest = path.join(distDir, dir);

      if (!fs.existsSync(src)) {
        console.error(`Missing directory: ${src}`);
        process.exit(1);
      }

      copyDirectorySync(src, dest);
      console.log(`Copied directory: ${dir}`);
    }
  }
}

function publishPackage(pkgKey, mode) {
  const pkg = packages[pkgKey];
  const pkgDir = path.join(rootDir, 'npm', pkg.dir);

  if (mode === 'pack') {
    console.log(`Packing ${pkg.name} ...`);
    execSync('npm pack', {
      cwd: pkgDir,
      stdio: 'inherit',
    });
  } else if (mode === 'dry-run') {
    console.log(`Publishing ${pkg.name} (dry-run) ...`);
    execSync('npm publish --access public --dry-run', {
      cwd: pkgDir,
      stdio: 'inherit',
    });
  } else {
    console.log(`Publishing ${pkg.name} ...`);
    execSync('npm publish --access public', {
      cwd: pkgDir,
      stdio: 'inherit',
    });
  }
}

function main() {
  const args = process.argv.slice(2);

  let target = 'all';
  let mode = 'publish';

  for (const arg of args) {
    if (arg === '--dry-run') {
      mode = 'dry-run';
    } else if (arg === '--pack') {
      mode = 'pack';
    } else if (!arg.startsWith('-')) {
      target = arg;
    }
  }

  const keys = target === 'all' ? Object.keys(packages) : [target];

  for (const key of keys) {
    if (!packages[key]) {
      console.error(`Unknown package: ${key}`);
      console.error(`Available: ${Object.keys(packages).join(', ')}`);
      console.error(`Flags: --dry-run, --pack`);
      process.exit(1);
    }

    console.log(`\n========== ${packages[key].name} [${mode}] ==========\n`);
    buildProject(packages[key].proj);
    copyFiles(key);
    publishPackage(key, mode);
  }

  console.log('\nDone!');
}

main();
