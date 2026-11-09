param()
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts/windows-2.0.0-prototype'))
$artifactsRoot = [IO.Path]::GetFullPath((Join-Path $projectRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $output.StartsWith($artifactsRoot, [StringComparison]::OrdinalIgnoreCase) -or (Split-Path $output -Leaf) -ne 'windows-2.0.0-prototype') { throw 'Güvenli yayın hedefi doğrulanamadı.' }
Push-Location $projectRoot
try {
    foreach ($project in @('src/FamilyMonitor.Agent/FamilyMonitor.Agent.csproj','src/FamilyMonitor.NativeHost/FamilyMonitor.NativeHost.csproj')) {
        & (Join-Path $PSScriptRoot 'dotnet.ps1') restore $project --configfile NuGet.Config --disable-parallel '-m:1' -r win-x64 '-p:SelfContained=true'
        if ($LASTEXITCODE -ne 0) { throw 'Publish restore failed.' }
        & (Join-Path $PSScriptRoot 'dotnet.ps1') publish $project -c Release -r win-x64 --self-contained true --no-restore -o $output '-m:1' '-p:UseSharedCompilation=false'
        if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    }
    foreach ($kind in @('Chrome','Edge','Firefox')) {
        Copy-Item -LiteralPath (Join-Path $output 'FamilyMonitor.NativeHost.exe') -Destination (Join-Path $output ('FamilyMonitor.NativeHost.' + $kind + '.exe')) -Force
    }
    Push-Location (Join-Path $projectRoot 'extensions')
    try { npm run build; if ($LASTEXITCODE -ne 0) { throw 'Extension build failed.' } } finally { Pop-Location }
    foreach ($kind in @('chrome','edge','firefox')) {
        $source = Join-Path $projectRoot ('artifacts/extensions-2.0.0-prototype/' + $kind)
        $target = Join-Path $output ('extensions/' + $kind)
        [IO.Directory]::CreateDirectory($target) | Out-Null
        foreach ($file in Get-ChildItem -LiteralPath $source -File) { Copy-Item -LiteralPath $file.FullName -Destination $target -Force }
    }
    Copy-Item -LiteralPath (Join-Path $projectRoot 'extensions/identity.json') -Destination (Join-Path $output 'extension-identity.json') -Force
    $identity = Get-Content -LiteralPath (Join-Path $output 'extension-identity.json') -Raw | ConvertFrom-Json
    $manifests = Join-Path $output 'native-host-manifests'
    [IO.Directory]::CreateDirectory($manifests) | Out-Null
    foreach ($kind in @('Chrome','Edge','Firefox')) {
        $manifest = [ordered]@{ name=('com.familymonitor.' + $kind.ToLowerInvariant()); description='Family Activity Monitor — yerel bağlantı'; path=(Join-Path $output ('FamilyMonitor.NativeHost.' + $kind + '.exe')); type='stdio' }
        if ($kind -eq 'Firefox') { $manifest.allowed_extensions=@($identity.firefoxId) }
        else { $manifest.allowed_origins=@('chrome-extension://' + $identity.chromiumId + '/') }
        [IO.File]::WriteAllText((Join-Path $manifests ($kind.ToLowerInvariant()+'.json')),($manifest | ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
    }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Install-NativeHosts.ps1') -Destination (Join-Path $output 'Install-NativeHosts.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/task-2-browser-installation.md') -Destination (Join-Path $output 'TARAYICI-KURULUM.md') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'docs/task-2-manual-tests.md') -Destination (Join-Path $output 'MANUEL-KABUL.md') -Force
    Write-Output $output
}
finally { Pop-Location }
