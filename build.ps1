[CmdletBinding()]
param(
    [switch]$SkipTests,
    [switch]$SkipSmoke,
    [switch]$NoPackage
)
$ErrorActionPreference = 'Stop'
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$root = $PSScriptRoot
$tools = Join-Path $root '.tools'
$packages = Join-Path $root 'packages'
$artifacts = Join-Path $root 'artifacts'
$nuget = Join-Path $tools 'nuget.exe'
New-Item -ItemType Directory -Force -Path $tools, $packages, $artifacts | Out-Null
if (-not (Test-Path -LiteralPath $nuget)) {
    Write-Host 'Загрузка NuGet в локальную папку проекта…'
    Invoke-WebRequest -Uri 'https://dist.nuget.org/win-x86-commandline/latest/nuget.exe' -OutFile $nuget -UseBasicParsing
}
$msbuild = @(
    (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe'),
    (Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe')
) | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
if (-not $msbuild) { throw 'MSBuild не найден. Установите .NET Framework 4.8 Developer Pack или Visual Studio Build Tools.' }
Write-Host 'Восстановление пакетов…'
$packageConfigs = @(Get-ChildItem -LiteralPath (Join-Path $root 'src'),(Join-Path $root 'tests') -Recurse -Filter 'packages.config' -File)
foreach ($config in $packageConfigs) {
    & $nuget install $config.FullName -OutputDirectory $packages -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw "NuGet restore завершился с ошибкой: $($config.FullName)" }
}
$runtimePackages = Join-Path $packages 'runtime'
if (-not (Test-Path -LiteralPath (Join-Path $runtimePackages 'Microsoft.Diagnostics.Tracing.TraceEvent.4.0.2'))) {
    Write-Host 'Разрешение runtime-зависимостей TraceEvent…'
    & $nuget install Microsoft.Diagnostics.Tracing.TraceEvent -Version 4.0.2 -OutputDirectory $runtimePackages -NonInteractive
    if ($LASTEXITCODE -ne 0) { throw 'Не удалось восстановить runtime-зависимости.' }
}
Write-Host 'Сборка Release…'
& $msbuild (Join-Path $root 'PCWatch.sln') /t:Build /p:Configuration=Release '/p:Platform=Any CPU' /m /v:m /nologo
if ($LASTEXITCODE -ne 0) { throw 'Сборка завершилась с ошибкой.' }
& (Join-Path $root 'tools\Copy-RuntimeDependencies.ps1') -PackageRoot $runtimePackages -OutputDirectory (Join-Path $root 'src\PCWatch\bin\Release')
& (Join-Path $root 'tools\Copy-RuntimeDependencies.ps1') -PackageRoot $runtimePackages -OutputDirectory (Join-Path $root 'tests\PCWatch.EtwSmoke\bin\Release')
if (-not $SkipTests) {
    & (Join-Path $root 'tests\PCWatch.Core.Tests\bin\Release\PCWatch.Core.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Провалены тесты ядра.' }
}
if (-not $SkipSmoke) {
    & (Join-Path $root 'tests\PCWatch.CollectorSmoke\bin\Release\PCWatch.CollectorSmoke.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Провален smoke-тест сборщика.' }
}
if (-not $NoPackage) {
    $portable = Join-Path $artifacts 'release\PCWatch'
    if (Test-Path -LiteralPath $portable) { Remove-Item -LiteralPath $portable -Recurse -Force }
    New-Item -ItemType Directory -Force -Path $portable | Out-Null
    $bin = Join-Path $root 'src\PCWatch\bin\Release'
    Copy-Item -Path (Join-Path $bin '*.exe'),(Join-Path $bin '*.dll'),(Join-Path $bin '*.config') -Destination $portable -Force -ErrorAction SilentlyContinue
    Copy-Item -LiteralPath (Join-Path $bin 'PCWatch.exe') -Destination $portable -Force
    if (Test-Path -LiteralPath (Join-Path $bin 'PCWatch.exe.config')) { Copy-Item -LiteralPath (Join-Path $bin 'PCWatch.exe.config') -Destination $portable -Force }
    Copy-Item -LiteralPath (Join-Path $root 'src\PCWatch\Assets\PCWatch.png') -Destination $portable -Force
    Copy-Item -LiteralPath (Join-Path $root 'THIRD-PARTY-NOTICES.txt') -Destination $portable -Force
    Copy-Item -LiteralPath (Join-Path $root 'README.md') -Destination $portable -Force
    Copy-Item -LiteralPath (Join-Path $root 'licenses') -Destination $portable -Recurse -Force
    foreach ($architecture in @('x86','amd64','arm64')) {
        $native = Join-Path $bin $architecture
        if (Test-Path -LiteralPath $native) { Copy-Item -LiteralPath $native -Destination $portable -Recurse -Force }
    }
    $zip = Join-Path $artifacts 'release\PCWatch-2.2.0-win.zip'
    if (Test-Path -LiteralPath $zip) { Remove-Item -LiteralPath $zip -Force }
    Compress-Archive -Path (Join-Path $portable '*') -DestinationPath $zip -CompressionLevel Optimal
    $hash = Get-FileHash -Algorithm SHA256 -LiteralPath $zip
    Set-Content -LiteralPath ($zip + '.sha256') -Value ($hash.Hash.ToLowerInvariant() + '  PCWatch-2.2.0-win.zip') -Encoding ascii
    Write-Host "Portable: $zip"
}
Write-Host 'Готово.'
