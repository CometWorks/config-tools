param(
    [Parameter(Mandatory=$true)][ValidateSet('PulsarConfig', 'MagnetarConfig')][string]$Tool,
    [Parameter(Mandatory=$true)][ValidateSet('linux-x64', 'win-x64')][string]$Rid,
    [string]$Version
)
$ErrorActionPreference = 'Stop'
if ($Tool -eq 'PulsarConfig' -and $Rid -ne 'linux-x64') { throw 'PulsarConfig supports Linux x64' }
if (-not $Version) { $Version = ([xml](Get-Content "$Tool/$Tool.csproj")).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1 }
if ($Version -notmatch '^\d+\.\d+\.\d+(-dev)?$') { throw "Invalid tool version: $Version" }
$output = Join-Path $PWD "publish/$Tool-$Rid"
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
New-Item -ItemType Directory -Force dist | Out-Null
dotnet publish "$Tool/$Tool.csproj" -c Release -r $Rid --self-contained true -p:Version=$Version -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:PublishTrimmed=false -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed: $Tool" }
$files = @(Get-ChildItem $output -File -Recurse)
if ($files.Count -ne 1) { throw "Expected one executable for $Tool, got $($files.Count) files" }
$extension = if ($Rid -eq 'win-x64') { '.exe' } else { '.bin' }
$dest = Join-Path $PWD "dist/$Tool-$Rid$extension"
Copy-Item $files[0].FullName $dest
if ($Rid -eq 'linux-x64') { chmod +x $dest }
if (($IsWindows -and $Rid -eq 'win-x64') -or ($IsLinux -and $Rid -eq 'linux-x64')) {
    $previousRoot = $env:DOTNET_ROOT
    try {
        $env:DOTNET_ROOT = Join-Path $PWD 'no-installed-runtime'
        & $dest --help
        if ($LASTEXITCODE -ne 0) { throw "Standalone startup failed: $Tool" }
        & $dest --tool-version
        if ($LASTEXITCODE -ne 0) { throw "Standalone version failed: $Tool" }
        if ($Tool -eq 'PulsarConfig') {
            python Scripts/test-bootstrap.py
            if ($LASTEXITCODE -ne 0) { throw 'Bootstrap smoke test failed' }
        }
        python Scripts/test-self-update.py $dest
        if ($LASTEXITCODE -ne 0) { throw "Self-update smoke test failed: $Tool" }
    } finally { $env:DOTNET_ROOT = $previousRoot }
}
$runtime = Get-ChildItem "$HOME/.nuget/packages/microsoft.netcore.app.runtime.$Rid" -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $runtime) { throw 'Runtime package notices not found' }
Copy-Item "$runtime/LICENSE.TXT" "dist/DotNet-LICENSE-$Rid.txt"
Copy-Item "$runtime/THIRD-PARTY-NOTICES.TXT" "dist/DotNet-NOTICES-$Rid.txt"
