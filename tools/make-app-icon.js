#!/usr/bin/env node
/**
 * 生成 dsh-tray 的 exe 文件图标 assets/icon-app.ico
 *
 * 这是本项目**原创**的图形（胖鲸），MIT 许可，可随二进制自由分发；
 * 与 DeepSeek 的官方标识无关。托盘图标不走这里 —— 它在运行时从使用者
 * 本机的 DSH 读取官方 favicon 渲染。
 *
 * 维护者专用：只在需要修改图标设计时运行。
 * 用法：node tools/make-app-icon.js
 * 依赖：sharp（可从本机 DSH 的 profile 依赖树里复用）
 */
const fs = require('fs');
const os = require('os');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'assets', 'icon-app.ico');
const PREVIEW = path.join(ROOT, 'assets', 'icon-app-preview.png');
const SIZES = [16, 20, 24, 32, 48, 64, 256];
const SCALE = 0.96;

function loadSharp() {
  const tries = ['sharp'];
  const home = process.env.DSH_HOME || path.join(os.homedir(), '.dsh');
  const profiles = path.join(home, 'profiles');
  tries.push(path.join(profiles, 'node_modules', 'sharp'));
  try {
    for (const d of fs.readdirSync(profiles)) tries.push(path.join(profiles, d, 'node_modules', 'sharp'));
  } catch (e) { /* 没有 DSH 也无妨，只要本仓库装了 sharp */ }
  for (const t of tries) {
    try { return require(t); } catch (e) { /* 继续找 */ }
  }
  return null;
}

// ── 图形定义：胖鲸（厚身 + 浅色腹部 + 眼睛 + 笑脸 + 浮空喷水 + 尾鳍）──
const ART = `<defs>
  <linearGradient id="blue" x1="0.1" y1="0" x2="0.7" y2="1">
    <stop offset="0" stop-color="#6E8CFF"/><stop offset="1" stop-color="#2638C8"/></linearGradient>
  <linearGradient id="pale" x1="0" y1="0" x2="0.3" y2="1">
    <stop offset="0" stop-color="#EAF0FF"/><stop offset="1" stop-color="#C3D0FF"/></linearGradient>
</defs>
<g transform="translate(${(100 - 100 * SCALE) / 2},${(100 - 100 * SCALE) / 2}) scale(${SCALE})">
  <path fill="url(#blue)" d="M8 56
    C8 36 24 24 46 24
    C64 24 76 33 80 45
    L99 35 C94 49 89 56 84 60 C90 65 94 72 99 82
    L78 68 C72 76 60 84 44 84
    C22 84 8 74 8 56 Z"/>
  <path fill="url(#pale)" d="M12 60 C17 76 30 84 46 84 C60 84 70 78 76 68
    C68 76 56 80 44 80 C28 80 17 72 12 60 Z"/>
  <path d="M33 25 L33 15" stroke="url(#blue)" stroke-width="5" stroke-linecap="round"/>
  <path d="M33 19 C27 15 23 11 21 6 C28 8 32 12 35 16 Z" fill="url(#blue)"/>
  <path d="M33 19 C39 15 43 11 45 6 C38 8 34 12 31 16 Z" fill="url(#blue)"/>
  <circle cx="34" cy="49" r="5.2" fill="#0A1870"/>
  <path d="M17 63 C25 69 35 71 46 69" fill="none" stroke="#0A1870" stroke-width="4" stroke-linecap="round"/>
</g>`;

const svg = (size) => Buffer.from(
  `<svg xmlns="http://www.w3.org/2000/svg" width="${size}" height="${size}" viewBox="0 0 100 100">${ART}</svg>`);

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
  const sharp = loadSharp();
  if (!sharp) {
    console.error('FAIL: 找不到 sharp（本仓库 npm i -D sharp，或本机装有 DSH 亦可复用）');
    process.exit(1);
  }
  const entries = [];
  for (const size of SIZES) {
    const png = await sharp(svg(size), { density: 384 })
      .resize(size, size, { fit: 'contain', background: { r: 0, g: 0, b: 0, alpha: 0 } })
      .png().toBuffer();
    entries.push({ size, png });
    if (size === 256) {
      try {
        fs.mkdirSync(path.dirname(PREVIEW), { recursive: true });
        fs.writeFileSync(PREVIEW, png);
      } catch (e) { /* 预览写不出不影响 .ico */ }
    }
  }
  fs.mkdirSync(path.dirname(OUT), { recursive: true });
  fs.writeFileSync(OUT, buildIco(entries));
  console.log('OK ' + OUT + ' (' + fs.statSync(OUT).size + 'B, sizes=' + SIZES.join(',') + ')');
  console.log('OK ' + PREVIEW);
})().catch((e) => { console.error('FAIL: ' + e.message); process.exit(1); });
