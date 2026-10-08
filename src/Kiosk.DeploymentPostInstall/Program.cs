using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text.Json;
using Kiosk.Deployment;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Equipment;
using Microsoft.Win32;

namespace Kiosk.DeploymentPostInstall;

internal static class Program
{
    private static string Root => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "ClinicaPC", "DeploymentJob");
    public static async Task<int> Main(string[] args)
    {
        if (args.Length > 0 && args is not ["--resume"]) return 64;
        try
        {
            MachineState.RequireElevated(); SecureRoot();
            var plan = LoadPlan();
            DeploymentPolicy.Require(plan.SchemaVersion == 1 && DeploymentPolicy.IsId(plan.Job.Id) &&
                plan.Job.SessionId == plan.SessionId && DeploymentPolicy.IsHash(plan.CallbackToken) && DeploymentPolicy.IsHash(plan.CertificateSha256), "Plan posterior incompatible.");
            DeploymentPolicy.Username(plan.Job.Username);
            using var handler = new HttpClientHandler { AllowAutoRedirect = false,
                ServerCertificateCustomValidationCallback = (_, cert, _, _) => cert is not null &&
                    Convert.ToHexString(SHA256.HashData(cert.RawData)).Equals(plan.CertificateSha256, StringComparison.OrdinalIgnoreCase) };
            using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            string route = plan.Server.TrimEnd('/') + "/worker/" + plan.SessionId + "/";
            DeploymentPolicy.Require(Uri.TryCreate(plan.Server, UriKind.Absolute, out var server) && server.Scheme == "https" &&
                server.UserInfo.Length == 0 && server.Query.Length == 0 && server.Fragment.Length == 0, "Dirección de estación no válida.");
            http.DefaultRequestHeaders.Add("X-Worker-Token", plan.CallbackToken);
            if (args.Length > 0)
            {
                var remote = await http.GetFromJsonAsync<DeploymentJob>(route + "status");
                DeploymentPolicy.Require(remote is not null && remote.Id == plan.Job.Id && remote.State == DeploymentState.PostInstall,
                    "Autoriza la reanudación de componentes desde la aplicación del encargado.");
                plan = plan with { Job = remote! };
                if (File.Exists(Path.Combine(Root, "components-result.json"))) File.Delete(Path.Combine(Root, "components-result.json"));
                if (File.Exists(Path.Combine(Root, "result.json"))) File.Delete(Path.Combine(Root, "result.json"));
                string journalPath = Path.Combine(Root, "progress-v1.json");
                if (File.Exists(journalPath)) File.Move(journalPath, Path.Combine(Root, "progress-before-recovery-" + Guid.NewGuid().ToString("N") + ".json"));
            }
            var journal = new DeploymentProgressJournal(Path.Combine(Root, "progress-v1.json"), plan.Job.LastSequence);
            async Task<bool> SendProgress(DeploymentProgress value)
            {
                try { using var response = await http.PostAsJsonAsync(route + "progress", value); return response.IsSuccessStatusCode; }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { return false; }
            }
            string? lastApplications = null;
            void ComponentProgress(EquipmentEvent value)
            {
                if (value.Kind is not ("phase" or "extract" or "preflight-ready" or "preflight-failed" or "applications")) return;
                string phase = value.Message;
                var activeApp = value.Run?.Items.FirstOrDefault(i => i.State is KioskClinicaPC.Core.Sync.PackItemState.Checking or KioskClinicaPC.Core.Sync.PackItemState.Installing);
                if (activeApp is not null) phase = activeApp.Application.DisplayName + ": " + activeApp.Message;
                KioskClinicaPC.Core.Sync.PackRun? applications = null;
                if (value.Run is { } run)
                {
                    var signature = JsonSerializer.Serialize(run.Items.Select(i => new { i.Application.Id,
                        i.Application.PinnedVersion, i.State, i.InstalledVersion, i.RequiresRebootBeforeRetry, i.RebootRequired,
                        Error = i.State is KioskClinicaPC.Core.Sync.PackItemState.Failed or KioskClinicaPC.Core.Sync.PackItemState.VerificationPending ? i.Message : null }));
                    // Retain state/version changes offline without duplicating the full pack for every download tick.
                    if (signature != lastApplications) { applications = run; lastApplications = signature; }
                }
                journal.Append(plan.Job.Id, DeploymentState.PostInstall, phase, value.Percent, true, true,
                    reboot: value.Run?.RebootRequired ?? value.RebootRequired, applications: applications);
                _ = journal.Drain(SendProgress);
            }
            // Credential cleanup is mandatory and precedes every application installer, including resumption.
            using var identity = WindowsIdentity.GetCurrent();
            var preparation = await DeploymentPreparation.Run(() => { CredentialCleanup.Run(); return Task.CompletedTask; }, () => VerifyWindows(plan),
                () => string.Equals(identity.Name.Split('\\').Last(), plan.Job.Username, StringComparison.OrdinalIgnoreCase) &&
                    new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator), async () =>
            {
                if (File.Exists(Path.Combine(Root, "components-result.json")))
                    return AtomicState.Read<EquipmentEvent>(Path.Combine(Root, "components-result.json"), () => throw new InvalidDataException());
                if (plan.Job.Profile.Applications.Applications.Count == 0 && !plan.Job.Profile.Kiosk)
                    return new("result", "Windows y usuario verificados.", ExitCode: 0);
                if (plan.WorkerSha256 is not null) await Verify("worker.zip", plan.WorkerSha256);
                if (plan.KioskSha256 is not null) await Verify("kiosk.exe", plan.KioskSha256);
                Payload.UseDirectory(Root);
                AtomicState.Write(Path.Combine(Root, "payload.json"), new PayloadManifest(2, 3, "equipment-wpf", "0.1.0", "0.1.0",
                    plan.KioskVersion ?? "0.0.0", new string('0', 40), plan.WorkerSha256 ?? new string('0', 64), plan.KioskSha256 ?? new string('0', 64)));
                string work = MachineState.Prepare();
                using var lease = SetupLease.Equipment(MachineState.Root, plan.Job.Profile.Applications.Applications.Count > 0);
                var selectedPack = plan.Job.Profile.ApplicationDefinition?.ForExecution() ?? plan.Job.Profile.Applications;
                var execution = new EquipmentExecution(_ => Task.FromResult(selectedPack),
                    () => new PackSession(work, _ => { }), new KioskPayload(work),
                    (selection, report, token) => Payload.Prepare(selection, work, http, report, token));
                var request = new EquipmentRequest(plan.Job.Profile.Applications.Applications.Count > 0, plan.Job.Profile.Kiosk,
                    selectedPack.Revision, selectedPack.Applications.Select(a => new EquipmentSelection(a.Id, a.PinnedVersion)).ToList(), args.Length > 0,
                    AllowPartialPack: selectedPack.Definition is not null, ResolveLatest: selectedPack.Definition is not null);
                var componentResult = await execution.Run(request, ComponentProgress, CancellationToken.None);
                if (componentResult.KioskVerified)
                {
                    try { await KioskPayload.RegisterAutostart(); }
                    catch { return componentResult with { Message = "Revisa el registro de inicio automático de Kiosk.", ExitCode = 2 }; }
                }
                return componentResult;
            });
            var result = preparation.Components;
            bool windowsVerified = preparation.WindowsVerified, accountVerified = preparation.AccountVerified;
            AtomicState.Write(Path.Combine(Root, "components-result.json"), result);
            var progressPath = Path.Combine(Root, "result.json");
            var progress = AtomicState.Read(progressPath, () => journal.Append(plan.Job.Id,
                result.ExitCode == 0 ? DeploymentState.Completed : DeploymentState.Attention,
                result.ExitCode == 0 ? result.RebootRequired ? "Reinicio necesario" : "Escritorio y componentes verificados" : "Windows instalado; revisa la preparación de aplicaciones",
                null, windowsVerified, accountVerified, result.ExitCode == 0, result.RebootRequired, result.Run));
            AtomicState.Write(progressPath, progress);
            // Typed durable result is retried verbatim. Repeated delivery cannot rerun Setup or native installers.
            for (int attempt = 0; attempt < 12; attempt++)
            {
                try
                {
                    await journal.Drain(SendProgress);
                    var remote = await http.GetFromJsonAsync<DeploymentJob>(route + "status");
                    if (remote?.LastSequence >= progress.Sequence && remote.State == progress.State)
                    {
                        if (progress.State == DeploymentState.Completed) File.Delete(Path.Combine(Root, "plan.bin"));
                        return result.ExitCode ?? 2;
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException) { }
                await Task.Delay(TimeSpan.FromSeconds(5));
            }
            return 3; // Pending callback can be resent by starting the helper; completed installers are retained.
        }
        catch { return 2; }
    }
    private static PostInstallPlan LoadPlan()
    {
        string path = Path.Combine(Root, "plan.json"), protectedPath = Path.Combine(Root, "plan.bin");
        if (File.Exists(path))
        {
            byte[] bytes = File.ReadAllBytes(path);
            var plan = JsonSerializer.Deserialize<PostInstallPlan>(bytes, AtomicState.Json) ?? throw new InvalidDataException();
            File.WriteAllBytes(protectedPath, ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser));
            File.Delete(path); return plan;
        }
        return JsonSerializer.Deserialize<PostInstallPlan>(ProtectedData.Unprotect(File.ReadAllBytes(protectedPath), null, DataProtectionScope.CurrentUser), AtomicState.Json)
            ?? throw new InvalidDataException();
    }
    private static bool VerifyWindows(PostInstallPlan plan)
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
        // Freeze the edition identity with the profile image at authorization; never accept Server or Enterprise.
        return Environment.Is64BitOperatingSystem && int.TryParse(key?.GetValue("CurrentBuildNumber") as string, out int build) && build >= plan.Job.WindowsBuild &&
            key?.GetValue("EditionID") as string == plan.Job.WindowsEditionId && plan.Job.WindowsEditionId is "Core" or "Professional";
    }
    private static async Task Verify(string name, string expected)
    {
        using var input = File.OpenRead(Path.Combine(Root, name));
        DeploymentPolicy.Require(Convert.ToHexString(await SHA256.HashDataAsync(input)).Equals(expected, StringComparison.OrdinalIgnoreCase), "Recurso posterior alterado.");
    }
    private static void SecureRoot()
    {
        for (var path = new DirectoryInfo(Root); path is not null; path = path.Parent)
            DeploymentPolicy.Require(!path.Exists || (path.Attributes & FileAttributes.ReparsePoint) == 0, "Carpeta posterior redirigida.");
        var acl = new System.Security.AccessControl.DirectorySecurity(); acl.SetAccessRuleProtection(true, false);
        foreach (var sid in new[] { WellKnownSidType.LocalSystemSid, WellKnownSidType.BuiltinAdministratorsSid })
            acl.AddAccessRule(new(new SecurityIdentifier(sid, null), System.Security.AccessControl.FileSystemRights.FullControl,
                System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
                System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
        new DirectoryInfo(Root).SetAccessControl(acl);
    }
}
