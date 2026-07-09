using KioskClinicaPC.Core.Sync;
using Xunit;

namespace KioskClinicaPC.Tests
{
    /// <summary>La fórmula del índice de slide es el contrato de sincronización: dado el mismo estado y
    /// la misma hora, todos los kioscos DEBEN calcular el mismo slide. Estos tests fijan ese contrato.</summary>
    public sealed class AttractSyncStateTests
    {
        private static AttractSyncState State(long epoch, int durationMs) => new AttractSyncState
        {
            EpochUnixMs = epoch,
            SlideDurationMs = durationMs,
            ServerTimeUnixMs = epoch
        };

        [Fact]
        public void En_el_origen_es_el_primer_slide()
        {
            var s = State(1_000_000, 5000);
            Assert.Equal(0, s.SlideIndexAt(1_000_000, 4));
        }

        [Theory]
        [InlineData(0, 0)]      // t=0        → slide 0
        [InlineData(4999, 0)]   // fin slide 0 → aún 0
        [InlineData(5000, 1)]   // frontera    → slide 1
        [InlineData(10000, 2)]  //             → slide 2
        [InlineData(15000, 0)]  // envuelve (3 slides) → vuelve a 0
        [InlineData(20000, 1)]  // sigue envolviendo
        public void Avanza_y_envuelve_por_duracion(long elapsedMs, int expected)
        {
            var s = State(1_000_000, 5000);
            Assert.Equal(expected, s.SlideIndexAt(1_000_000 + elapsedMs, slideCount: 3));
        }

        [Fact]
        public void Dos_clientes_con_la_misma_hora_calculan_el_mismo_slide()
        {
            var s = State(500, 5200);
            long now = 500 + 5200 * 7 + 100; // en algún punto del 8º slide
            Assert.Equal(s.SlideIndexAt(now, 5), s.SlideIndexAt(now, 5));
            Assert.Equal(7 % 5, s.SlideIndexAt(now, 5));
        }

        [Fact]
        public void Antes_del_origen_no_va_negativo()
        {
            var s = State(1_000_000, 5000);
            Assert.Equal(0, s.SlideIndexAt(999_000, 4)); // reloj cliente por detrás → slide 0, no crash
        }

        [Fact]
        public void Sin_slides_o_duracion_invalida_devuelve_cero()
        {
            Assert.Equal(0, State(0, 5000).SlideIndexAt(9999, slideCount: 0));
            Assert.Equal(0, State(0, 0).SlideIndexAt(9999, slideCount: 4));
        }
    }
}
