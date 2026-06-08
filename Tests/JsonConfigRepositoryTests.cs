using System;
using System.IO;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Services;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class JsonConfigRepositoryTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _configPath;
        private readonly string _hardwarePath;
        private readonly JsonConfigRepository _repo;

        public JsonConfigRepositoryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "KioskRepoTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _configPath = Path.Combine(_dir, "KioskConfig.json");
            _hardwarePath = Path.Combine(_dir, "KioskHardware.json");
            _repo = new JsonConfigRepository(_configPath, _hardwarePath);
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
        }

        [Fact]
        public async Task LoadConfig_SinArchivo_DevuelveDefaultLimpio()
        {
            var result = await _repo.LoadConfigAsync();
            Assert.False(result.WasCorrupt);
            Assert.False(result.Migrated);
            Assert.NotNull(result.Config);
        }

        [Fact]
        public async Task SaveConfig_LuegoLoad_RoundTrip()
        {
            _repo.SaveConfig(new AppConfig { Price = "599", Cpu = "Intel i7", SchemaVersion = AppConfig.CurrentSchemaVersion });

            var result = await _repo.LoadConfigAsync();

            Assert.False(result.WasCorrupt);
            Assert.Equal("599", result.Config.Price);
            Assert.Equal("Intel i7", result.Config.Cpu);
        }

        [Fact]
        public async Task LoadConfig_ArchivoCorrupto_RespaldaYDevuelveWasCorrupt()
        {
            File.WriteAllText(_configPath, "{ esto no es json válido ");

            var result = await _repo.LoadConfigAsync();

            Assert.True(result.WasCorrupt);
            Assert.NotNull(result.Config);
            // El original dañado se ha movido a un .corrupt-*.bak (no se pierde).
            Assert.NotEmpty(Directory.GetFiles(_dir, "*.corrupt-*.bak"));
        }

        [Fact]
        public async Task LoadLastHardware_SinArchivo_DevuelveVacioSinLanzar()
        {
            var hw = await _repo.LoadLastHardwareAsync();
            Assert.NotNull(hw);
        }

        [Fact]
        public async Task SaveHardware_LuegoLoad_RoundTrip()
        {
            _repo.SaveHardware(new AppConfig { Cpu = "AMD Ryzen 5" });
            var hw = await _repo.LoadLastHardwareAsync();
            Assert.Equal("AMD Ryzen 5", hw.Cpu);
        }
    }
}
