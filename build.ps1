# 這個檔案是 PowerShell，不是 C#。負責呼叫編譯器與執行測試。
$ErrorActionPreference = 'Stop'
$project = $PSScriptRoot
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'

New-Item -ItemType Directory -Force -Path (Join-Path $project 'dist') | Out-Null

# 核心程式碼同時用在桌面程式與測試中，不必複製兩份邏輯。
$coreSources = @(
    "$project\src\Entry.cs"
    "$project\src\Engine.cs"
    "$project\src\Settings.cs"
    "$project\src\ScanResult.cs"
)
$appSources = $coreSources + @(
    "$project\src\Program.cs"
    "$project\src\App.cs"
    "$project\src\CategoryDialog.cs"
)

# /target:winexe 產生桌面程式；manifest 設定 Windows 外觀與 DPI 行為。
& $compiler /nologo /target:winexe "/win32manifest:$project\src\app.manifest" "/out:$project\dist\FolderOrganizer.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll $appSources
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }

# 測試是獨立的主控台程式，有自己的 Main 入口。
& $compiler /nologo "/out:$project\tests\EngineTests.exe" $coreSources "$project\tests\EngineTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Test build failed' }

& "$project\tests\EngineTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }

# 新功能整合測試：設定、自訂分類、讀檔失敗與取消掃描。
& $compiler /nologo "/out:$project\tests\FeatureTests.exe" $coreSources "$project\tests\FeatureTests.cs"
if ($LASTEXITCODE -ne 0) { throw 'Feature test build failed' }
& "$project\tests\FeatureTests.exe"
if ($LASTEXITCODE -ne 0) { throw 'Feature tests failed' }

Write-Output "Ready: $project\dist\FolderOrganizer.exe"
