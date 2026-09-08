using System;
using System.Collections.Generic;
using KioskClinicaPC.Core.Config;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public sealed class EventRulesTests
    {
        private static readonly TimeZoneInfo Utc = TimeZoneInfo.Utc;

        [Fact]
        public void Publicar_bloquea_solape_pero_borrador_no()
        {
            var existing = Event("existente", new DateTime(2026, 12, 1), new DateTime(2026, 12, 10));
            existing.Enabled = true;
            var candidate = Event("nuevo", new DateTime(2026, 12, 5), new DateTime(2026, 12, 12));

            Assert.Contains(EventRules.Validate(candidate, new[] { existing }, Utc, publishing: true),
                x => x.Contains("solapan", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(EventRules.Validate(candidate, new[] { existing }, Utc, publishing: false),
                x => x.Contains("solapan", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Intervalos_adyacentes_no_se_solapan()
        {
            var first = Event("primero", new DateTime(2026, 12, 1), new DateTime(2026, 12, 10));
            first.Enabled = true;
            var second = Event("segundo", first.End, first.End.AddDays(2));

            Assert.Empty(EventRules.Validate(second, new[] { first }, Utc, publishing: true));
        }

        [Fact]
        public void Valida_nombre_y_rango()
        {
            var ev = Event("", new DateTime(2026, 1, 2), new DateTime(2026, 1, 1));
            IReadOnlyList<string> errors = EventRules.Validate(ev, Array.Empty<KioskEvent>(), Utc, false);
            Assert.True(errors.Count >= 2);
        }

        [Theory]
        [InlineData(2026, 3, 29, "inexistente")]
        [InlineData(2026, 10, 25, "ambigua")]
        public void Rechaza_horas_problematicas_del_cambio_horario(
            int year, int month, int day, string expected)
        {
            TimeZoneInfo madrid = TimeZoneInfo.FindSystemTimeZoneById(
                OperatingSystem.IsWindows() ? "Romance Standard Time" : "Europe/Madrid");
            var ev = Event("cambio horario", new DateTime(year, month, day, 2, 30, 0),
                new DateTime(year, month, day, 4, 0, 0));

            Assert.Contains(EventRules.Validate(ev, Array.Empty<KioskEvent>(), madrid, false),
                error => error.Contains(expected, StringComparison.OrdinalIgnoreCase));
        }

        private static KioskEvent Event(string name, DateTime start, DateTime end) => new()
        { Name = name, Start = start, End = end };
    }
}
