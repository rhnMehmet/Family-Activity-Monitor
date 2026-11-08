using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;

namespace FamilyMonitor.Infrastructure.Games;

public sealed record InstalledPackage(string Name, string FamilyName, string InstallDirectory, string DisplayName,
    string[] Executables, string? GameConfigXml = null, string? ConfigDirectory = null, bool HasGameExtension = false);
public interface IWindowsPackageInventory { IReadOnlyList<InstalledPackage> Read(CancellationToken cancellationToken); }

/// <summary>Supported current-user Appx cmdlets (PackageManager), no elevation or protected directory enumeration.</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsPackageInventory : IWindowsPackageInventory
{
    private DateTimeOffset lastRead;
    private InstalledPackage[] cached = [];
    public IReadOnlyList<InstalledPackage> Read(CancellationToken cancellationToken)
    {
        if (DateTimeOffset.UtcNow - lastRead < TimeSpan.FromSeconds(30)) return cached;
        // Entire script is static. No paths or names from metadata/user input are interpolated as executable code.
        const string script = """
            $ErrorActionPreference='Stop'
            [Console]::OutputEncoding=[System.Text.Encoding]::UTF8
            $result=@(foreach($p in Get-AppxPackage -PackageTypeFilter Main) {
                $m=$null; try { $m=$p | Get-AppxPackageManifest -ErrorAction Stop } catch {}
                $exe=@(); $display=$p.Name; $game=$false
                if($null -ne $m) {
                    $display=[string]$m.Package.Properties.DisplayName
                    $exe=@($m.SelectNodes("//*[local-name()='Application'][@Executable]") | ForEach-Object { [string]$_.Executable })
                    $game=($m.SelectNodes("//*[local-name()='Extension'][@Category='windows.gameExplorer']").Count -gt 0)
                }
                if([string]::IsNullOrWhiteSpace($display) -or $display.StartsWith('ms-resource:')) { $display=$p.Name }
                $cfg=$null; $cfgdir=$null
                foreach($sub in @('', 'Content')) {
                    try {
                        $dir=Join-Path -Path $p.InstallLocation -ChildPath $sub
                        $file=Join-Path -Path $dir -ChildPath 'MicrosoftGame.config'
                        if((Test-Path -LiteralPath $file -PathType Leaf) -and (Get-Item -LiteralPath $file).Length -le 2097152) {
                            $cfg=Get-Content -LiteralPath $file -Raw -Encoding UTF8; $cfgdir=$dir; break
                        }
                    } catch {}
                }
                [pscustomobject]@{Name=$p.Name;FamilyName=$p.PackageFamilyName;InstallDirectory=$p.InstallLocation;
                    DisplayName=$display;Executables=$exe;GameConfigXml=$cfg;ConfigDirectory=$cfgdir;HasGameExtension=$game}
            })
            ConvertTo-Json -InputObject $result -Depth 5 -Compress
            """;
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
        start.ArgumentList.Add("-NoProfile"); start.ArgumentList.Add("-NonInteractive"); start.ArgumentList.Add("-EncodedCommand");
        start.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        using var process = Process.Start(start) ?? throw new IOException("Package query could not start.");
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errors = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(30));
            process.WaitForExitAsync(deadline.Token).GetAwaiter().GetResult();
            if (process.ExitCode != 0) throw new IOException("Current-user package query failed.");
            var json = output.GetAwaiter().GetResult();
            _ = errors.GetAwaiter().GetResult();
            if (json.Length > 8 * 1024 * 1024) throw new IOException("Package metadata budget exceeded.");
            cached = JsonSerializer.Deserialize<InstalledPackage[]>(json) ?? [];
            lastRead = DateTimeOffset.UtcNow;
            return cached;
        }
        catch (Exception error)
        {
            if (!process.HasExited) process.Kill();
            if (error is OperationCanceledException && !cancellationToken.IsCancellationRequested)
                throw new TimeoutException("Package query timed out.", error);
            throw;
        }
    }
}
