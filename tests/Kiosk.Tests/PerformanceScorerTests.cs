using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class PerformanceScorerTests
    {
        // --- Score: rango y cálculo --------------------------------------------------------

        [Theory]
        [InlineData(ComponentIds.Cpu)]
        [InlineData(ComponentIds.Gpu)]
        [InlineData(ComponentIds.Ram)]
        [InlineData(ComponentIds.Storage)]
        [InlineData(ComponentIds.Screen)]
        [InlineData(ComponentIds.Battery)]
        [InlineData(ComponentIds.Wifi)]
        public void Score_ComponentesPuntuables_SiempreEntre70y100(string id)
        {
            // Hardware "flojo" no debe bajar de 70 (nunca un número feo).
            var weak = new AppConfig
            {
                Cores = "2 Núcleos / 4 Hilos",
                Ram = "4 GB",
                Gpu = "Intel UHD Graphics",
                Storage = "Generic (120 GB HDD)",
                Screen = "1366 x 768",
                Battery = "20 Wh",
                Wifi = "Realtek 802.11n"
            };
            int s = PerformanceScorer.Score(id, weak);
            Assert.InRange(s, 70, 100);
        }

        [Fact]
        public void Score_HardwarePotente_SeRecortaA100()
        {
            var beast = new AppConfig { Cores = "24 Núcleos / 32 Hilos" };
            Assert.Equal(100, PerformanceScorer.Score(ComponentIds.Cpu, beast));
        }

        [Fact]
        public void Score_Cpu_8c16t_DaGamaAlta()
        {
            var hw = new AppConfig { Cores = "8 Núcleos / 16 Hilos" };
            int s = PerformanceScorer.Score(ComponentIds.Cpu, hw);
            Assert.Equal(92, s);
        }

        [Fact]
        public void Score_Ram_32GbDdr5()
        {
            var hw = new AppConfig { Ram = "32 GB DDR5 (Kingston KF @ 4800 MHz)" };
            Assert.Equal(90, PerformanceScorer.Score(ComponentIds.Ram, hw));
        }

        [Fact]
        public void Score_Gpu_Rtx4060()
        {
            var hw = new AppConfig { Gpu = "NVIDIA GeForce RTX 4060 Laptop GPU" };
            Assert.Equal(94, PerformanceScorer.Score(ComponentIds.Gpu, hw));
        }

        [Theory]
        [InlineData(ComponentIds.Camera)]
        [InlineData(ComponentIds.Ports)]
        [InlineData(ComponentIds.Os)]
        public void Score_ComponentesSinPotencia_DevuelveCero(string id)
        {
            Assert.Equal(0, PerformanceScorer.Score(id, new AppConfig { Os = "Windows 11 Home" }));
        }

        [Fact]
        public void Score_ConfigNula_DevuelveCero()
        {
            Assert.Equal(0, PerformanceScorer.Score(ComponentIds.Cpu, null!));
        }

        // --- TierLabel: bandas × matiz por componente, nunca "baja" -------------------------

        [Fact]
        public void TierLabel_Gpu_BandaAlta_EsGaming()
        {
            Assert.Equal("Gama gaming", PerformanceScorer.TierLabel(ComponentIds.Gpu, 94));
            Assert.Equal("Élite gaming", PerformanceScorer.TierLabel(ComponentIds.Gpu, 97));
        }

        [Fact]
        public void TierLabel_Battery_UsaMatizDeAutonomia()
        {
            Assert.Equal("Buena autonomía", PerformanceScorer.TierLabel(ComponentIds.Battery, 90));
        }

        [Theory]
        [InlineData(ComponentIds.Cpu, 70)]
        [InlineData(ComponentIds.Ram, 75)]
        [InlineData(ComponentIds.Storage, 72)]
        [InlineData(ComponentIds.Screen, 74)]
        public void TierLabel_NuncaDiceBajaNiLigero(string id, int score)
        {
            string label = PerformanceScorer.TierLabel(id, score);
            Assert.DoesNotContain("baja", label, System.StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ligero", label, System.StringComparison.OrdinalIgnoreCase);
            Assert.False(string.IsNullOrWhiteSpace(label));
        }
    }
}
