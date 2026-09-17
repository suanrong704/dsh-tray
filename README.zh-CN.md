# dsh-tray

[![build](https://github.com/suanrong704/dsh-tray/actions/workflows/build.yml/badge.svg)](https://github.com/suanrong704/dsh-tray/actions/workflows/build.yml)

[English](./README.md) | 中文

给 **DeepSeek Harness** Web GUI 用的 Windows 托盘启动器：双击启动、托盘常驻、右键退出。

- 全程**没有 cmd / PowerShell 黑窗口**（编译为 GUI 子系统）
- 单个 **~44 KB** 的 exe，无 Electron、无运行时依赖（用系统自带的 .NET Framework）
- 再次双击**只打开浏览器**，绝不启动第二个 DSH
- 自动使用 DSH 打印的**带 token 地址**，避免重启后认证失败
- 「退出」结束**整棵进程树**，不留 pwsh 子进程

## 下载即用

不想自己编译的话，到 **[Releases](https://github.com/suanrong704/dsh-tray/releases/latest)** 下载
`dsh-tray-v1.2.0-win-x64.zip`，解压后双击 `dsh-tray.exe` 即可 —— **不需要 Node，也不需要编译**。

首次运行零配置：工作区默认使用你的用户主目录；要改就编辑 exe 同目录的 `dsh-tray.ini`
（格式见 `dsh-tray.ini.example`）。

## 为什么还需要一个启动器

`dsh web` 本身很好用，但放到桌面日常使用时有几个坑，这个项目就是来填的：

| 问题 | dsh-tray 的做法 |
|---|---|
| 必须一直留着一个终端窗口，关掉就停了 | 托盘常驻，双击启动、右键退出 |
| 重复运行会起第二个实例、抢端口 | 按端口隔离的互斥体 + 命名事件，第二次启动只通知第一个实例打开浏览器 |
| 重启后用裸地址 `http://127.0.0.1:3080` 打开会**认证失败** | 启动时传 `--no-open`，抓取 dsh 输出的 `?token=...` 地址并用它打开浏览器 |
| 只杀父进程会留下 pwsh 等子进程 | 退出走 `taskkill /PID <pid> /T /F` 结束整棵树 |

## 图标说明

本项目**不携带** DeepSeek 的图形标识。两类图标来源不同：

**① 托盘图标**（右下角）—— 启动时按以下顺序取得：

1. **运行时渲染（默认）**：读本机 DSH 自带的 `favicon.svg`
   （`$DSH_HOME/profiles/*/node_modules/@deepseek-ai/dsh-web-frontend/dist/favicon.svg`），
   用 .NET 自带的 WPF 直接光栅化并组装成多尺寸图标 —— 不需要 Node，也不需要 sharp。
2. exe 内嵌图标（本项目自己的图形）→ 系统默认图标。

任何一步失败（缺 WPF、SVG 结构变化、找不到 favicon）都会被捕获后退回下一级，不影响启动。
可在 `dsh-tray.ini` 写 `officialIcon=0` 关掉官方图标，或用环境变量 `DSH_FAVICON` 指定其他 SVG。

**② exe 文件图标 / 桌面快捷方式图标** —— 是本项目**原创的胖鲸图形**
（`assets/icon-app.ico`，MIT，构建时内嵌）。PE 资源无法在运行时修改，所以这里用我们自己的图，
而**右下角托盘里是 DeepSeek 官方鲸鱼**（从你本机的 DSH 读取，从不随二进制分发）。

> 维护者想改这张图：编辑 `tools/make-app-icon.js` 里的图形定义并重新运行它。

## 环境要求

- **Windows 10 / 11**
- **.NET Framework 4.x**（系统自带；`csc.exe` 与运行程序都用它，**不需要** .NET SDK）
- **Node.js 不需要**：日常构建与运行都不依赖它（仅维护者重新生成 `assets/icon-app.ico` 时才用得上）
- 一个已安装的 **DeepSeek Harness**（`npx @deepseek-ai/dsh web` 或全局安装均可）

## 从源码构建（可选）

```powershell
git clone https://github.com/suanrong704/dsh-tray
cd dsh-tray
pwsh -File scripts/build.ps1      # 或直接双击 scripts\build.cmd
```

产物在 `dist\`：

| 文件 | 说明 |
|---|---|
| `dsh-tray.exe` | 托盘程序 |
| `dsh.ico` | 图标（构建时从本机 DSH 读取官方 favicon 生成，见 [NOTICE](./NOTICE)） |
| `dsh-tray.ini` | 配置（首次构建时从 `.example` 复制，不会覆盖你已有的配置） |
| `selftest.txt` | 自检输出 |

> `scripts/build.ps1 -SkipIcon` 可跳过图标只做编译检查（CI 用；CI 机器上没有 DSH）。

## 使用

1. 编辑 `dist\dsh-tray.ini`，把 `workspace` 改成你希望 DSH 使用的工作区根目录
2. 双击 `dist\dsh-tray.exe` —— 右下角出现鲸鱼托盘图标
3. 想开机自启：`Win+R` 输入 `shell:startup`，把 `dsh-tray.exe` 的快捷方式丢进去

托盘操作：

| 操作 | 结果 |
|---|---|
| 左键 / 双击图标 | 打开浏览器（带 token 地址） |
| 右键 → 打开 DeepSeek Harness | 同上 |
| 右键 → 打开日志 | 打开 `%LOCALAPPDATA%\DshTray\dsh-web.log`（DSH 的完整 stdout/stderr） |
| 右键 → 打开工作区 | 资源管理器打开配置的 workspace |
| 右键 → 退出（结束 DSH） | 结束 DSH 进程树并移除图标 |
| 再次双击 exe | 通知已在运行的实例打开浏览器后自行退出 |

## 配置

`dsh-tray.ini`（放在 exe 旁边）：

```ini
workspace=C:\Users\YourName\workspace   # GUI 里新建会话的默认根目录
port=3080                               # Web GUI 端口
```

也支持环境变量，优先级高于 ini：`DSH_WORKSPACE`、`DSH_PORT`、`DSH_EXE`。
未配置时 `workspace` 默认是用户主目录。

## dsh 入口的查找顺序

1. 环境变量 `DSH_EXE` 指向的文件
2. `PATH` 上的 `dsh.cmd`（全局安装）
3. 已安装的 `@deepseek-ai/dsh/lib/bin.js` —— 依次检查 `npm -g`、`pnpm -g`、`npx` 缓存，取**修改时间最新**的一份
4. 兜底：`npx -y @deepseek-ai/dsh web --port <port> --no-open`（需要联网，首次较慢）

## 命令行参数

```powershell
dsh-tray.exe                      # 正常启动（等同双击）
dsh-tray.exe --open               # 让已在运行的实例打开浏览器
dsh-tray.exe --stop               # 让已在运行的实例退出（并结束 DSH）
dsh-tray.exe --selftest out.txt   # 只写诊断信息，不出界面、不启动任何进程
```

> `--open` / `--stop` 依据同一份 `dsh-tray.ini` 解析端口；多实例时配合 `DSH_PORT` 使用。

## 工作原理

- **启动**：带 `--no-open` 启动 DSH，stdout/stderr 重定向进日志；轮询端口就绪后，
  再等待 stdout 里出现 `dsh web: http://127.0.0.1:<port>/?token=...`，用**这个地址**打开浏览器
  （最多等 10 秒，超时回退裸地址）。这正是重启后仍能正常认证的关键。
- **单实例**：命名互斥体（名字含端口）。第二个实例 Set 一个命名事件，让第一个实例打开浏览器，
  自己立即退出。
- **退出**：若 DSH 是本程序启动的 → 结束它启动的进程树；若是外部启动的 → 用 `netstat -ano`
  找到占用该端口的 PID，再结束其进程树。
- **外部启动模式**：端口的生命周期就是这个图标的生命周期——端口消失后图标自动退出，
  下次双击即可正常接管。

## 已知限制

- **仅 Windows**（依赖 WinForms 与 `taskkill`）。
- **界面文案目前是中文**，欢迎 PR 补多语言。
- 若 DSH 是**外部启动**的（例如终端里的 `npx dsh web`），托盘拿不到 token 地址，只能打开裸地址；
  只要浏览器还持有该进程的签名 cookie 就能用，否则请从本启动器重启 DSH。
- 端口就绪后最多再等 10 秒抓取 token URL，超时则回退裸地址。
- 图标外观取决于你本机的 DSH 版本；托盘图标是运行时生成的，而 exe/快捷方式图标必须构建时内嵌。

## 商标与致谢

本项目**非官方**，与 DeepSeek 无隶属关系。仓库**不分发** DeepSeek 的图形标识：
图标在构建时从你本机的 DSH 读取。详见 [NOTICE](./NOTICE)。

也**因此只发布源码，不发布预编译二进制** —— 构建出的 exe 会内嵌该图形。

## 许可证

[MIT](./LICENSE)
