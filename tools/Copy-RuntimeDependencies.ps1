param(
    [Parameter(Mandatory=$true)][string]$PackageRoot,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference = 'Stop'
$frameworks = @('net462','net461','net47','net46','netstandard2.0')
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
foreach ($package in Get-ChildItem -LiteralPath $PackageRoot -Directory) {
    $selected = $null
    foreach ($framework in $frameworks) {
        $folder = Join-Path $package.FullName ('lib\' + $framework)
        if (Test-Path -LiteralPath $folder) { $selected = $folder; break }
    }
    if ($selected) { Copy-Item -Path (Join-Path $selected '*.dll') -Destination $OutputDirectory -Force -ErrorAction SilentlyContinue }
    foreach ($name in @('Microsoft.Win32.Registry','System.Security.AccessControl','System.Security.Principal.Windows')) {
        if ($package.Name.StartsWith($name + '.',[System.StringComparison]::OrdinalIgnoreCase)) {
            $runtime = Join-Path $package.FullName 'runtimes\win\lib'
            if (Test-Path -LiteralPath $runtime) {
                $selectedRuntime = Get-ChildItem -LiteralPath $runtime -Directory | Where-Object { $_.Name -in @('net462','net461','net46','netstandard2.0') } | Select-Object -First 1
                if ($selectedRuntime) { Copy-Item -Path (Join-Path $selectedRuntime.FullName '*.dll') -Destination $OutputDirectory -Force }
            }
        }
    }
    $trace = Join-Path $package.FullName 'build\native'
    if (Test-Path -LiteralPath $trace) {
        foreach ($architecture in @('x86','amd64','arm64')) {
            $source = Join-Path $trace $architecture
            if (Test-Path -LiteralPath $source) {
                $destination = Join-Path $OutputDirectory $architecture
                New-Item -ItemType Directory -Force -Path $destination | Out-Null
                Copy-Item -Path (Join-Path $source '*.dll') -Destination $destination -Force
            }
        }
    }
}
