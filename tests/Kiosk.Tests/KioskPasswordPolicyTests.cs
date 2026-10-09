using System.IO;
using KioskClinicaPC.Core;
using KioskClinicaPC.Core.Config;
using KioskClinicaPC.Equipment;
using Newtonsoft.Json;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class KioskPasswordPolicyTests
{
    [Fact]
    public void Private_panel_default_skips_setup_and_can_be_changed_without_affecting_the_panel()
    {
        string directory = Path.Combine(Path.GetTempPath(), "kiosk-panel-password-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            const string panelPassword = "clave-del-panel-para-pruebas";
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash(panelPassword), KioskSettings.CurrentPasswordPolicyVersion);
            string path = Path.Combine(directory, PanelPasswordProvisioning.FileName);
            File.WriteAllText(path, JsonConvert.SerializeObject(seed));
            var settings = new KioskSettings { ServerUrl = "https://existing.test", DeviceId = "original-device", InactivitySeconds = 123 };
            Assert.True(settings.ApplyPanelPasswordIfMissing(path));
            Assert.False(settings.RequiresPasswordSetup());
            Assert.True(PasswordService.Verify(panelPassword, settings.PasswordHash));
            string settingsPath = Path.Combine(directory, "settings.json"); settings.Save(settingsPath);
            var reopened = KioskSettings.Load(settingsPath);
            Assert.False(reopened.RequiresPasswordSetup());
            Assert.True(PasswordService.Verify(panelPassword, reopened.PasswordHash));
            Assert.False(reopened.TrySetPassword("incorrecta", "nueva-clave-del-equipo", "nueva-clave-del-equipo", out _));
            Assert.True(reopened.TrySetPassword(panelPassword, "nueva-clave-del-equipo", "nueva-clave-del-equipo", out _));
            Assert.True(PasswordService.Verify("nueva-clave-del-equipo", reopened.PasswordHash));
            Assert.False(reopened.ApplyPanelPasswordIfMissing(path));
            Assert.True(PasswordService.Verify("nueva-clave-del-equipo", reopened.PasswordHash));
            Assert.True(PasswordService.Verify(panelPassword, seed.PasswordHash));
            Assert.Equal("https://existing.test", reopened.ServerUrl); Assert.Equal("original-device", reopened.DeviceId); Assert.Equal(123, reopened.InactivitySeconds);
            Assert.DoesNotContain(panelPassword, File.ReadAllText(settingsPath));
            Assert.DoesNotContain(seed.PasswordHash, seed.ToString());
        }
        finally { Directory.Delete(directory, true); }
    }
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void Existing_password_is_preserved_even_for_an_older_policy(int policy)
    {
        var settings = new KioskSettings { PasswordHash = PasswordService.Hash("clave-local-existente"), PasswordPolicyVersion = policy };
        string hash = settings.PasswordHash;
        string file = Path.Combine(Path.GetTempPath(), "kiosk-existing-password-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            var seed = new PanelPasswordProvisioning(1, PasswordService.Hash("otra-clave-del-panel"), 1);
            File.WriteAllText(file, JsonConvert.SerializeObject(seed));
            Assert.False(settings.ApplyPanelPasswordIfMissing(file));
            Assert.Equal(hash, settings.PasswordHash); Assert.Equal(policy, settings.PasswordPolicyVersion);
        }
        finally { File.Delete(file); }
    }
    [Theory]
    [InlineData("{}")]
    [InlineData("not-json")]
    [InlineData("{\"schemaVersion\":1,\"passwordHash\":\"invalid\",\"passwordPolicyVersion\":1}")]
    public void Invalid_default_keeps_the_manual_setup_available(string content)
    {
        string file = Path.Combine(Path.GetTempPath(), "kiosk-password-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(file, content); var settings = new KioskSettings();
            Assert.False(settings.ApplyPanelPasswordIfMissing(file));
            Assert.True(settings.RequiresPasswordSetup()); Assert.Null(settings.PasswordHash);
        }
        finally { File.Delete(file); }
    }
    [Fact]
    public void New_profile_requires_password_setup_and_has_no_shared_default()
    {
        var settings = new KioskSettings();

        Assert.True(settings.RequiresPasswordSetup());
        Assert.Null(settings.PasswordHash);
    }

    [Fact]
    public void New_profile_accepts_a_strong_confirmed_password()
    {
        var settings = new KioskSettings();

        Assert.True(settings.TrySetPassword(null, "una-clave-local-segura", "una-clave-local-segura", out string error), error);
        Assert.False(settings.RequiresPasswordSetup());
        Assert.Equal(KioskSettings.CurrentPasswordPolicyVersion, settings.PasswordPolicyVersion);
        Assert.True(PasswordService.Verify("una-clave-local-segura", settings.PasswordHash));
    }

    [Fact]
    public void Existing_profile_requires_current_password_for_one_time_renewal()
    {
        var settings = new KioskSettings
        {
            PasswordHash = PasswordService.Hash("clave-anterior-segura"),
            PasswordPolicyVersion = 0,
            ServerUrl = "https://server.test",
            DeviceId = "device-1"
        };

        Assert.True(settings.RequiresPasswordSetup());
        Assert.False(settings.TrySetPassword("incorrecta", "una-clave-nueva-segura", "una-clave-nueva-segura", out _));
        Assert.True(settings.TrySetPassword("clave-anterior-segura", "una-clave-nueva-segura", "una-clave-nueva-segura", out string error), error);

        Assert.False(settings.RequiresPasswordSetup());
        Assert.Equal("https://server.test", settings.ServerUrl);
        Assert.Equal("device-1", settings.DeviceId);
    }

    [Theory]
    [InlineData("corta", "corta")]
    [InlineData("", "")]
    [InlineData("una-clave-nueva-segura", "no-coincide")]
    public void Invalid_new_password_is_rejected(string password, string confirmation)
    {
        var settings = new KioskSettings();

        Assert.False(settings.TrySetPassword(null, password, confirmation, out string error));
        Assert.NotEmpty(error);
        Assert.True(settings.RequiresPasswordSetup());
    }
}
