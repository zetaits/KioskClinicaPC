using KioskClinicaPC.Core;
using KioskClinicaPC.Core.Config;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class KioskPasswordPolicyTests
{
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
