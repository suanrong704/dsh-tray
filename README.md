# dsh-tray

[![build](https://github.com/suanrong704/dsh-tray/actions/workflows/build.yml/badge.svg)](https://github.com/suanrong704/dsh-tray/actions/workflows/build.yml)

English | [中文](./README.zh-CN.md)

A Windows tray launcher for the **DeepSeek Harness** web GUI: double-click to start, lives in the
notification area, right-click to quit.

- **No cmd / PowerShell window ever appears** (compiled as a GUI-subsystem exe)
- A single **~44 KB** exe — no Electron, no runtime dependencies (uses the built-in .NET Framework)
- Double-clicking again **only opens the browser**; it never starts a second DSH
- Opens the browser with the **tokenized URL** DSH prints, so authentication keeps working after restarts
- "Exit" kills the **entire process tree**, leaving no orphaned shells behind

## Download and run

Grab `dsh-tray-v1.2.0-win-x64.zip` from
**[Releases](https://github.com/suanrong704/dsh-tray/releases/latest)**, unzip it and double-click
`dsh-tray.exe` — **no Node, no build step**.

First run needs no configuration: the workspace defaults to your user profile; edit `dsh-tray.ini`
next to the exe if you want to change it (see `dsh-tray.ini.example`).

## Why a launcher at all

`dsh web` works fine on its own, but for day-to-day desktop use there are a few rough edges:

| Problem | What dsh-tray does |
|---|---|
| You must keep a terminal window open; closing it stops DSH | Tray-resident: double-click to start, right-click to quit |
| Running it twice starts a second instance and fights over the port | Per-port mutex + named event: the second launch just tells the first instance to open the browser |
| After a restart, opening the bare `http://127.0.0.1:3080` **fails authentication** | Starts DSH with `--no-open`, captures the `?token=...` URL from its output, and opens that |
| Killing only the parent process leaves child shells behind | Exit runs `taskkill /PID <pid> /T /F` on the whole tree |

## About the icons

<img src="assets/icon-app-preview.png" width="88" alt="dsh-tray exe icon">

This project **ships no DeepSeek artwork**. The two icons have different sources:

**1. Notification-area (tray) icon** — resolved at startup in this order:

1. **Rendered at runtime (default)**: reads `favicon.svg` from your own DSH install
   (`$DSH_HOME/profiles/*/node_modules/@deepseek-ai/dsh-web-frontend/dist/favicon.svg`) and
   rasterizes it with the built-in .NET WPF stack — no Node, no sharp.
2. The exe's embedded icon (this project's own artwork), then the system default icon.

Any failure (WPF missing, changed SVG structure, no favicon) is caught and falls through; it never
blocks startup. Set `officialIcon=0` in `dsh-tray.ini` to turn the official icon off, or
`DSH_FAVICON` to point at a different SVG.

**2. exe file icon / desktop-shortcut icon** — this project's **own artwork**, a chunky whale
(`assets/icon-app.ico`, MIT, embedded at build time). PE resources cannot be changed while the
program runs, so Explorer shows our mark while the **tray shows DeepSeek's official whale** (read
from your local DSH, never redistributed in the binary).

> Maintainers: edit the artwork definition in `tools/make-app-icon.js` and re-run it.

## Requirements

- **Windows 10 / 11**
- **.NET Framework 4.x** (ships with Windows; used for both `csc.exe` and the app — **no .NET SDK needed**)
- **Node.js is not required** — neither to build nor to run (it is only used by maintainers to regenerate `assets/icon-app.ico`)
- An installed **DeepSeek Harness** (`npx @deepseek-ai/dsh web` or a global install)

## Build from source (optional)

```powershell
git clone https://github.com/suanrong704/dsh-tray
cd dsh-tray
pwsh -File scripts/build.ps1      # or double-click scripts\build.cmd
```

Output lands in `dist\`:

| File | Purpose |
|---|---|
| `dsh-tray.exe` | the tray app |
| `dsh.ico` | icon, generated at build time from your own DSH install (see [NOTICE](./NOTICE)) |
| `dsh-tray.ini` | config (copied from `.example` on first build; never overwrites an existing one) |
| `selftest.txt` | diagnostics from the build's self-check |

> `scripts/build.ps1 -SkipIcon` compiles without the icon (used by CI, where no DSH is installed).

## Usage

1. Edit `dist\dsh-tray.ini` and set `workspace` to the root folder DSH should use
2. Double-click `dist\dsh-tray.exe` — a whale icon appears in the notification area
3. To start it at login: press `Win+R`, run `shell:startup`, and drop a shortcut to `dsh-tray.exe` in there

Tray actions:

| Action | Result |
|---|---|
| Left-click / double-click the icon | Open the browser (tokenized URL) |
| Right-click → Open DeepSeek Harness | Same as above |
| Right-click → Open Log | Opens `%LOCALAPPDATA%\DshTray\dsh-web.log` (full DSH stdout/stderr) |
| Right-click → Open Workspace | Opens the configured workspace in Explorer |
| Right-click → Exit (stop DSH) | Kills the DSH process tree and removes the icon |
| Double-click the exe again | Tells the running instance to open the browser, then exits |

## Configuration

`dsh-tray.ini`, next to the exe:

```ini
workspace=C:\Users\YourName\workspace   # root folder for new sessions in the GUI
port=3080                               # web GUI port
```

Environment variables override the ini: `DSH_WORKSPACE`, `DSH_PORT`, `DSH_EXE`.
When unset, `workspace` defaults to your user profile directory.

## How the dsh entry point is resolved

1. The file named by the `DSH_EXE` environment variable
2. `dsh.cmd` on `PATH` (a global install)
3. An installed `@deepseek-ai/dsh/lib/bin.js` — checked under `npm -g`, `pnpm -g`, and the `npx`
   cache, taking the **most recently modified** one
4. Fallback: `npx -y @deepseek-ai/dsh web --port <port> --no-open` (needs network; slower the first time)

## Command line

```powershell
dsh-tray.exe                      # normal start (same as double-clicking)
dsh-tray.exe --open               # ask the running instance to open the browser
dsh-tray.exe --stop               # ask the running instance to exit (stopping DSH)
dsh-tray.exe --selftest out.txt   # write diagnostics only; no UI, starts nothing
```

> `--open` / `--stop` resolve the port from the same `dsh-tray.ini`; use `DSH_PORT` when running
> several instances.

## How it works

- **Start**: launches DSH with `--no-open` and redirects stdout/stderr to the log. Once the port is
  listening it waits for the `dsh web: http://127.0.0.1:<port>/?token=...` line on stdout and opens
  **that** URL (10 s timeout, then it falls back to the bare address). This is what keeps
  authentication working across restarts.
- **Single instance**: a named mutex (the name includes the port). A second launch sets a named event
  so the first instance opens the browser, then exits immediately.
- **Exit**: if this app started DSH it kills that process tree; if DSH was started elsewhere it finds
  the PID listening on the port via `netstat -ano` and kills that tree.
- **Externally started mode**: the port's lifetime is the icon's lifetime — the icon disappears when
  the port goes away, and the next double-click takes over cleanly.

## Known limitations

- **Windows only** (WinForms + `taskkill`).
- **UI strings are currently Chinese**; PRs for localisation are welcome.
- If DSH was **started elsewhere** (e.g. `npx dsh web` in a terminal), the tray has no token URL and
  can only open the bare address. That works as long as the browser still holds that process's signed
  cookie; otherwise restart DSH from this launcher.
- After the port is ready, it waits at most 10 s for the token URL before falling back.
- The icon's appearance depends on the DSH version installed on your machine; the tray icon is generated at runtime, while the exe/shortcut icon must be embedded at build time.

## Trademarks and credits

This is an **unofficial** project, not affiliated with or endorsed by DeepSeek. The repository does
**not** distribute DeepSeek's logo: the icon is generated at build time from your own DSH install.
See [NOTICE](./NOTICE).

For that reason it **ships source only, with no prebuilt binaries** — a built exe embeds the mark.

## License

[MIT](./LICENSE)
