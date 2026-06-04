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
