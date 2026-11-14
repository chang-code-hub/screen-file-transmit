#!/usr/bin/env node
/**
 * Build and publish script for npm packages.
 *
 * Usage:
 *   node npm/publish.js [sender|receiver|all] [version] [--dry-run|--pack]
 *
 * Arguments:
 *   sender|receiver|all  - Package to publish (default: all)
 *   version              - Optional fixed version (e.g. 2.1.0). If omitted, auto-bumps patch.
 *
 * Modes:
 *   (default)  - Build, copy artifacts, and publish to npm
 *   --dry-run  - Run npm publish --dry-run (validate without uploading)
 *   --pack     - Build and create local .tgz tarball only (no publish)
 *
 * Steps:
 *   1. Resolve the final version (fixed or auto-bumped patch)
 *   2. Write AssemblyVersion / AssemblyFileVersion / AssemblyInformationalVersion
 *      into each project's Properties/AssemblyInfo.cs (these projects set
 *      GenerateAssemblyInfo=false, so the SDK's Version property alone has no
 *      effect — the attributes must be written directly)
 *   3. Build the .NET projects in Release mode
 *   4. Copy build artifacts to npm/<package>/bin/
 *   5. Run npm publish / npm publish --dry-run / npm pack
 *   6. Git commit and tag (publish mode only)
 */
const fs = require('fs');
const path = require('path');
const { execSync } = require('child_process');
const { platform } = require('os');

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
    assemblyInfo: 'screen-file-sender/Properties/AssemblyInfo.cs',
    sourceDir: 'screen-file-sender/bin/Release',
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
    assemblyInfo: 'screen-file-receiver/Properties/AssemblyInfo.cs',
    sourceDir: 'screen-file-receiver/bin/Release',
    files: [
      'screen-file-receiver.exe',
      'screen-file-receiver.exe.config'
    ],
    nativeFiles: {
    },
    directories: ['de', 'en', 'es', 'fr', 'it', 'ja', 'ko', 'ru'],
  },
};

// Parse a SemVer-ish version into a 4-part numeric version for
// AssemblyVersion / AssemblyFileVersion (e.g. "2.2.37" -> "2.2.37.0").
// Any non-numeric prerelease/build suffix is dropped.
function toFourPartVersion(version) {
  const numeric = version.split('-')[0].split('+')[0];
  const parts = numeric.split('.').map(p => parseInt(p, 10));
  while (parts.length < 4) parts.push(0);
  return parts.slice(0, 4).join('.');
}

