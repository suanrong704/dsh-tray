#!/usr/bin/env node
/**
 * dsh-tray 图标生成器
 *
 * 从「本机已安装的 DSH」读取官方 favicon.svg，渲染成 DeepSeek 蓝底 + 白色鲸鱼
 * 的多尺寸 .ico。官方图形不随本仓库分发，只在使用者自己的机器上即时生成。
 *
 * 用法：
 *   node tools/build-icon.js --out dist/dsh.ico [--preview dist/icon-preview.png]
 *   node tools/build-icon.js --favicon <favicon.svg 路径> --out dist/dsh.ico
 *
 * 找不到 favicon 或 sharp 时以非零状态退出，不会静默换用别的图形。
 */
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const BRAND = '#4D6BFE';
const SIZES = [16, 20, 24, 32, 48, 64, 256];

const argv = process.argv.slice(2);
function arg(name, def) {
  const i = argv.indexOf(name);
  return i >= 0 && argv[i + 1] ? argv[i + 1] : def;
}
const OUT = path.resolve(arg('--out', path.join(ROOT, 'dist', 'dsh.ico')));
const PREVIEW = arg('--preview', path.join(path.dirname(OUT), 'icon-preview.png'));
const FAVICON_ARG = arg('--favicon', process.env.DSH_FAVICON || null);

function dshHome() {
  return process.env.DSH_HOME || path.join(os.homedir(), '.dsh');
}

/** 本机 DSH 的官方 favicon.svg（不随仓库分发） */
function findFavicon() {
  if (FAVICON_ARG) {
    if (fs.existsSync(FAVICON_ARG)) return FAVICON_ARG;
    console.error('FAIL: --favicon 指定的文件不存在: ' + FAVICON_ARG);
    process.exit(1);
  }
  const profiles = path.join(dshHome(), 'profiles');
  const rel = path.join('node_modules', '@deepseek-ai', 'dsh-web-frontend', 'dist', 'favicon.svg');
  const candidates = [path.join(profiles, rel)];
  try {
    for (const d of fs.readdirSync(profiles)) candidates.push(path.join(profiles, d, rel));
  } catch (e) { /* profiles 不存在 */ }
  for (const c of candidates) {
    try { if (fs.existsSync(c)) return c; } catch (e) { /* ignore */ }
  }
  return null;
}

/** sharp：本项目自装优先，其次复用 DSH 的 profile 依赖树 */
function loadSharp() {
  const tries = ['sharp'];
  const profiles = path.join(dshHome(), 'profiles');
  tries.push(path.join(profiles, 'node_modules', 'sharp'));
  try {
    for (const d of fs.readdirSync(profiles)) tries.push(path.join(profiles, d, 'node_modules', 'sharp'));
  } catch (e) { /* ignore */ }
  for (const t of tries) {
    try { return require(t); } catch (e) { /* 继续找 */ }
  }
  return null;
}

/** 官方鲸鱼：把 SVG 里针对暗色模式的媒体查询覆盖成纯白填充 */
function whaleGlyphSvg(file) {
  const raw = fs.readFileSync(file, 'utf8');
  return Buffer.from(raw.replace(/<style>[\s\S]*?<\/style>/, '<style>path{fill:#ffffff;}</style>'));
}

function bgSvg(size) {
  const rx = Math.max(2, Math.round(size * 0.22));
  return Buffer.from(
    '<svg xmlns="http://www.w3.org/2000/svg" width="' + size + '" height="' + size + '">' +
    '<rect width="' + size + '" height="' + size + '" rx="' + rx + '" ry="' + rx +
    '" fill="' + BRAND + '"/></svg>'
  );
}

function buildIco(entries) {
  const header = Buffer.alloc(6);
  header.writeUInt16LE(0, 0);
  header.writeUInt16LE(1, 2);
  header.writeUInt16LE(entries.length, 4);
  const dir = Buffer.alloc(16 * entries.length);
  let offset = 6 + 16 * entries.length;
  entries.forEach((e, i) => {
    const b = i * 16;
    const dim = e.size >= 256 ? 0 : e.size;
    dir.writeUInt8(dim, b + 0);
    dir.writeUInt8(dim, b + 1);
    dir.writeUInt8(0, b + 2);
    dir.writeUInt8(0, b + 3);
    dir.writeUInt16LE(1, b + 4);
    dir.writeUInt16LE(32, b + 6);
    dir.writeUInt32LE(e.png.length, b + 8);
    dir.writeUInt32LE(offset, b + 12);
    offset += e.png.length;
  });
  return Buffer.concat([header, dir, ...entries.map((e) => e.png)]);
}

(async () => {
  const favicon = findFavicon();
  if (!favicon) {
    console.error('FAIL: 未找到本机 DSH 的 favicon.svg。');
    console.error('      查找位置: ' + path.join(dshHome(), 'profiles', '*', 'node_modules', '@deepseek-ai', 'dsh-web-frontend', 'dist', 'favicon.svg'));
    console.error('      请先安装 / 启动过 DeepSeek Harness，或用 --favicon <路径> 显式指定。');
    process.exit(1);
  }
  const sharp = loadSharp();
  if (!sharp) {
    console.error('FAIL: 未找到 sharp 模块（用于把 SVG 渲染成图标）。');
    console.error('      可在 DSH 的 profile 里找到它，或在本项目执行: npm i -D sharp');
    process.exit(1);
  }

  const glyph = whaleGlyphSvg(favicon);
  const entries = [];
  for (const size of SIZES) {
    const inner = Math.max(8, Math.round(size * 0.64));
    const mark = await sharp(glyph, { density: 384 })
      .resize(inner, inner, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png()
      .toBuffer();
    const png = await sharp(bgSvg(size))
      .composite([{ input: mark, gravity: 'center' }])
      .png()
      .toBuffer();
    entries.push({ size, png });
    if (size === 256) {
      try {
        fs.mkdirSync(path.dirname(PREVIEW), { recursive: true });
        fs.writeFileSync(PREVIEW, png);
      } catch (e) { /* 预览写不出不影响构建 */ }
    }
  }

  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, buildIco(entries));
  console.log('source: ' + favicon);
  console.log('OK ' + OUT + ' (' + fs.statSync(OUT).size + 'B, sizes=' + SIZES.join(',') + ')');
})().catch((e) => { console.error('FAIL: ' + e.message); process.exit(1); });
