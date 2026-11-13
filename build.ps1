#Requires -Version 5.1
<#
    C# / .NET 桌面程序统一编译入口。新项目只需修改下面「项目配置」一段。

    用法：
      .\build.ps1                 发测试版（第三段加 1），默认行为
      .\build.ps1 -Bump Release   发正式版（第二段加 1，满 99 向前进位）。同时出不带运行时和自带运行时两个包
      .\build.ps1 -Bump None      版本号不变，仅重新编译
      .\build.ps1 -NoCommit       跳过 git 提交

    完整编译日志写入 dev\build.log，终端只显示进度和结果。
#>
[CmdletBinding()]
param(
    [ValidateSet('Test', 'Release', 'None')]
    [string]$Bump = 'Test',

    [switch]$NoCommit
)

$ErrorActionPreference = 'Stop'

# 部署按开关改成 $true 或 $false。不要手改。
$RuleVersion   = $true
$RuleNames     = $true
$RuleBumpAfter = $true
$DisplayName   = 'Cent Browser 便携助手'

# ==================== 项目配置：新项目只改这里 ====================
$AppName       = 'CentBrowser-PortableAssistant'    # 程序名，同时是 src\<AppName>\<AppName>.csproj 的名字
$Rid           = 'win-x64'  # 目标平台，32 位填 win-x86，ARM 填 win-arm64
$SelfContained = $false     # 测试版不带运行时。正式版会再打一个自带运行时的包
# =================================================================

$Ext         = 'exe'
$Root        = $PSScriptRoot
$SrcDir      = Join-Path $Root 'src'
$DistDir     = Join-Path $Root 'dist'
$DevDir      = Join-Path $Root 'dev'
$VersionFile = Join-Path $Root 'VERSION'
$LogFile     = Join-Path $DevDir 'build.log'
$PublishDir  = Join-Path $DevDir 'publish'

# ---------------------------- 输出函数 ----------------------------

function Write-Step { param([string]$m) Write-Host "==> $m" -ForegroundColor Cyan }
function Write-Ok   { param([string]$m) Write-Host "    $m" -ForegroundColor Green }
function Write-Note { param([string]$m) Write-Host "    $m" -ForegroundColor DarkGray }

function Stop-WithLog {
    param([string]$Title)
    Write-Host ''
    Write-Host "[失败] $Title" -ForegroundColor Red
    Write-Host '----- dev\build.log 末尾 40 行 -----' -ForegroundColor DarkGray
    if (Test-Path $LogFile) { Get-Content -Path $LogFile -Tail 40 }
    Write-Host '----- 完整日志见 dev\build.log -----' -ForegroundColor DarkGray
    exit 1
}

# 执行一段命令，输出全部写进日志文件，终端只留一行进度。
# 失败时回显日志末尾 40 行，避免编译器的长篇输出刷屏。
function Invoke-Logged {
    param(
        [Parameter(Mandatory)][string]$Title,
        [Parameter(Mandatory)][scriptblock]$Action
    )
    Write-Step $Title
    Add-Content -Path $LogFile -Value "`n===== $Title =====" -Encoding UTF8

    $global:LASTEXITCODE = 0
    $prev = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & $Action 2>&1 | ForEach-Object { $_.ToString() } |
            Add-Content -Path $LogFile -Encoding UTF8
    }
    catch {
        Add-Content -Path $LogFile -Value $_.Exception.Message -Encoding UTF8
        $ErrorActionPreference = $prev
        Stop-WithLog $Title
    }
    $ErrorActionPreference = $prev

    if ($LASTEXITCODE -ne 0) { Stop-WithLog $Title }
}

# ---------------------------- 版本号 ----------------------------

function Get-CurrentVersion {
    if (-not (Test-Path $VersionFile)) { return $null }
    $found = $null
        foreach ($line in @(Get-Content -Path $VersionFile -Encoding UTF8)) {
        $line = $line.Trim()
        if ($line -match '^Version\s*=\s*(.+)$') { $line = $Matches[1].Trim() }
        if ($line -match '^\d+\.\d+(\.\d+)?$') { $found = $line }
    }
    if ($found) { return $found }
    throw "VERSION 内容不合法。应包含两段或三段版本号，例如 0.1 或 0.1.1"
}

# 正式版两段 [主版本].[次版本]，测试版再加 [修订号]。
# 没有版本号时用 $VersionStart。测试版在末尾加一位，正式版去掉末尾再把第二位加 1。
# 第二位满 99 就进位：0.99 -> 1.0，1.99 -> 2.0。
$VersionStart = '1.0'

function Get-NextVersion {
    param([string]$Current, [string]$Mode)

    if (-not $Current) { return $VersionStart }
    if ($Mode -eq 'None') { return $Current }

    $p     = $Current.Split('.')
    $major = [int]$p[0]
    $minor = [int]$p[1]
    $patch = if ($p.Count -ge 3) { [int]$p[2] } else { 0 }

    if ($Mode -eq 'Test') { return "$major.$minor.$($patch + 1)" }

    if ($minor -ge 99) { return "$($major + 1).0" }
    return "$major.$($minor + 1)"
}

