using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class SpecFormatterTests
    {
        [Fact]
        public void Wifi_ReconoceEstandar6E_YLimpiaRuido()
        {
            var r = SpecFormatter.Format("wifi", "Intel(R) Wi-Fi 6E AX211 160MHz");
            Assert.Equal("WiFi 6E", r.Headline);
        }

        [Fact]
        public void Storage_SeparaCapacidadYModelo_YConvierteATB()
        {
            var r = SpecFormatter.Format("storage", "Samsung PM9A1 (1024 GB SSD)");
            Assert.Equal("SSD 1 TB", r.Headline);
            Assert.Equal("Samsung PM9A1", r.Tech);
        }

        [Fact]
        public void Screen_TraduceResolucionANombreComercial()
        {
            var r = SpecFormatter.Format("screen", "2560 x 1600");
            Assert.Equal("QHD+", r.Headline);
            Assert.Equal("2560 × 1600 px", r.Tech);
        }

        [Fact]
        public void Cpu_QuitaSufijoRadeonGraphics_RedundanteConLaGpu()
        {
            var r = SpecFormatter.Format("cpu", "AMD Ryzen 7 4700U with Radeon Graphics");
            Assert.Equal("AMD Ryzen 7 4700U", r.Headline);
        }

        [Fact]
        public void Cpu_QuitaVarianteVega_YRuidoDeMarca()
        {
            var r = SpecFormatter.Format("cpu", "AMD Ryzen 5 4500U with Radeon Vega Graphics");
            Assert.Equal("AMD Ryzen 5 4500U", r.Headline);
        }

        [Fact]
        public void Cpu_IntelSinSufijo_SeMantieneLimpio()
        {
            var r = SpecFormatter.Format("cpu", "Intel(R) Core(TM) i7-1165G7");
            Assert.Equal("Intel Core i7-1165G7", r.Headline);
        }

        [Fact]
        public void Gpu_ConservaRadeonGraphics()
        {
            var r = SpecFormatter.Format("gpu", "AMD Radeon Graphics");
            Assert.Equal("AMD Radeon", r.Headline);
        }

        [Fact]
        public void ValorVacio_DevuelveNulos()
        {
            var r = SpecFormatter.Format("cpu", "");
            Assert.Null(r.Headline);
            Assert.Null(r.Tech);
        }

        [Fact]
        public void IdDesconocido_DevuelveElValorTalCual()
        {
            var r = SpecFormatter.Format("desconocido", "Texto libre");
            Assert.Equal("Texto libre", r.Headline);
            Assert.Null(r.Tech);
        }
    }
}
