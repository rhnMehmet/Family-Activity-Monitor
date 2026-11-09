[CmdletBinding(SupportsShouldProcess = $true, ConfirmImpact = 'Medium')]
param(
    [Parameter(Mandatory = $true)][ValidateSet('Chrome','Edge','Firefox')][string[]]$Browser,
    [switch]$Uninstall,
    [switch]$ReplaceExisting
)
$ErrorActionPreference = 'Stop'
$packageRoot = [IO.Path]::GetFullPath($PSScriptRoot)
$identityPath = Join-Path $packageRoot 'extension-identity.json'
if (-not (Test-Path -LiteralPath $identityPath)) { throw 'Bu betiği yayın klasöründeki kopyadan çalıştırın.' }
$identity = Get-Content -LiteralPath $identityPath -Raw | ConvertFrom-Json
if ($identity.chromiumId -ne 'mcipbngepoajhbkoecbbdiijckmfejol' -or $identity.firefoxId -ne 'family-monitor@local.familyactivitymonitor') { throw 'Beklenmeyen eklenti kimliği; kayıt yapılmadı.' }
$manifestDirectory = Join-Path $packageRoot 'native-host-manifests'
$plans = foreach ($kind in ($Browser | Select-Object -Unique)) {
    $name = 'com.familymonitor.' + $kind.ToLowerInvariant()
    $vendor = switch ($kind) { Chrome { 'Google\Chrome' } Edge { 'Microsoft\Edge' } Firefox { 'Mozilla' } }
    $keyName = 'Software\' + $vendor + '\NativeMessagingHosts\' + $name
    $manifestPath = Join-Path $manifestDirectory ($kind.ToLowerInvariant() + '.json')
    $hostPath = Join-Path $packageRoot ('FamilyMonitor.NativeHost.' + $kind + '.exe')
    if (-not $Uninstall -and -not (Test-Path -LiteralPath $hostPath -PathType Leaf)) { throw "Native host bulunamadı: $hostPath" }
    foreach ($view in @([Microsoft.Win32.RegistryView]::Registry32, [Microsoft.Win32.RegistryView]::Registry64)) {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $view)
        try {
            $existing = $baseKey.OpenSubKey($keyName)
            try { $previous = if ($null -ne $existing) { $existing.GetValue('') } else { $null } }
            finally { if ($null -ne $existing) { $existing.Dispose() } }
        }
        finally { $baseKey.Dispose() }
        if (-not $Uninstall -and $null -ne $previous -and $previous -ne $manifestPath -and -not $ReplaceExisting) {
            throw 'Bu host adı başka bir yayın klasörüne kayıtlı. Önce mevcut kaydı inceleyin; değiştirmek istiyorsanız -ReplaceExisting seçimini açıkça verin.'
        }
        [PSCustomObject]@{ Kind=$kind; Name=$name; Key=$keyName; View=$view; Manifest=$manifestPath; Host=$hostPath; Previous=$previous }
    }
}
foreach ($plan in $plans) {
    if ($Uninstall) {
        if ($plan.Previous -ne $plan.Manifest) { Write-Warning 'Bu klasöre ait olmayan kayıt korundu.'; continue }
        if ($PSCmdlet.ShouldProcess(('HKCU\' + $plan.Key + ' (' + $plan.View + ')'), 'Yalnızca bu yayının native host kaydını kaldır')) {
            $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $plan.View)
            try { $baseKey.DeleteSubKey($plan.Key, $false) } finally { $baseKey.Dispose() }
        }
        continue
    }
    if ($PSCmdlet.ShouldProcess($plan.Manifest, 'Seçili tarayıcı için dar izinli native host manifesti oluştur')) {
        [IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
        $manifest = [ordered]@{ name=$plan.Name; description='Family Activity Monitor — yerel, açık izinli bağlantı'; path=$plan.Host; type='stdio' }
        if ($plan.Kind -eq 'Firefox') { $manifest.allowed_extensions = @($identity.firefoxId) }
        else { $manifest.allowed_origins = @('chrome-extension://' + $identity.chromiumId + '/') }
        [IO.File]::WriteAllText($plan.Manifest, (($manifest | ConvertTo-Json -Depth 4) + [Environment]::NewLine), [Text.UTF8Encoding]::new($false))
    }
    if ($PSCmdlet.ShouldProcess(('HKCU\' + $plan.Key + ' (' + $plan.View + ')'), 'Seçili tarayıcı için native host kaydı oluştur')) {
        $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey([Microsoft.Win32.RegistryHive]::CurrentUser, $plan.View)
        try {
            $key = $baseKey.CreateSubKey($plan.Key, $true)
            try { $key.SetValue('', $plan.Manifest, [Microsoft.Win32.RegistryValueKind]::String) } finally { $key.Dispose() }
        }
        finally { $baseKey.Dispose() }
    }
}
Write-Output 'Seçilen işlem tamamlandı. Bu betik eklenti kurmaz, otomatik başlatmayı açmaz ve kullanım veritabanını açmaz.'