function Read-VersionNames {
    $chinese = $null
    $app = $null
    $plain = @()
    if (Test-Path $VersionFile) {
        foreach ($raw in @(Get-Content -Path $VersionFile -Encoding UTF8)) {
            $line = $raw.Trim()
            if (-not $line) { continue }
            if ($line -match '^ChineseName\s*=\s*(.*)$') { $chinese = $Matches[1].Trim(); continue }
            if ($line -match '^AppName\s*=\s*(.*)$') { $app = $Matches[1].Trim(); continue }
            if ($line -match '^Version\s*=') { continue }
            if ($line -match '^\d+\.\d+(\.\d+)?$') { continue }
            $plain += $line
        }
    }
    if (-not $chinese -and $plain.Count -ge 1) { $chinese = $plain[0] }
    if (-not $app -and $plain.Count -ge 2) { $app = $plain[1] }
    return @{ ChineseName = $chinese; AppName = $app }
}

function Save-Version {
    param([string]$Version)
    if ($RuleNames) {
        $display = $DisplayName
        $english = $AppName
        $stored = Read-VersionNames
        if ($stored.ChineseName) { $display = $stored.ChineseName }
        if ($stored.AppName) { $english = $stored.AppName }
        Set-Content -Path $VersionFile -Value @(
            "ChineseName=$display",
            "AppName=$english",
            "Version=$Version"
        ) -Encoding UTF8
        return
    }
    Set-Content -Path $VersionFile -Value $Version -Encoding ASCII -NoNewline
}

function Publish-Version {
    if (-not $RuleVersion) { return }
    Save-Version $version
    if (-not $NoCommit) { Invoke-GitCommit -Version $version -Mode $Bump }
}

# ---------------------------- git ----------------------------

function Invoke-GitCommit {
    param([string]$Version, [string]$Mode)

    if (-not (Test-Path (Join-Path $Root '.git'))) {
        Write-Note '当前目录不是 git 仓库，跳过提交'
        return
    }
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        Write-Note '未检测到 git，跳过提交'
        return
    }

    $label = if ($Mode -eq 'Release' -or $Version.Split('.').Count -lt 3) { '正式版' } else { '测试版' }
    git add -A 2>&1 | Out-Null
    $staged = git diff --cached --name-only 2>$null
    if (-not $staged) {
        Write-Note '没有需要提交的变更'
        return
    }

    git commit -q -m "v$Version $label" 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) { Write-Ok "已提交 v$Version" }
    else { Write-Note 'git 提交失败，继续编译' }
}

# ---------------------------- 编译一个产物 ----------------------------

function Publish-Executable {
    param(
        [Parameter(Mandatory)][bool]$BundleRuntime,
        [Parameter(Mandatory)][string]$Destination
    )

    if (Test-Path $PublishDir) { Remove-Item $PublishDir -Recurse -Force }

    $sc = if ($BundleRuntime) { 'true' } else { 'false' }
    $kind = if ($BundleRuntime) { '自带运行时' } else { '不带运行时' }
    # 参数放进数组再展开。单个字符串直接展开会被拆成一个个字符。
    $publishArgs = @(
        $Proj,
        '-c', 'Release',
        '-r', $Rid,
        '--self-contained', $sc,
        '--no-restore',
        '--nologo',
        '-v', 'quiet',
        '-p:PublishSingleFile=true',
        '-p:IncludeNativeLibrariesForSelfExtract=true',
        '-p:DebugType=none',
        "-p:Version=$version",
        '-o', $PublishDir
    )
    # 单文件压缩只允许自带运行时。不带运行时加上会报 NETSDK1176。
    if ($BundleRuntime) { $publishArgs += '-p:EnableCompressionInSingleFile=true' }

    # restore 必须带上和 publish 相同的 -r 与是否自带运行时，否则 publish 用 --no-restore 时
    # 会报 NETSDK1047：资产文件里没有该 RID 的目标。
    Invoke-Logged "还原依赖（$kind）" {
        dotnet restore $Proj -r $Rid "-p:SelfContained=$sc" --nologo -v quiet
    }

    Invoke-Logged "编译发布（$Rid，$kind）" {
        dotnet publish @publishArgs
    }

    $built = Join-Path $PublishDir "$AppName.exe"
    if (-not (Test-Path $built)) {
        $exe = Get-ChildItem -Path $PublishDir -Filter *.exe -ErrorAction SilentlyContinue |
               Select-Object -First 1
        if (-not $exe) { Stop-WithLog '发布产物中找不到 exe' }
        $built = $exe.FullName
    }

    Copy-Item -Path $built -Destination $Destination -Force
    $size = '{0:N1} MB' -f ((Get-Item $Destination).Length / 1MB)
    Write-Host "    完成  dist\$([IO.Path]::GetFileName($Destination))  ($size)" -ForegroundColor Green
}

