# Changelog

本项目遵循 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/) 的结构，版本号遵循语义化版本。

## [1.0.0] - 2026-09-18

首个版本。

### 新增

- Windows 托盘启动器 `dsh-tray.exe`：GUI 子系统编译，任何情况下都不弹出控制台窗口。
- 双击启动；若 DSH 已在运行则只打开浏览器，绝不启动第二个实例
  （按端口隔离的命名互斥体 + 命名事件通知）。
- 托盘右键菜单：打开 DeepSeek Harness / 打开日志 / 打开工作区 / 退出（结束 DSH）。
- **token 感知的浏览器打开**：带 `--no-open` 启动 DSH，抓取其 stdout 里的
  `dsh web: http://127.0.0.1:<port>/?token=...` 并用该地址打开浏览器，
  避免重启后裸地址认证失败（等待上限 10 秒，超时回退裸地址）。
- 退出时用 `taskkill /T /F` 结束整个进程树；对**外部启动**的 DSH 则通过
  `netstat -ano` 找到占用端口的 PID 再结束其进程树。
- dsh 入口自动发现：`DSH_EXE` → `PATH` 上的 `dsh.cmd` → `npm -g` / `pnpm -g` / `npx`
  缓存中的 `bin.js`（取最新）→ `npx -y` 兜底。
- 配置：exe 同目录的 `dsh-tray.ini`（`workspace` / `port`），并支持
  `DSH_WORKSPACE`、`DSH_PORT`、`DSH_EXE` 环境变量覆盖。
- 命令行模式：`--open`、`--stop`、`--selftest <file>`。
- 构建脚本 `scripts/build.ps1`（含 `-SkipIcon`），图标由 `tools/build-icon.js`
  在构建时从本机 DSH 读取官方 favicon 生成，仓库不分发该图形（见 NOTICE）。
- GitHub Actions：仅做编译检查（CI 无 DSH，跳过图标生成）。

### 已知限制

- 仅 Windows；界面文案目前为中文。
- 外部启动的 DSH 实例无法获取 token 地址，只能打开裸地址。
