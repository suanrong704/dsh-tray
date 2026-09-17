<#
  Build dsh-tray: generate icon -> compile exe -> self-check.

  Usage:
    pwsh -File scripts/build.ps1              # full build (needs DSH installed locally)
    pwsh -File scripts/build.ps1 -SkipIcon    # compile only, no icon (used by CI)

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

# ---- 1. Icon: derived from the locally installed DSH (never shipped in the repo) ----
$ico = Join-Path $OutDir 'dsh.ico'
if ($SkipIcon) {
    Write-Host '[1/3] Skipping icon generation (-SkipIcon)'
} else {
    Write-Host '[1/3] Generating icon from the locally installed DSH favicon ...'
    if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
        throw 'Node.js is required to generate the icon. Install Node, or use -SkipIcon to compile only.'
    }
    & node (Join-Path $root 'tools\build-icon.js') --out $ico --preview (Join-Path $OutDir 'icon-preview.png')
    if ($LASTEXITCODE -ne 0) { throw ('Icon generation failed (exit code ' + $LASTEXITCODE + ')') }
}

# ---- 2. Compile ----
Write-Host '[2/3] Compiling src/DshTray.cs ...'
$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'csc.exe not found (.NET Framework 4.x ships with Windows 10/11).' }

$exe = Join-Path $OutDir 'dsh-tray.exe'
$srcFile = Join-Path $root 'src\DshTray.cs'

# Build the argument list with explicit variables: mixing a `+` expression with a
# trailing comma inside an array literal parses surprisingly in PowerShell.
$cscArgs = @('/nologo', '/target:winexe', ('/out:' + $exe),
             '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll')
if (Test-Path $ico) { $cscArgs += ('/win32icon:' + $ico) }
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

# ---- 3. Self-check ----
Write-Host '[3/3] Self-check ...'
$selftest = Join-Path $OutDir 'selftest.txt'
Start-Process -FilePath $exe -ArgumentList '--selftest', ('"' + $selftest + '"') -Wait
if (Test-Path $selftest) { Get-Content $selftest -Encoding UTF8 }

Write-Host ''
Write-Host ('Build finished: ' + $exe)
Write-Host ('Next: edit workspace in ' + (Join-Path $OutDir 'dsh-tray.ini') + ' then double-click the exe.')