# 正式版把两个包的下载地址写进 README，放在「使用」前面。
function Update-DownloadLinks {
    param(
        [Parameter(Mandatory)][string]$Version,
        [Parameter(Mandatory)][string]$PlainFile,
        [Parameter(Mandatory)][string]$BundledFile
    )

    $readmePath = Join-Path $Root 'README.md'
    if (-not (Test-Path $readmePath)) { return }

    $remote = git -C $Root remote get-url origin 2>$null
    if ($remote -notmatch 'github\.com[:/](.+?)(?:\.git)?\s*$') {
        Write-Note '没有 GitHub 远程仓库，跳过下载链接'
        return
    }

    $repo = "https://github.com/$($Matches[1])"
    $base = "$repo/releases/download/v$Version"
    $plainName = [IO.Path]::GetFileName($PlainFile)
    $bundledName = [IO.Path]::GetFileName($BundledFile)
    $plainMb = '{0:N1} MB' -f ((Get-Item $PlainFile).Length / 1MB)
    $bundledMb = '{0:N1} MB' -f ((Get-Item $BundledFile).Length / 1MB)
    $block = @"
## 下载

- [不带运行时]($base/$plainName)（$plainMb，需要 .NET 8 桌面运行时）
- [自带运行时]($base/$bundledName)（$bundledMb，不用另装运行时）
"@

    $utf8 = New-Object System.Text.UTF8Encoding $false
    $text = [IO.File]::ReadAllText($readmePath)
    $newline = if ($text.Contains("`r`n")) { "`r`n" } else { "`n" }
    $block = $block.Replace("`r`n", "`n").Replace("`n", $newline)

    $pattern = '(?s)## 下载\s*\r?\n.*?(?=\r?\n## )'
    if ([regex]::IsMatch($text, $pattern)) {
        $text = [regex]::Replace($text, $pattern, ($block + $newline), 1)
    }
    else {
        $marker = '## 使用'
        $idx = $text.IndexOf($marker)
        if ($idx -lt 0) {
            Write-Note '说明文档里没有「使用」，跳过下载链接'
            return
        }
        $text = $text.Insert($idx, $block + $newline + $newline)
    }

    [IO.File]::WriteAllText($readmePath, $text, $utf8)
    Write-Ok '已更新说明文档里的下载链接'
}

# ============================ 主流程 ============================

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw '未检测到 dotnet SDK，请先安装 .NET SDK'
}

# 定位主项目文件
$Proj = Join-Path $SrcDir "$AppName\$AppName.csproj"
if (-not (Test-Path $Proj)) {
    $found = Get-ChildItem -Path $SrcDir -Filter *.csproj -Recurse -ErrorAction SilentlyContinue |
             Select-Object -First 1
    if (-not $found) { throw "在 src\ 下找不到 .csproj，请确认主项目位于 src\$AppName\" }
    $Proj = $found.FullName
    Write-Note "使用自动定位到的项目：$($found.Name)"
}

New-Item -ItemType Directory -Force -Path $DistDir, $DevDir | Out-Null
Set-Content -Path $LogFile -Encoding UTF8 `
    -Value "build $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')  bump=$Bump"

$current = Get-CurrentVersion
if ($RuleVersion) {
    $version = Get-NextVersion -Current $current -Mode $Bump
} else {
    $version = if ($current) { $current } else { '1.0' }
}
$channel = if ($version.Split('.').Count -ge 3) { '测试版' } else { '正式版' }

Write-Host ''
Write-Host "$AppName  $(if ($current) { "v$current -> " })v$version  ($channel)" -ForegroundColor White
Write-Host ''

if (-not $RuleBumpAfter) { Publish-Version }

# ---------------------------- 编译 ----------------------------

$artifact = Join-Path $DistDir "$AppName-v$version.$Ext"
$artifactBundled = Join-Path $DistDir "$AppName-v$version-selfcontained.$Ext"
$formal = $version.Split('.').Count -lt 3

# ===== 编译步骤（技术栈相关，换技术栈时只改这一段）=====
# 上面的版本号、日志、git 提交和下面的产物报告都是通用框架，不随技术栈变动。
# 换语言时替换 Publish-Executable。测试版只出不带运行时的包，正式版再出一份自带运行时的。

Publish-Executable -BundleRuntime $SelfContained -Destination $artifact
if ($formal) {
    Publish-Executable -BundleRuntime $true -Destination $artifactBundled
    Update-DownloadLinks -Version $version -PlainFile $artifact -BundledFile $artifactBundled
}

if ($RuleBumpAfter) { Publish-Version }

# ===== 编译步骤结束 =====

Write-Host ''
