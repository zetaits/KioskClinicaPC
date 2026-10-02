using System;
using System.IO;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class KioskSettingsProvisioningTests
{
    [Fact]
    public void NewProfile_AppliesInstallerProvisioning()
    {
        string dir = NewTempDirectory();
        try
        {
            string provisioningPath = Path.Combine(dir, "KioskProvisioning.json");
            File.WriteAllText(provisioningPath, JsonConvert.SerializeObject(new
            {
                ServerUrl = "https://panel.example.test/",
                ServerApiKey = new string('a', 64)
            }));

            var settings = new KioskSettings();

            Assert.True(settings.ApplyProvisioningIfMissingServer(provisioningPath));
            Assert.Equal("https://panel.example.test", settings.ServerUrl);
            Assert.Equal(new string('a', 64), settings.ServerApiKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ExistingProfileWithoutServer_GetsProvisioningAndKeepsLocalSettings()
    {
        string dir = NewTempDirectory();
        try
        {
            string settingsPath = Path.Combine(dir, "KioskSettings.json");
            string provisioningPath = Path.Combine(dir, "KioskProvisioning.json");
            File.WriteAllText(settingsPath, "{\"InactivitySeconds\":123,\"DeviceId\":\"existing-device\"}");
            File.WriteAllText(provisioningPath, JsonConvert.SerializeObject(new
            {
                ServerUrl = "https://new.example.test",
                ServerApiKey = new string('b', 64)
            }));
            var settings = KioskSettings.Load(settingsPath);

            Assert.True(settings.ApplyProvisioningIfMissingServer(provisioningPath));
            Assert.Equal("https://new.example.test", settings.ServerUrl);
            Assert.Equal(new string('b', 64), settings.ServerApiKey);
            Assert.Equal(123, settings.InactivitySeconds);
            Assert.Equal("existing-device", settings.DeviceId);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConfiguredServer_IsNeverOverwritten()
    {
        string dir = NewTempDirectory();
        try
        {
            string provisioningPath = Path.Combine(dir, "KioskProvisioning.json");
            File.WriteAllText(provisioningPath, JsonConvert.SerializeObject(new
            {
                ServerUrl = "https://new.example.test",
                ServerApiKey = new string('b', 64)
            }));
            var settings = new KioskSettings
            {
                ServerUrl = "https://existing.example.test",
                ServerApiKey = "existing"
            };

            Assert.False(settings.ApplyProvisioningIfMissingServer(provisioningPath));
            Assert.Equal("https://existing.example.test", settings.ServerUrl);
            Assert.Equal("existing", settings.ServerApiKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void InvalidOrInsecureProvisioning_IsRejected()
    {
        string dir = NewTempDirectory();
        try
        {
            string provisioningPath = Path.Combine(dir, "KioskProvisioning.json");
            File.WriteAllText(provisioningPath, JsonConvert.SerializeObject(new
            {
                ServerUrl = "http://panel.example.test",
                ServerApiKey = new string('c', 64)
            }));
            var settings = new KioskSettings();

            Assert.False(settings.ApplyProvisioningIfMissingServer(provisioningPath));
            Assert.Null(settings.ServerUrl);
            Assert.Null(settings.ServerApiKey);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    private static string NewTempDirectory()
    {
        string path = Path.Combine(Path.GetTempPath(), "KioskSettingsTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
