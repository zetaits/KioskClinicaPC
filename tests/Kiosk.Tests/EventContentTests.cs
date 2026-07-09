using System;
using System.Collections.Generic;
using KioskClinicaPC.Core.Config;
using Xunit;

namespace KioskClinicaPC.Tests
{
    /// <summary>Vigencia y aplicación de eventos: qué evento manda en cada instante y cómo sobrepone el
    /// contenido compartido sin tocar lo local (precio/specs).</summary>
    public sealed class EventContentTests
    {
        private static KioskEvent Event(string name, DateTime start, DateTime end, bool enabled = true) => new()
        {
            Name = name, Start = start, End = end, Enabled = enabled
        };

        private static readonly DateTime Now = new(2026, 11, 27, 12, 0, 0); // Black Friday

        [Fact]
        public void Activo_dentro_del_rango()
        {
            var ev = Event("BF", Now.AddDays(-1), Now.AddDays(2));
            Assert.True(ev.IsActiveAt(Now));
        }

        [Fact]
        public void Inactivo_fuera_del_rango_o_desactivado()
        {
            Assert.False(Event("pasado", Now.AddDays(-5), Now.AddDays(-1)).IsActiveAt(Now));
            Assert.False(Event("futuro", Now.AddDays(1), Now.AddDays(3)).IsActiveAt(Now));
            Assert.False(Event("off", Now.AddDays(-1), Now.AddDays(1), enabled: false).IsActiveAt(Now));
        }

        [Fact]
        public void Fin_es_exclusivo_inicio_inclusivo()
        {
            var ev = Event("x", Now, Now.AddHours(1));
            Assert.True(ev.IsActiveAt(Now));                    // inicio inclusivo
            Assert.False(ev.IsActiveAt(Now.AddHours(1)));       // fin exclusivo
        }

        [Fact]
        public void Solapados_gana_el_de_inicio_mas_reciente()
        {
            var events = new List<KioskEvent>
            {
                Event("viejo", Now.AddDays(-3), Now.AddDays(3)),
                Event("nuevo", Now.AddDays(-1), Now.AddDays(1)),
            };
            Assert.Equal("nuevo", EventContent.ActiveAt(events, Now)!.Name);
        }

        [Fact]
        public void Sin_eventos_activos_devuelve_null()
        {
            var events = new List<KioskEvent> { Event("futuro", Now.AddDays(1), Now.AddDays(2)) };
            Assert.Null(EventContent.ActiveAt(events, Now));
        }

        [Fact]
        public void Apply_reemplaza_slides_y_funde_textos_sin_tocar_precio()
        {
            var baseCfg = new AppConfig
            {
                Price = "499",
                AttractSlides = new() { new AttractSlide { Title1 = "Base" } },
                UiTexts = new() { ["cta"] = "Compra", ["footer"] = "Gracias" }
            };
            var ev = Event("BF", Now.AddDays(-1), Now.AddDays(1));
            ev.AttractSlides.Add(new AttractSlide { Title1 = "BLACK FRIDAY" });
            ev.UiTextOverrides["cta"] = "¡-40%!";

            EventContent.Apply(baseCfg, ev);

            Assert.Equal("BLACK FRIDAY", Assert.Single(baseCfg.AttractSlides).Title1); // slides reemplazados
            Assert.Equal("¡-40%!", baseCfg.UiTexts["cta"]);   // texto sobrepuesto
            Assert.Equal("Gracias", baseCfg.UiTexts["footer"]); // texto no tocado se conserva
            Assert.Equal("499", baseCfg.Price);                // precio local intacto
        }

        [Fact]
        public void Apply_con_slides_vacios_no_borra_los_base()
        {
            var baseCfg = new AppConfig { AttractSlides = new() { new AttractSlide { Title1 = "Base" } } };
            var ev = Event("solo-texto", Now.AddDays(-1), Now.AddDays(1));
            ev.UiTextOverrides["x"] = "y"; // evento sin slides

            EventContent.Apply(baseCfg, ev);

            Assert.Equal("Base", Assert.Single(baseCfg.AttractSlides).Title1); // se conservan los base
        }
    }
}