// Write AssemblyVersion / AssemblyFileVersion / AssemblyInformationalVersion
// into the project's AssemblyInfo.cs, replacing any previously injected values.
// This is required because these projects set GenerateAssemblyInfo=false, which
// disables the SDK's automatic version-attribute generation entirely.
function writeAssemblyVersion(pkgKey, version) {
  const pkg = packages[pkgKey];
  const assemblyInfoPath = path.join(rootDir, pkg.assemblyInfo);

  let content = fs.readFileSync(assemblyInfoPath, 'utf8');

  const fourPart = toFourPartVersion(version);
  const injectionBlock =
    `// Version injected by npm/publish.js at publish time (from the npm package version).\r\n` +
    `// AssemblyVersion / AssemblyFileVersion need a 4-part numeric version; AssemblyInformationalVersion keeps the original SemVer format.\r\n` +
    `[assembly: AssemblyVersion("${fourPart}")]\r\n` +
    `[assembly: AssemblyFileVersion("${fourPart}")]\r\n` +
    `[assembly: AssemblyInformationalVersion("${version}")]\r\n`;

  // Remove any previously injected block (from a marker comment through the
  // AssemblyInformationalVersion attribute), then append the fresh block.
  const injectionPattern =
    /\/\/ Version injected by npm\/publish\.js[\s\S]*?\[assembly: AssemblyInformationalVersion\("[^"]*"\)\]\r?\n/g;

  content = content.replace(injectionPattern, '');

  // Ensure the file ends with a newline before appending.
  if (!content.endsWith('\n')) {
    content += '\r\n';
  }

  content = content.replace(/\s*$/, '\r\n') + injectionBlock;

  fs.writeFileSync(assemblyInfoPath, content);
  console.log(`Injected version ${version} into ${pkg.assemblyInfo}`);
}

function buildProject() {
  console.log(`Cleaning ...`);
  execSync(`if exist "screen-file-receiver\\bin\\Release" rd /s /q "screen-file-receiver\\bin\\Release"`, {
    cwd: rootDir,
    stdio: 'inherit',
  });
  execSync(`if exist "screen-file-sender\\bin\\Release" rd /s /q "screen-file-sender\\bin\\Release"`, {
    cwd: rootDir,
    stdio: 'inherit',
  });
  console.log(`Building ...`);
  execSync(`msbuild  /t:Clean,Build /p:Configuration=Release`, {
    cwd: rootDir,
    stdio: 'inherit',
  });
}

function copyFiles(pkgKey) {
  const pkg = packages[pkgKey];
  const distDir = path.join(rootDir, 'npm', pkg.dir, 'bin');

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

function getRemoteVersion(pkgName) {
  try {
    const version = execSync(`npm view "${pkgName}" version`, {
      encoding: 'utf8',
      stdio: ['pipe', 'pipe', 'ignore'],
    });
    return version.trim();
  } catch {
    return null;
  }
}

function resolveVersion(pkgKey, fixedVersion) {
  const pkg = packages[pkgKey];
  const pkgDir = path.join(rootDir, 'npm', pkg.dir);
  const pkgJsonPath = path.join(pkgDir, 'package.json');

  const pkgJson = JSON.parse(fs.readFileSync(pkgJsonPath, 'utf8'));
  const localVersion = pkgJson.version;

  let newVersion;
  if (fixedVersion) {
    newVersion = fixedVersion;
    console.log(`Using fixed version: ${newVersion}`);
  } else {
    const remoteVersion = getRemoteVersion(pkg.name);
    let baseVersion = localVersion;
    if (remoteVersion && remoteVersion !== localVersion) {
      const localParts = localVersion.split('.').map(Number);
      const remoteParts = remoteVersion.split('.').map(Number);

      const localNum = localParts[0] * 1_000_000 + localParts[1] * 1_000 + localParts[2];
      const remoteNum = remoteParts[0] * 1_000_000 + remoteParts[1] * 1_000 + remoteParts[2];

      if (remoteNum > localNum) {
        baseVersion = remoteVersion;
        console.log(`Remote version ${remoteVersion} is ahead of local ${localVersion}, using remote as base.`);
      }
    }

    const parts = baseVersion.split('.').map(Number);
    parts[2] += 1;
    newVersion = parts.join('.');
  }

  let changed = false;
  if (newVersion !== localVersion) {
    pkgJson.version = newVersion;
    fs.writeFileSync(pkgJsonPath, JSON.stringify(pkgJson, null, 2) + '\n');
    console.log(`Bumped version: ${localVersion} -> ${newVersion}`);
    changed = true;
  } else {
    console.log(`Version already at ${newVersion}`);
  }

  return { pkgKey, oldVersion: localVersion, newVersion, changed, dir: pkg.dir, name: pkg.name };
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

function gitCommitAndTag(changes, mode) {
  if (mode !== 'publish' || changes.length === 0) {
    return;
  }

  const paths = changes.map(c => path.join('npm', c.dir, 'package.json'));
  execSync(`git add ${paths.map(p => `"${p}"`).join(' ')}`, {
    cwd: rootDir,
    stdio: 'inherit',
  });

  const lines = ['publish: bump version', ''];
  for (const c of changes) {
    lines.push(`- ${c.name}: ${c.oldVersion} → ${c.newVersion}`);
  }
  const msg = lines.join('\n');

  execSync('git commit -F -', {
    cwd: rootDir,
    stdio: ['pipe', 'inherit', 'inherit'],
    input: msg,
  });

  for (const c of changes) {
    const tag = `${c.pkgKey}-${c.newVersion}`;
    execSync(`git tag "${tag}"`, { cwd: rootDir, stdio: 'inherit' });
    console.log(`Tagged: ${tag}`);
  }
}

function main() {
  const args = process.argv.slice(2);

  let target = 'all';
  let fixedVersion = null;
  let mode = 'publish';

  const posArgs = [];
  for (const arg of args) {
    if (arg === '--dry-run') {
      mode = 'dry-run';
    } else if (arg === '--pack') {
      mode = 'pack';
    } else if (!arg.startsWith('-')) {
      posArgs.push(arg);
    }
  }

  if (posArgs.length >= 1) {
    target = posArgs[0];
  }
  if (posArgs.length >= 2) {
    fixedVersion = posArgs[1];
  }

  if (target === 'all' && fixedVersion) {
    console.error('Cannot specify a fixed version when publishing all packages.');
    process.exit(1);
  }

  const keys = target === 'all' ? Object.keys(packages) : [target];
  for (const key of keys) {
    if (!packages[key]) {
      console.error(`Unknown package: ${key}`);
      console.error(`Available: ${Object.keys(packages).join(', ')}`);
      console.error(`Flags: --dry-run, --pack`);
      process.exit(1);
    }
  }

  // Resolve the final version for each package first, then inject it into
  // AssemblyInfo.cs and build — so the compiled exe carries the real npm
  // package version (shown in the UI and via `sfr -v` / `sfs -v`).
  const changes = [];
  for (const key of keys) {
    const bumpResult = resolveVersion(key, fixedVersion);
    writeAssemblyVersion(key, bumpResult.newVersion);
    changes.push(bumpResult);
  }

  buildProject();

  for (const key of keys) {
    console.log(`\n========== ${packages[key].name} [${mode}] ==========\n`);
    copyFiles(key);
    publishPackage(key, mode);
  }

  gitCommitAndTag(changes.filter(c => c.changed), mode);

  console.log('\nDone!');
}

main();
