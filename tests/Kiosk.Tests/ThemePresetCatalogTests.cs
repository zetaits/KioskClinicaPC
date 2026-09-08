using System;
using System.Linq;
using KioskClinicaPC.Core.Config;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public sealed class ThemePresetCatalogTests
    {
        [Fact]
        public void Catalogo_incluye_tema_base_y_cuatro_campanas()
        {
            Assert.Equal(5, ThemePresetCatalog.All.Count);
            Assert.Contains(ThemePresetCatalog.All, x => x.Id == ThemePresetCatalog.ChristmasId);
            Assert.All(ThemePresetCatalog.All, x =>
                Assert.True(ThemeColor.Contrast(x.Palette.Primary, x.Palette.Background0) >= 4.5));
        }

        [Fact]
        public void Resolve_aplica_acentos_intensidad_y_slots()
        {
            var selection = ThemePresetCatalog.CreateSelection(ThemePresetCatalog.ChristmasId);
            selection.PrimaryAccent = "#66CCFF";
            selection.Intensity = 130;
            selection.Scenes.Attract.Primary.Mode = ThemeSlotMode.Hidden;
            selection.Scenes.Attract.Secondary.Mode = ThemeSlotMode.Library;
            selection.Scenes.Attract.Secondary.AssetKey = "mi-adorno.png";

            ResolvedVisualTheme theme = ThemePresetCatalog.Resolve(selection);

            Assert.Equal("#66CCFF", theme.Palette.Primary);
            Assert.Equal(100, theme.Intensity);
            Assert.Null(theme.Scenes.Attract.PrimaryAssetKey);
            Assert.Equal("mi-adorno.png", theme.Scenes.Attract.SecondaryAssetKey);
        }

        [Theory]
        [InlineData("../secreto.png")]
        [InlineData("https://host/a.png")]
        [InlineData("adorno.svg")]
        [InlineData("")]
        public void Asset_key_rechaza_referencias_no_seguras(string key) =>
            Assert.False(ThemeAssetKey.IsSafe(key));

        [Fact]
        public void Evento_aplica_tema_sin_tocar_precio()
        {
            var config = new AppConfig { Price = "799" };
            var ev = new KioskEvent
            {
                Start = DateTime.Today.AddDays(-1), End = DateTime.Today.AddDays(1),
                Theme = ThemePresetCatalog.CreateSelection(ThemePresetCatalog.BlackFridayId)
            };

            EventContent.Apply(config, ev);

            Assert.Equal("799", config.Price);
            Assert.Equal(ThemePresetCatalog.BlackFridayId, config.VisualTheme?.Id);
        }

        [Fact]
        public void Shared_content_copia_null_para_retirar_tema_cacheado()
        {
            var local = new AppConfig { VisualTheme = ThemePresetCatalog.Resolve(
                ThemePresetCatalog.CreateSelection(ThemePresetCatalog.SalesId)) };
            SharedContent.ApplyServerToLocal(local, new AppConfig());
            Assert.Null(local.VisualTheme);
        }
    }
}
