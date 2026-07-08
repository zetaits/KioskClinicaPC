using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class ConfigMergerTests
    {
        [Fact]
        public void Display_ConOverrideManual_DevuelveElManual()
        {
            Assert.Equal("Mi CPU", ConfigMerger.Display("Mi CPU", "Intel i5"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Display_SinManual_DevuelveElDetectado(string? manual)
        {
            Assert.Equal("Intel i5", ConfigMerger.Display(manual, "Intel i5"));
        }

        [Fact]
        public void Override_ManualDistintoDelDetectado_SeGuarda()
        {
            Assert.Equal("Mi CPU", ConfigMerger.Override("Mi CPU", "Intel i5"));
        }

        [Theory]
        [InlineData("Intel i5", "Intel i5")]   // igual al detectado → no es override
        [InlineData("INTEL I5", "intel i5")]   // igual ignorando mayúsculas
        [InlineData("", "Intel i5")]
        [InlineData(null, "Intel i5")]
        public void Override_VacioOIgualAlDetectado_DevuelveNull(string? manual, string? detected)
        {
            Assert.Null(ConfigMerger.Override(manual, detected));
        }

        [Theory]
        [InlineData("No detectada")]
        [InlineData("No detectado")]
        [InlineData("")]
        [InlineData(null)]
        public void NoPlaceholder_ValoresPlaceholderOVacios_DevuelveNull(string? v)
        {
            Assert.Null(ConfigMerger.NoPlaceholder(v));
        }

        [Fact]
        public void NoPlaceholder_ValorReal_SeConserva()
        {
            Assert.Equal("Cámara HD", ConfigMerger.NoPlaceholder("Cámara HD"));
        }

        [Fact]
        public void NormalizeOs_QuitaElNombreDePc()
        {
            Assert.Equal("Windows 11 Pro", ConfigMerger.NormalizeOs("Windows 11 Pro (DESKTOP-ABC)"));
        }

        [Fact]
        public void NormalizeOs_SinSufijo_QuedaIgual()
        {
            Assert.Equal("Windows 11 Pro", ConfigMerger.NormalizeOs("Windows 11 Pro"));
        }
    }
}
