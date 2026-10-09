using System.IO;
using System.Text.Json;
using Kiosk.EquipmentSetup;
using KioskClinicaPC.Core;
using KioskClinicaPC.Equipment;
using Xunit;

namespace Kiosk.EquipmentSetup.Tests;

[Collection("Equipment resources")]
public sealed class PanelPasswordTests
{
    [Theory]
    [InlineData("1.2.0", false)]
    [InlineData("1.2.1", true)]
    public async Task Incompatible_default_stops_kiosk_before_any_payload_is_extracted(string version, bool malformed)
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-password-preflight-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string work = Path.Combine(root, "work"); Directory.CreateDirectory(work);
        try
        {
            var manifest = new PayloadManifest(2, 3, "equipment-wpf", "1.5.3", "1.5.3", version, new string('a', 40), new string('a', 64), new string('b', 64));
            File.WriteAllText(Path.Combine(root, "payload.json"), JsonSerializer.Serialize(manifest, Payload.Json));
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash("panel-test-password"), 1);
            File.WriteAllText(Path.Combine(root, "kiosk-password.json"), malformed ? "malformed-secret" : JsonSerializer.Serialize(seed, Payload.Json));
            Payload.UseDirectory(root);
            using var http = new System.Net.Http.HttpClient(); var events = new List<EquipmentEvent>();
            var error = await Assert.ThrowsAsync<ComponentPreparationException>(() => Payload.Prepare(new(false, true, 0, [], false), work, http, events.Add, default));
            Assert.IsType<InvalidDataException>(error.InnerException);
            Assert.Empty(Directory.GetFiles(work));
            string log = string.Join('\n', events.Select(EquipmentDiagnostics.FormatEvent));
            Assert.Contains("fase: validación del aprovisionamiento inicial", log);
            Assert.DoesNotContain("malformed-secret", log); Assert.DoesNotContain(seed.PasswordHash, log);
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Private_default_is_written_to_the_install_directory_without_logging_its_verifier()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-password-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); string installed = Path.Combine(root, "installed"); Directory.CreateDirectory(installed);
        try
        {
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash("panel-test-password"), 1);
            File.WriteAllText(Path.Combine(root, "kiosk-password.json"), JsonSerializer.Serialize(seed, Payload.Json));
            Payload.UseDirectory(root);
            var events = new List<EquipmentEvent>();
            await KioskPayload.ProvisionPanelPassword(installed, events.Add);
            var written = JsonSerializer.Deserialize<PanelPasswordProvisioning>(File.ReadAllText(Path.Combine(installed, PanelPasswordProvisioning.FileName)), Payload.Json)!;
            Assert.Equal(seed, written);
            string log = string.Join('\n', events.Select(EquipmentDiagnostics.FormatEvent));
            Assert.Contains("Contraseña inicial preparada", log);
            Assert.DoesNotContain(seed.PasswordHash, log); Assert.DoesNotContain("panel-test-password", log);
            Assert.DoesNotContain(Directory.GetFiles(installed), p => p.EndsWith(".tmp"));
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Fact]
    public async Task Old_assistant_without_a_default_does_not_change_installed_provisioning()
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-password-legacy-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            string existing = Path.Combine(root, PanelPasswordProvisioning.FileName); File.WriteAllText(existing, "preserved");
            Payload.UseDirectory(root); var events = new List<EquipmentEvent>();
            await KioskPayload.ProvisionPanelPassword(root, events.Add);
            Assert.Equal("preserved", File.ReadAllText(existing)); Assert.Empty(events);
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
    [Theory]
    [InlineData("malformed-secret")]
    [InlineData("{\"schemaVersion\":1,\"passwordHash\":\"malformed-secret\",\"passwordPolicyVersion\":1}")]
    public void Invalid_private_default_fails_with_a_safe_diagnostic(string content)
    {
        string root = Path.Combine(Path.GetTempPath(), "equipment-password-invalid-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root, "kiosk-password.json"), content); Payload.UseDirectory(root);
            var error = Assert.Throws<InvalidDataException>(() => Payload.PanelPassword());
            string log = EquipmentDiagnostics.Describe(error);
            Assert.Contains("Aprovisionamiento", log, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("malformed-secret", log);
        }
        finally { Payload.UseAssembly(typeof(MainWindow).Assembly); Directory.Delete(root, true); }
    }
}
