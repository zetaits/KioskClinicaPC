using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class ComponentRegistryTests
    {
        [Theory]
        [InlineData(ComponentIds.Cpu)]
        [InlineData(ComponentIds.Gpu)]
        [InlineData(ComponentIds.Ram)]
        [InlineData(ComponentIds.Storage)]
        [InlineData(ComponentIds.Screen)]
        [InlineData(ComponentIds.Battery)]
        [InlineData(ComponentIds.Wifi)]
        [InlineData(ComponentIds.Camera)]
        [InlineData(ComponentIds.Ports)]
        [InlineData(ComponentIds.Os)]
        public void TryGet_ComponentesConocidos_Resuelven(string id)
        {
            Assert.True(ComponentRegistry.TryGet(id, out var accessor));
            Assert.Equal(id, accessor.Id);
        }

        [Theory]
        [InlineData("CPU")]
        [InlineData("Gpu")]
        public void TryGet_IgnoraMayusculas(string id)
        {
            Assert.True(ComponentRegistry.TryGet(id, out _));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("desconocido")]
        public void TryGet_IdInvalido_DevuelveFalse(string? id)
        {
            Assert.False(ComponentRegistry.TryGet(id, out _));
        }

        [Fact]
        public void GetSetValue_VanContraElCampoCorrecto()
        {
            var config = new AppConfig();
            Assert.True(ComponentRegistry.TryGet(ComponentIds.Ram, out var ram));

            ram.SetValue(config, "16 GB");
            Assert.Equal("16 GB", config.Ram);
            Assert.Equal("16 GB", ram.GetValue(config));
        }

        [Fact]
        public void GetDetail_Cpu_UsaCores()
        {
            var config = new AppConfig { Cores = "8 núcleos / 16 hilos" };
            Assert.True(ComponentRegistry.TryGet(ComponentIds.Cpu, out var cpu));
            Assert.Equal("8 núcleos / 16 hilos", cpu.GetDetail(config));
        }

        [Fact]
        public void GetDetail_Ram_UsaRamDetail()
        {
            var config = new AppConfig { RamDetail = "DDR5 5600" };
            Assert.True(ComponentRegistry.TryGet(ComponentIds.Ram, out var ram));
            Assert.Equal("DDR5 5600", ram.GetDetail(config));
        }
    }
}
