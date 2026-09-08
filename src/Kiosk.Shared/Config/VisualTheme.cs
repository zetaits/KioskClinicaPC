using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace KioskClinicaPC.Core.Config
{
    public enum ThemeAmbientEffect
    {
        Default,
        Snow,
        Spotlights,
        Confetti,
        Notebook
    }

    public enum ThemeSlotMode
    {
        Preset,
        Hidden,
        Library
    }

    public sealed class ThemePalette
    {
        public string Background0 { get; set; } = "#04020A";
        public string Background1 { get; set; } = "#0A0716";
        public string Background2 { get; set; } = "#140B24";
        public string Background3 { get; set; } = "#1C1135";
        public string Primary { get; set; } = "#F37A4A";
        public string Secondary { get; set; } = "#FFB069";
        public string Highlight { get; set; } = "#F0D26B";
        public string Text0 { get; set; } = "#FFFFFF";
        public string Text1 { get; set; } = "#D6D2E8";
        public string Text2 { get; set; } = "#A59EC2";
        public string Text3 { get; set; } = "#6F6890";

        public ThemePalette Clone() => new()
        {
            Background0 = Background0,
            Background1 = Background1,
            Background2 = Background2,
            Background3 = Background3,
            Primary = Primary,
            Secondary = Secondary,
            Highlight = Highlight,
            Text0 = Text0,
            Text1 = Text1,
            Text2 = Text2,
            Text3 = Text3
        };
    }

    public sealed class ThemeSlotOverride
    {
        public ThemeSlotMode Mode { get; set; } = ThemeSlotMode.Preset;
        public string? AssetKey { get; set; }
    }

    public sealed class ThemeSceneOverride
    {
        public ThemeSlotOverride Primary { get; set; } = new();
        public ThemeSlotOverride Secondary { get; set; } = new();
    }

    public sealed class ThemeSceneOverrides
    {
        public ThemeSceneOverride Attract { get; set; } = new();
        public ThemeSceneOverride Scan { get; set; } = new();
        public ThemeSceneOverride Overview { get; set; } = new();
        public ThemeSceneOverride Detail { get; set; } = new();
    }

    /// <summary>Elecciones editables que persisten en events.json.</summary>
    public sealed class EventThemeSelection
    {
        public string PresetId { get; set; } = ThemePresetCatalog.ClinicPcId;
        public string? PrimaryAccent { get; set; }
        public string? SecondaryAccent { get; set; }
        public int Intensity { get; set; } = 70;
        public bool DecorationsEnabled { get; set; } = true;
        public ThemeSceneOverrides Scenes { get; set; } = new();
    }

    public sealed class ResolvedThemeScene
    {
        public string? PrimaryAssetKey { get; set; }
        public string? SecondaryAssetKey { get; set; }
    }

    public sealed class ResolvedThemeScenes
    {
        public ResolvedThemeScene Attract { get; set; } = new();
        public ResolvedThemeScene Scan { get; set; } = new();
        public ResolvedThemeScene Overview { get; set; } = new();
        public ResolvedThemeScene Detail { get; set; } = new();
    }

    /// <summary>Contrato resuelto que viaja en /api/config. El cliente no necesita conocer el preset.</summary>
    public sealed class ResolvedVisualTheme
    {
        public string Id { get; set; } = ThemePresetCatalog.ClinicPcId;
        public ThemePalette Palette { get; set; } = new();
        public ThemeAmbientEffect AmbientEffect { get; set; }
        public int Intensity { get; set; } = 70;
        public bool DecorationsEnabled { get; set; }
        public ResolvedThemeScenes Scenes { get; set; } = new();
    }

    public sealed class ThemePreset
    {
        public string Id { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Description { get; init; } = "";
        public ThemePalette Palette { get; init; } = new();
        public ThemeAmbientEffect AmbientEffect { get; init; }
        public int DefaultIntensity { get; init; } = 70;
        public ResolvedThemeScenes Scenes { get; init; } = new();
        public IReadOnlyList<AttractSlide> CampaignSlides { get; init; } = Array.Empty<AttractSlide>();
        public IReadOnlyDictionary<string, string> UiTexts { get; init; } =
            new Dictionary<string, string>();
    }

    public static class ThemeAssetKey
    {
        public static bool IsSafe(string? key)
        {
            if (string.IsNullOrWhiteSpace(key) || key.Length > 128) return false;
            if (!key.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return false;
            return key.All(c => char.IsLetterOrDigit(c) || c is '-' or '_' or '.');
        }
    }

    public static class ThemeColor
    {
        public static bool TryNormalize(string? value, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrWhiteSpace(value)) return false;
            string raw = value.Trim().TrimStart('#');
            if (raw.Length != 6 || !int.TryParse(raw, NumberStyles.HexNumber,
                    CultureInfo.InvariantCulture, out _)) return false;
            normalized = "#" + raw.ToUpperInvariant();
            return true;
        }

        public static double Contrast(string first, string second)
        {
            static double Luminance(string value)
            {
                string raw = value.TrimStart('#');
                int r = int.Parse(raw[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int g = int.Parse(raw.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                int b = int.Parse(raw.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                static double Channel(int c)
                {
                    double value = c / 255d;
                    return value <= .04045 ? value / 12.92 : Math.Pow((value + .055) / 1.055, 2.4);
                }
                return .2126 * Channel(r) + .7152 * Channel(g) + .0722 * Channel(b);
            }

            double a = Luminance(first);
            double b = Luminance(second);
            return (Math.Max(a, b) + .05) / (Math.Min(a, b) + .05);
        }
    }

    public static class ThemePresetCatalog
    {
        public const string ClinicPcId = "clinic-pc";
        public const string ChristmasId = "navidad";
        public const string BlackFridayId = "black-friday";
        public const string SalesId = "rebajas";
        public const string BackToSchoolId = "vuelta-al-cole";

        private static readonly IReadOnlyList<ThemePreset> Presets = BuildPresets();

        public static IReadOnlyList<ThemePreset> All => Presets;

        public static ThemePreset? Find(string? id) => Presets.FirstOrDefault(p =>
            string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        public static EventThemeSelection CreateSelection(string id)
        {
            ThemePreset preset = Find(id) ?? Presets[0];
            return new EventThemeSelection
            {
                PresetId = preset.Id,
                Intensity = preset.DefaultIntensity,
                DecorationsEnabled = preset.Id != ClinicPcId
            };
        }

        public static ResolvedVisualTheme Resolve(EventThemeSelection? selection)
        {
            ThemePreset preset = Find(selection?.PresetId) ?? Presets[0];
            ThemePalette palette = preset.Palette.Clone();
            if (ThemeColor.TryNormalize(selection?.PrimaryAccent, out string primary)) palette.Primary = primary;
            if (ThemeColor.TryNormalize(selection?.SecondaryAccent, out string secondary)) palette.Secondary = secondary;

            ThemeSceneOverrides overrides = selection?.Scenes ?? new ThemeSceneOverrides();
            bool decorations = selection?.DecorationsEnabled ?? preset.Id != ClinicPcId;
            return new ResolvedVisualTheme
            {
                Id = preset.Id,
                Palette = palette,
                AmbientEffect = preset.AmbientEffect,
                Intensity = Math.Clamp(selection?.Intensity ?? preset.DefaultIntensity, 0, 100),
                DecorationsEnabled = decorations,
                Scenes = decorations ? new ResolvedThemeScenes
                {
                    Attract = ResolveScene(preset.Scenes.Attract, overrides.Attract),
                    Scan = ResolveScene(preset.Scenes.Scan, overrides.Scan),
                    Overview = ResolveScene(preset.Scenes.Overview, overrides.Overview),
                    Detail = ResolveScene(preset.Scenes.Detail, overrides.Detail)
                } : new ResolvedThemeScenes()
            };
        }

        public static IReadOnlyList<string> Validate(EventThemeSelection? selection)
        {
            var errors = new List<string>();
            if (selection == null) return errors;
            ThemePreset? preset = Find(selection.PresetId);
            if (preset == null) errors.Add("El tema seleccionado ya no existe.");
            if (selection.Intensity is < 0 or > 100) errors.Add("La intensidad debe estar entre 0 y 100.");
            ValidateAccent(selection.PrimaryAccent, preset?.Palette.Background0, "principal", errors);
            ValidateAccent(selection.SecondaryAccent, preset?.Palette.Background0, "secundario", errors);
            foreach (ThemeSceneOverride scene in EnumerateScenes(selection.Scenes))
            {
                ValidateSlot(scene.Primary, errors);
                ValidateSlot(scene.Secondary, errors);
            }
            return errors;
        }

        private static void ValidateAccent(string? value, string? background, string name, List<string> errors)
        {
            if (string.IsNullOrWhiteSpace(value)) return;
            if (!ThemeColor.TryNormalize(value, out string normalized))
            {
                errors.Add($"El acento {name} no es un color hexadecimal válido.");
                return;
            }
            if (background != null && ThemeColor.Contrast(normalized, background) < 4.5)
                errors.Add($"El acento {name} no tiene suficiente contraste con el fondo.");
        }

        private static void ValidateSlot(ThemeSlotOverride? slot, List<string> errors)
        {
            if (slot?.Mode == ThemeSlotMode.Library && !ThemeAssetKey.IsSafe(slot.AssetKey))
                errors.Add("Una imagen decorativa tiene una referencia no válida.");
        }

        private static IEnumerable<ThemeSceneOverride> EnumerateScenes(ThemeSceneOverrides? scenes)
        {
            scenes ??= new ThemeSceneOverrides();
            yield return scenes.Attract ?? new ThemeSceneOverride();
            yield return scenes.Scan ?? new ThemeSceneOverride();
            yield return scenes.Overview ?? new ThemeSceneOverride();
            yield return scenes.Detail ?? new ThemeSceneOverride();
        }

        private static ResolvedThemeScene ResolveScene(ResolvedThemeScene preset, ThemeSceneOverride? value) => new()
        {
            PrimaryAssetKey = ResolveSlot(preset.PrimaryAssetKey, value?.Primary),
            SecondaryAssetKey = ResolveSlot(preset.SecondaryAssetKey, value?.Secondary)
        };

        private static string? ResolveSlot(string? preset, ThemeSlotOverride? value) => value?.Mode switch
        {
            ThemeSlotMode.Hidden => null,
            ThemeSlotMode.Library when ThemeAssetKey.IsSafe(value.AssetKey) => value.AssetKey,
            _ => preset
        };

        private static IReadOnlyList<ThemePreset> BuildPresets()
        {
            static IReadOnlyList<AttractSlide> Slides(string eyebrow, params (string A, string B)[] titles)
            {
                string[] subtitles =
                {
                    "Consulta el precio y todos los detalles de este equipo",
                    "Componentes explicados de forma clara y sin tecnicismos",
                    "Escanea el QR y guarda la ficha en tu móvil"
                };
                return titles.Select((title, index) => new AttractSlide
                {
                    Eyebrow = eyebrow,
                    Title1 = title.A,
                    Title2 = title.B,
                    Subtitle = subtitles[Math.Min(index, subtitles.Length - 1)]
                }).ToList();
            }

            static IReadOnlyDictionary<string, string> Texts(string campaign) =>
                new Dictionary<string, string>
                {
                    ["attract.cta"] = "TOCA PARA DESCUBRIR ESTE EQUIPO",
                    ["price.label"] = $"// Precio en tienda · {campaign}"
                };

            static ResolvedThemeScenes Scenes(string prefix) => new()
            {
                Attract = new() { PrimaryAssetKey = $"{prefix}-hero.png", SecondaryAssetKey = $"{prefix}-horizontal.png" },
                Scan = new() { PrimaryAssetKey = $"{prefix}-compacto.png", SecondaryAssetKey = $"{prefix}-horizontal.png" },
                Overview = new() { PrimaryAssetKey = $"{prefix}-hero.png", SecondaryAssetKey = $"{prefix}-compacto.png" },
                Detail = new() { PrimaryAssetKey = $"{prefix}-horizontal.png", SecondaryAssetKey = $"{prefix}-hero.png" }
            };

            return new List<ThemePreset>
            {
                new()
                {
                    Id = ClinicPcId, DisplayName = "Clínica PC", Description = "Diseño normal del kiosco",
                    Palette = new(), AmbientEffect = ThemeAmbientEffect.Default, DefaultIntensity = 70
                },
                new()
                {
                    Id = ChristmasId, DisplayName = "Navidad", Description = "Verde pino, rojo y dorado",
                    Palette = Palette("#040B08", "#071711", "#0D241B", "#153527", "#F05B5B", "#D9B85F", "#F4E7C5"),
                    AmbientEffect = ThemeAmbientEffect.Snow, DefaultIntensity = 72, Scenes = Scenes("navidad"),
                    CampaignSlides = Slides("NAVIDAD EN CLÍNICA PC", ("REGALA", "TECNOLOGÍA"),
                        ("UN EQUIPO", "PARA CADA PERSONA"), ("ELIGE CON CALMA", "Y CON TODOS LOS DATOS")),
                    UiTexts = Texts("Navidad")
                },
                new()
                {
                    Id = BlackFridayId, DisplayName = "Black Friday", Description = "Negro, oro y energía comercial",
                    Palette = Palette("#030303", "#0B0B0D", "#151517", "#232326", "#F5C542", "#FF6846", "#FFF1AE"),
                    AmbientEffect = ThemeAmbientEffect.Spotlights, DefaultIntensity = 78, Scenes = Scenes("black-friday"),
                    CampaignSlides = Slides("BLACK FRIDAY", ("BLACK FRIDAY", "EN CLÍNICA PC"),
                        ("MIRA LO QUE", "LLEVA DENTRO"), ("COMPARA · ESCANEA", "GUARDA LA FICHA")),
                    UiTexts = Texts("Black Friday")
                },
                new()
                {
                    Id = SalesId, DisplayName = "Rebajas", Description = "Coral, ámbar y confeti",
                    Palette = Palette("#100307", "#21070D", "#350B13", "#4C111C", "#FF5B68", "#FFB84D", "#FFF0D1"),
                    AmbientEffect = ThemeAmbientEffect.Confetti, DefaultIntensity = 75, Scenes = Scenes("rebajas"),
                    CampaignSlides = Slides("REBAJAS EN CLÍNICA PC", ("REBAJAS", "EN CLÍNICA PC"),
                        ("BUEN EQUIPO", "MEJOR OPORTUNIDAD"), ("TOCA · COMPARA", "ELIGE CON DATOS")),
                    UiTexts = Texts("Rebajas")
                },
                new()
                {
                    Id = BackToSchoolId, DisplayName = "Vuelta al cole", Description = "Azul, cian y amarillo",
                    Palette = Palette("#030915", "#07152A", "#0D2340", "#15365C", "#38BDF8", "#FBBF24", "#D8F3FF"),
                    AmbientEffect = ThemeAmbientEffect.Notebook, DefaultIntensity = 68, Scenes = Scenes("vuelta-al-cole"),
                    CampaignSlides = Slides("VUELTA AL COLE", ("VUELTA", "AL COLE"),
                        ("LISTO PARA", "EL NUEVO CURSO"), ("ESTUDIA · CREA", "HAZLO A TU MANERA")),
                    UiTexts = Texts("Vuelta al cole")
                }
            };
        }

        private static ThemePalette Palette(string b0, string b1, string b2, string b3,
            string primary, string secondary, string highlight) => new()
        {
            Background0 = b0, Background1 = b1, Background2 = b2, Background3 = b3,
            Primary = primary, Secondary = secondary, Highlight = highlight,
            Text0 = "#FFFFFF", Text1 = "#E7E4EE", Text2 = "#B8B2C8", Text3 = "#827A98"
        };
    }
}
