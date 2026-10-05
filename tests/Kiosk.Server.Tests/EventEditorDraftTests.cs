using Kiosk.Server.Services;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;
using Xunit;

namespace Kiosk.Server.Tests;

public sealed class EventEditorDraftTests
{
    private static KioskEvent Campaign() => new()
    {
        Name = "Campaña", Enabled = true, Start = new(2026, 12, 1), End = new(2026, 12, 8),
        Theme = ThemePresetCatalog.CreateSelection(ThemePresetCatalog.ChristmasId),
        AttractSlides = [new() { Title1 = "Ocasión" }],
        AttractSlidesNew = [new() { Title1 = "Nuevo" }],
        UiTextOverrides = new() { ["attract.cta"] = "Original" }
    };

    [Fact]
    public void Preview_uses_unsaved_text_and_both_decks_without_mutating_base_or_saved_event()
    {
        var saved = Campaign();
        var content = new AppConfig
        {
            AttractSlides = [new() { Title1 = "Base" }],
            AttractSlidesNew = [new() { Title1 = "Base nuevo" }],
            UiTexts = new() { ["attract.cta"] = "Base", ["scan.progress"] = "Avance base" }
        };
        string original = JsonConvert.SerializeObject(new { saved, content });
        var draft = new EventEditorDraft(saved);
        draft.Event.AttractSlides[0].Title1 = "Pendiente ocasión";
        draft.Event.AttractSlidesNew[0].Title1 = "Pendiente nuevo";
        draft.Texts[0].Value = "Pendiente";

        var preview = draft.Preview(content);
        Assert.Equal("Pendiente ocasión", preview.AttractSlides[0].Title1);
        Assert.Equal("Pendiente nuevo", preview.AttractSlidesNew[0].Title1);
        Assert.Equal("Pendiente", preview.UiTexts["attract.cta"]);
        Assert.Equal("Avance base", preview.UiTexts["scan.progress"]);
        Assert.Equal(ThemePresetCatalog.ChristmasId, preview.VisualTheme!.Id);
        preview.AttractSlides[0].Title1 = "No debe volver al borrador";
        Assert.Equal("Pendiente ocasión", draft.Event.AttractSlides[0].Title1);
        Assert.Equal(original, JsonConvert.SerializeObject(new { saved, content }));
    }

    [Fact]
    public void Disabled_options_preview_base_and_reenable_retained_edits()
    {
        var draft = new EventEditorDraft(Campaign());
        var content = new AppConfig
        {
            AttractSlides = [new() { Title1 = "Base ocasión" }],
            AttractSlidesNew = [new() { Title1 = "Base nuevo" }],
            UiTexts = new() { ["attract.cta"] = "Base" }
        };
        draft.ThemeEnabled = draft.SlidesEnabled = draft.NewSlidesEnabled = draft.TextsEnabled = false;
        var preview = draft.Preview(content);
        Assert.Equal("Base ocasión", preview.AttractSlides[0].Title1);
        Assert.Equal("Base nuevo", preview.AttractSlidesNew[0].Title1);
        Assert.Equal("Base", preview.UiTexts["attract.cta"]);
        Assert.Null(preview.VisualTheme);
        var candidate = draft.Candidate(false);
        Assert.Empty(candidate.AttractSlides);
        Assert.Empty(candidate.AttractSlidesNew);
        Assert.Empty(candidate.UiTextOverrides);
        Assert.Null(candidate.Theme);

        draft.ThemeEnabled = draft.SlidesEnabled = draft.NewSlidesEnabled = draft.TextsEnabled = true;
        preview = draft.Preview(content);
        Assert.Equal("Ocasión", preview.AttractSlides[0].Title1);
        Assert.Equal("Nuevo", preview.AttractSlidesNew[0].Title1);
        Assert.Equal("Original", preview.UiTexts["attract.cta"]);
        Assert.NotNull(preview.VisualTheme);
        Assert.True(draft.Event.Enabled);
    }

    [Fact]
    public void Failed_validation_does_not_unpublish_or_remove_disabled_content()
    {
        var draft = new EventEditorDraft(Campaign()) { SlidesEnabled = false };
        draft.Event.End = draft.Event.Start;
        string before = draft.Fingerprint();
        var candidate = draft.Candidate(false);
        Assert.NotEmpty(EventRules.Validate(candidate, [], TimeZoneInfo.Utc, false));
        Assert.Equal(before, draft.Fingerprint());
        Assert.True(draft.Event.Enabled);
        Assert.Single(draft.Event.AttractSlides);
    }

    [Fact]
    public void Empty_decks_use_the_same_fallback_slides_as_the_client()
    {
        var preview = new EventEditorDraft(new() { Enabled = false }).Preview(new());
        Assert.Equal(JsonConvert.SerializeObject(KioskContentDefaults.SlidesUsed()), JsonConvert.SerializeObject(preview.AttractSlides));
        Assert.Equal(JsonConvert.SerializeObject(KioskContentDefaults.SlidesNew()), JsonConvert.SerializeObject(preview.AttractSlidesNew));
        Assert.NotEqual(preview.AttractSlides[2].Title1, preview.AttractSlidesNew[2].Title1);
    }

    [Fact]
    public void Text_keys_are_trimmed_last_duplicate_wins_and_unknown_keys_are_preserved()
    {
        var draft = new EventEditorDraft(new()) { TextsEnabled = true };
        draft.Texts.AddRange([new() { Key = " attract.cta ", Value = "Primero" }, new() { Key = "attract.cta", Value = "Último" }, new() { Key = "custom.key", Value = "Personalizado" }, new() { Key = " " }]);
        var candidate = draft.Candidate();
        Assert.Equal(2, candidate.UiTextOverrides.Count);
        Assert.Equal("Último", candidate.UiTextOverrides["attract.cta"]);
        Assert.Equal("Personalizado", candidate.UiTextOverrides["custom.key"]);
    }

    [Fact]
    public void Loading_campaign_creates_independent_decks_and_preserves_schedule_and_identity()
    {
        var draft = new EventEditorDraft(Campaign());
        string id = draft.Event.Id;
        DateTime start = draft.Event.Start;
        var preset = ThemePresetCatalog.Find(ThemePresetCatalog.ChristmasId)!;
        draft.LoadCampaign(preset);
        draft.Event.AttractSlides[0].Title1 = "Solo ocasión";
        Assert.NotEqual("Solo ocasión", draft.Event.AttractSlidesNew[0].Title1);
        Assert.NotEqual("Solo ocasión", preset.CampaignSlides[0].Title1);
        Assert.Equal(id, draft.Event.Id);
        Assert.Equal(start, draft.Event.Start);
        Assert.True(draft.TextsEnabled && draft.SlidesEnabled && draft.NewSlidesEnabled);
    }

    [Fact]
    public void Disabled_content_is_included_in_dirty_tracking()
    {
        var draft = new EventEditorDraft(Campaign()) { SlidesEnabled = false };
        string before = draft.Fingerprint();
        draft.Event.AttractSlides[0].Title1 = "Cambio oculto";
        Assert.NotEqual(before, draft.Fingerprint());
    }
}
