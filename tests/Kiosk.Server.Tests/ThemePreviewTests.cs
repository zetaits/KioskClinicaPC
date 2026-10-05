using System.Net;
using Kiosk.Server.Components.Shared;
using Kiosk.Server.Services;
using KioskClinicaPC.Core.Config;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class ThemePreviewTests
{
    private static async Task<string> Render(AppConfig content, string screen, string condition = "Ocasion", int slide = 0, bool decode = true)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, services.GetRequiredService<ILoggerFactory>());
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var parameters = ParameterView.FromDictionary(new Dictionary<string, object?>
            { [nameof(ThemePreview.Content)] = content, [nameof(ThemePreview.Screen)] = screen, [nameof(ThemePreview.Condition)] = condition, [nameof(ThemePreview.SlideIndex)] = slide });
            var output = await renderer.RenderComponentAsync<ThemePreview>(parameters);
            return decode ? WebUtility.HtmlDecode(output.ToHtmlString()) : output.ToHtmlString();
        });
    }

    [Theory]
    [InlineData("Attract", "ESPERA", "navidad-hero.png", "attract.cta")]
    [InlineData("Scan", "ESCANEO", "navidad-compacto.png", "scan.progress")]
    [InlineData("Overview", "RESUMEN", "navidad-hero.png", "hud.detected")]
    [InlineData("Detail", "DETALLE", "navidad-horizontal.png", "card.verified")]
    public async Task Screens_show_their_own_layout_decoration_and_pending_text(string screen, string label, string asset, string key)
    {
        var draft = new EventEditorDraft(new()
        { Theme = ThemePresetCatalog.CreateSelection(ThemePresetCatalog.ChristmasId), UiTextOverrides = new() { [key] = "Texto pendiente de " + screen } });
        string html = await Render(draft.Preview(new()), screen);
        Assert.Contains($"data-preview-screen=\"{screen}\"", html);
        Assert.Contains(label, html);
        Assert.Contains(asset, html);
        Assert.Contains("Texto pendiente de " + screen, html);
        Assert.Contains(screen == "Scan" ? "preview-radar" : screen == "Overview" ? "preview-tiles" : screen == "Detail" ? "preview-meter" : "preview-cta", html);
    }

    [Theory]
    [InlineData("Ocasion", 0, "Ocasión 1")]
    [InlineData("Ocasion", 1, "Ocasión 2")]
    [InlineData("Nuevo", 0, "Nuevo 1")]
    [InlineData("Nuevo", 99, "Nuevo 2")]
    public async Task Preview_selects_correct_deck_and_clamps_slide_index(string condition, int index, string title)
    {
        var content = new AppConfig
        {
            AttractSlides = [new() { Title1 = "Ocasión 1" }, new() { Title1 = "Ocasión 2" }],
            AttractSlidesNew = [new() { Title1 = "Nuevo 1" }, new() { Title1 = "Nuevo 2" }]
        };
        Assert.Contains(title, await Render(content, "Attract", condition, index));
    }

    [Fact]
    public async Task No_theme_or_disabled_decorations_still_renders_screen_without_images()
    {
        var content = new AppConfig { VisualTheme = ThemePresetCatalog.Resolve(null) };
        string html = await Render(content, "Scan");
        Assert.Contains("preview-radar", html);
        Assert.DoesNotContain("<img", html);
        content.VisualTheme = ThemePresetCatalog.Resolve(new() { PresetId = ThemePresetCatalog.ChristmasId, DecorationsEnabled = false });
        Assert.DoesNotContain("<img", await Render(content, "Detail"));
    }

    [Fact]
    public async Task Custom_text_is_escaped_and_empty_override_uses_shared_fallback()
    {
        var content = new AppConfig { UiTexts = new() { ["attract.cta"] = "<script>alert(1)</script>", ["attract.hint"] = "" } };
        string html = await Render(content, "Attract", decode: false);
        Assert.DoesNotContain("<script", html);
        Assert.Contains("&lt;script&gt;", html);
        Assert.Contains(KioskContentDefaults.Texts["attract.hint"], WebUtility.HtmlDecode(html));
    }
}
