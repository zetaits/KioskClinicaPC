using System.Diagnostics;
using System.Text.Json;
using Kiosk.SetupHelper;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class WinGetPreparationTests
{
    // Execute the production PowerShell script with local commands replaced by fakes.
    // These tests never import, repair or install real packages on the test machine.
    private static async Task<string[]> Script(bool moduleReady, bool providerReady = true, bool corruptAfterInstall = false, bool repairFails = false)
    {
        if (!OperatingSystem.IsWindows()) return [];
        string prefix = """
            $script:calls = [Collections.Generic.List[string]]::new()
            $script:imports = 0
            function Import-Module {
                param($Name, $RequiredVersion, $ErrorAction)
                $script:calls.Add("module-import:$RequiredVersion")
                $script:imports++
                if ($Name -ne 'Microsoft.WinGet.Client' -or $RequiredVersion -ne '1.12.440') { throw 'Incorrect module' }
                if (($script:imports -eq 1 -and -not $moduleInitiallyReady) -or ($script:imports -gt 1 -and $corruptAfterInstall)) { throw 'Module missing or corrupt' }
            }
            function Import-PackageProvider {
                param($Name, $MinimumVersion, [switch]$ForceBootstrap, $ErrorAction)
                if ($Name -ne 'NuGet' -or $MinimumVersion -ne '2.8.5.201' -or $ForceBootstrap) { throw 'Incorrect provider' }
                $script:calls.Add('provider-import')
                if (-not $providerReady) { throw 'Provider missing' }
            }
            function Install-PackageProvider {
                param($Name, $MinimumVersion, [switch]$Force, $Scope)
                if ($Name -ne 'NuGet' -or $MinimumVersion -ne '2.8.5.201' -or -not $Force -or $Scope -ne 'CurrentUser') { throw 'Incorrect provider install' }
                $script:calls.Add('provider-install')
            }
            function Install-Module {
                param($Name, $RequiredVersion, $Repository, $Scope, [switch]$Force, [switch]$AllowClobber)
                if ($Name -ne 'Microsoft.WinGet.Client' -or $RequiredVersion -ne '1.12.440' -or $Repository -ne 'PSGallery' -or $Scope -ne 'CurrentUser' -or -not $Force -or -not $AllowClobber) { throw 'Incorrect module install' }
                $script:calls.Add('module-install')
            }
            function Repair-WinGetPackageManager {
                param($Version, [switch]$Force)
                if ($Version -ne '1.29.380' -or -not $Force) { throw 'Incorrect repair' }
                $script:calls.Add("repair:$Version")
                if ($repairFails) { throw 'Repair failed' }
            }
            """;
        string[]? calls = null;
        await WinGetPreparation.Run(default, async start =>
        {
            Assert.False(start.UseShellExecute); Assert.True(start.CreateNoWindow);
            string command = start.ArgumentList[^1]; start.ArgumentList.RemoveAt(start.ArgumentList.Count - 1);
            string variables = $"$moduleInitiallyReady=${moduleReady}; $providerReady=${providerReady}; $corruptAfterInstall=${corruptAfterInstall}; $repairFails=${repairFails}; ";
            start.ArgumentList.Add(variables + prefix + "\n$failed=$false; try { & {\n" + command +
                "\n} } catch { $failed=$true }; ConvertTo-Json -InputObject @($script:calls) -Compress; if ($failed) { exit 1 }");
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(30));
            calls = JsonSerializer.Deserialize<string[]>(await output); _ = await error;
            // Return success here so failure scenarios can inspect the full call list.
            Assert.Equal(corruptAfterInstall || repairFails ? 1 : 0, process.ExitCode);
            return 0;
        });
        return calls!;
    }

    [Fact]
    public async Task Existing_importable_module_skips_all_provider_and_gallery_calls()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(new[] { "module-import:1.12.440", "repair:1.29.380" }, await Script(true));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Missing_or_corrupt_module_installs_once_and_reuses_an_existing_provider(bool providerReady)
    {
        if (!OperatingSystem.IsWindows()) return;
        var expected = new List<string> { "module-import:1.12.440", "provider-import" };
        if (!providerReady) expected.Add("provider-install");
        expected.AddRange(["module-install", "module-import:1.12.440", "repair:1.29.380"]);
        Assert.Equal(expected, await Script(false, providerReady));
    }
    [Fact]
    public async Task Import_failure_after_install_does_not_loop_or_attempt_repair()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(new[] { "module-import:1.12.440", "provider-import", "module-install", "module-import:1.12.440" },
            await Script(false, corruptAfterInstall: true));
    }
    [Fact]
    public async Task Repair_failure_does_not_reinstall_the_module()
    {
        if (!OperatingSystem.IsWindows()) return;
        Assert.Equal(new[] { "module-import:1.12.440", "repair:1.29.380" }, await Script(true, repairFails: true));
    }
    [Fact]
    public async Task Nonzero_process_result_is_reported_as_preparation_failure()
        => await Assert.ThrowsAsync<IOException>(() => WinGetPreparation.Run(default, _ => Task.FromResult(1)));
    [Fact]
    public async Task Cancellation_before_preparation_never_starts_a_process()
    {
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => WinGetPreparation.Run(cancel.Token, _ => throw new Exception("Must not start")));
    }
    [Fact]
    public async Task Cancellation_during_preparation_waits_for_native_process_completion()
    {
        using var cancel = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var exited = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        var preparation = WinGetPreparation.Run(cancel.Token, _ => { started.SetResult(); return exited.Task; });
        await started.Task; cancel.Cancel(); Assert.False(preparation.IsCompleted);
        exited.SetResult(0);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation);
    }
}
