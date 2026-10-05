using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>Unsaved editor state. Preview and persistence use detached candidates.</summary>
public sealed class EventEditorDraft
{
    public KioskEvent Event { get; }
    public bool ThemeEnabled { get; set; }
    public bool SlidesEnabled { get; set; }
    public bool NewSlidesEnabled { get; set; }
    public bool TextsEnabled { get; set; }
    public List<TextEntry> Texts { get; private set; }

    public EventEditorDraft(KioskEvent source)
    {
        Event = Clone(source);
        Event.AttractSlides ??= new();
        Event.AttractSlidesNew ??= new();
        Event.UiTextOverrides ??= new();
        if (Event.Theme is { } theme)
        {
            theme.Scenes ??= new();
            theme.Scenes.Attract ??= new();
            theme.Scenes.Scan ??= new();
            theme.Scenes.Overview ??= new();
            theme.Scenes.Detail ??= new();
            foreach (var scene in new[] { theme.Scenes.Attract, theme.Scenes.Scan, theme.Scenes.Overview, theme.Scenes.Detail })
            { scene.Primary ??= new(); scene.Secondary ??= new(); }
        }
        ThemeEnabled = Event.Theme != null;
        SlidesEnabled = Event.AttractSlides.Count > 0;
        NewSlidesEnabled = Event.AttractSlidesNew.Count > 0;
        Texts = Event.UiTextOverrides.Select(x => new TextEntry { Key = x.Key, Value = x.Value }).ToList();
        TextsEnabled = Texts.Count > 0;
    }

    public KioskEvent Candidate(bool? enabled = null)
    {
        var candidate = Clone(Event);
        if (enabled.HasValue) candidate.Enabled = enabled.Value;
        if (!ThemeEnabled) candidate.Theme = null;
        if (!SlidesEnabled) candidate.AttractSlides.Clear();
        if (!NewSlidesEnabled) candidate.AttractSlidesNew.Clear();
        candidate.UiTextOverrides = !TextsEnabled ? new() : Texts
            .Where(x => !string.IsNullOrWhiteSpace(x.Key))
            .GroupBy(x => x.Key!.Trim())
            .ToDictionary(x => x.Key, x => x.Last().Value ?? "");
        return candidate;
    }

    public AppConfig Preview(AppConfig baseContent)
    {
        var preview = Clone(baseContent);
        // Preview the selected event even when it is a draft or outside its scheduled dates.
        EventContent.Apply(preview, Candidate());
        if (preview.AttractSlides?.Count is not > 0) preview.AttractSlides = KioskContentDefaults.SlidesUsed();
        if (preview.AttractSlidesNew?.Count is not > 0) preview.AttractSlidesNew = KioskContentDefaults.SlidesNew();
        return preview;
    }

    public void LoadCampaign(ThemePreset preset)
    {
        Event.AttractSlides = Clone(preset.CampaignSlides.ToList());
        Event.AttractSlidesNew = Clone(preset.CampaignSlides.ToList());
        Texts = preset.UiTexts.Select(x => new TextEntry { Key = x.Key, Value = x.Value }).ToList();
        SlidesEnabled = Event.AttractSlides.Count > 0;
        NewSlidesEnabled = Event.AttractSlidesNew.Count > 0;
        TextsEnabled = Texts.Count > 0;
    }

    // Include disabled content: it is still an unsaved change that must not be silently discarded.
    public string Fingerprint() => JsonConvert.SerializeObject(new
    {
        Event, ThemeEnabled, SlidesEnabled, NewSlidesEnabled, TextsEnabled, Texts
    });

    private static T Clone<T>(T value) => JsonConvert.DeserializeObject<T>(JsonConvert.SerializeObject(value))!;

    public sealed class TextEntry
    {
        public string? Key { get; set; }
        public string? Value { get; set; }
    }
}
