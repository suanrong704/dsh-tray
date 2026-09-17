<#
  Build dsh-tray: compile the tray app -> self-check.

  Usage:
    pwsh -File scripts/build.ps1              # full build (embeds assets/icon-app.ico)
    pwsh -File scripts/build.ps1 -SkipIcon    # compile without embedding the app icon

  Requirements: Windows 10/11 + the built-in .NET Framework 4.x.
  No Node.js needed: the exe icon is the committed assets/icon-app.ico, and the
  notification-area icon is rendered at runtime from the local DSH install.

  NOTE: this file is deliberately ASCII-only. Windows PowerShell 5.1 decodes
  BOM-less files as the system ANSI codepage, so non-ASCII text here would be
  mis-decoded (and can break parsing, e.g. by swallowing a closing quote).
  Keep all messages in this script ASCII.
#>
[CmdletBinding()]
param(
    [string]$OutDir,
    [switch]$SkipIcon
)

$ErrorActionPreference = 'Stop'
# NOTE: $PSScriptRoot is empty inside the param() block on Windows PowerShell 5.1,
# so resolve paths here in the body instead.
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (-not $OutDir) { $OutDir = Join-Path $root 'dist' }
$OutDir = [IO.Path]::GetFullPath($OutDir)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# ---- 1. Compile ----
Write-Host '[1/2] Compiling src/DshTray.cs ...'
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found (.NET Framework 4.x ships with Windows 10/11).' }

# WPF assemblies: used only to render the notification-area icon from the local
# DSH favicon.svg at runtime. System.Xaml sits in the framework directory; the
# other two live in its WPF subdirectory.
$fwDir = Split-Path $csc -Parent
$wpfRefs = @(
    ('/r:' + (Join-Path $fwDir 'WPF\PresentationCore.dll')),
    ('/r:' + (Join-Path $fwDir 'WPF\WindowsBase.dll')),
    ('/r:' + (Join-Path $fwDir 'System.Xaml.dll'))
)
foreach ($wpfRef in $wpfRefs) {
    $wpfPath = $wpfRef.Substring(3)
    if (-not (Test-Path $wpfPath)) { throw ('WPF assembly not found: ' + $wpfPath) }
}

# The exe icon is this project's own artwork (MIT), committed under assets/.
# It never contains DeepSeek's mark, so a built binary is safe to redistribute.
$ico = Join-Path $root 'assets\icon-app.ico'
$exe = Join-Path $OutDir 'dsh-tray.exe'
$srcFile = Join-Path $root 'src\DshTray.cs'

# Build the argument list with explicit variables: mixing a `+` expression with a
# trailing comma inside an array literal parses surprisingly in PowerShell.
$cscArgs = @('/nologo', '/target:winexe', ('/out:' + $exe),
             '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll') + $wpfRefs

if (Test-Path $ico) {
    if ($SkipIcon) {
        Write-Host '      -SkipIcon: not embedding assets/icon-app.ico'
    } else {
        $cscArgs += ('/win32icon:' + $ico)
        Write-Host ('      embedding ' + $ico)
    }
} else {
    Write-Host '      WARNING: assets/icon-app.ico missing - the exe will use the default icon.'
    Write-Host '      (Maintainers can regenerate it with: node tools/make-app-icon.js)'
}
$cscArgs += $srcFile

Write-Host ('      ' + $csc)
Write-Host ('      ' + ($cscArgs -join ' '))
& $csc @cscArgs
if ($LASTEXITCODE -ne 0) { throw ('Compile failed (csc exit code ' + $LASTEXITCODE + ')') }

# Seed an editable config next to the exe on first build; never overwrite an existing one.
$iniExample = Join-Path $root 'dsh-tray.ini.example'
$iniTarget = Join-Path $OutDir 'dsh-tray.ini'
if ((Test-Path $iniExample) -and -not (Test-Path $iniTarget)) {
    # Seed with this user's real profile path so the first run works without edits.
    # Read/write as UTF-8 explicitly: the template carries non-ASCII comments.
    $template = [IO.File]::ReadAllText($iniExample, [Text.Encoding]::UTF8)
    $seeded = $template.Replace('C:\Users\YourName\workspace', $env:USERPROFILE)
    [IO.File]::WriteAllText($iniTarget, $seeded, (New-Object Text.UTF8Encoding($false)))
    Write-Host ('      Wrote config template: ' + $iniTarget + '  (workspace=' + $env:USERPROFILE + ')')
}

# ---- 2. Self-check ----
Write-Host '[2/2] Self-check ...'
$selftest = Join-Path $OutDir 'selftest.txt'
Start-Process -FilePath $exe -ArgumentList '--selftest', ('"' + $selftest + '"') -Wait
if (Test-Path $selftest) { Get-Content $selftest -Encoding UTF8 }

Write-Host ''
Write-Host ('Build finished: ' + $exe)
Write-Host ('Next: edit workspace in ' + (Join-Path $OutDir 'dsh-tray.ini') + ' then double-click the exe.')
