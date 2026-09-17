# 贡献指南

感谢你愿意改进 dsh-tray。项目很小，但有几条**硬性规则**请务必遵守 —— 它们全是踩过坑换来的。

## 构建

```powershell
git clone https://github.com/suanrong704/dsh-tray
cd dsh-tray
pwsh -File scripts/build.ps1      # 或双击 scripts\build.cmd
```

需要 Windows 10/11 + 系统自带的 .NET Framework 4.x。**不需要 Node.js**。
产物在 `dist\`，构建末尾会打印自检结果 —— 请确认 `wsExists = True`；`runtimeIcon = ok` 只在你本机装过 DSH 时才会出现（CI 上是 `no favicon found`，属正常降级）。

## 硬性规则

### 1. `scripts/build.ps1` 必须保持纯 ASCII

Windows PowerShell 5.1 会把**无 BOM** 的文件按系统 ANSI 代码页解码。中文注释会被解码错乱，甚至**吞掉字符串的收尾引号**导致语法错误（本项目真实发生过）。要写说明就用英文。

### 2. `.cmd` / `.bat` 必须是 CRLF 换行

`cmd.exe` 按字节偏移逐行读取批处理文件，LF-only 会让偏移全面错位、每一行都被从中间截断执行（也真实发生过）。`.gitattributes` 已固化该策略，不要修改。

### 3. C# 必须保持 C# 5 语法

我们刻意使用 .NET Framework 4.0 自带的 `csc.exe`，以实现**零构建依赖**。因此**不要**使用：

- 插值字符串 `$"..."`
- `?.` / `??=` / `out var` / `nameof` / `using static`
- 表达式体成员（`=> ...`）与自动属性初始化器

### 4. 不要把 DeepSeek 的图形提交进仓库

- **托盘图标**：运行时从使用者本机的 DSH 读取 `favicon.svg` 渲染，**不进入二进制**
- **exe 文件图标**：本项目原创图形 `assets/icon-app.ico`（MIT），构建时内嵌

改 exe 图标请编辑 `tools/make-app-icon.js` 里的 SVG 定义并重新运行（需要 sharp，可从本机 DSH 的依赖树复用）。
**不要**把官方 favicon/logo 提交进仓库，也不要在构建时内嵌它 —— 那会让二进制变成不可分发的制品。

### 5. 提交前自查 CI 的三条断言

- **PE subsystem = 2**（GUI 子系统）—— 否则会弹出控制台窗口
- **构建产物必须内嵌 `assets/icon-app.ico`** —— 漏掉会静默退回 .NET 默认图标
- **自检输出的字段名不能改**：`workspace` / `wsExists` / `port` / `faviconPath` / `runtimeIcon` / `urlParseTest` / `iconExists`。这是对外契约，用户报障时会引用，重命名等于破坏排障能力

## 提交 PR

- 一个 PR 解决一件事
- 说明**动机**（为什么改），而不只是改了什么
- 行为变化请同时更新 `CHANGELOG.md` 与两份 README（中 / 英）

## 报 Bug

请使用仓库的 Issue 模板，并**务必附上自检输出**：

```powershell
dsh-tray.exe --selftest out.txt
```

它一次性说明了工作区是否存在、图标从哪来、dsh 入口解析成什么、端口是否被占用 —— 能省掉好几轮来回。
