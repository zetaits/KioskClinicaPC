using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using KioskClinicaPC.Core;

namespace KioskClinicaPC.Models
{
    public class SpecItem : ObservableObject
    {
        public string? Id { get; set; }          // key: "cpu", "gpu" …

        private string? _family;
        public string? Family { get => _family; set => SetProperty(ref _family, value); }      // display family

        private string? _label;
        public string? Label { get => _label; set => SetProperty(ref _label, value); }       // Spanish label

        public string? LabelShort { get; set; }  // first 6 chars for mini-thumbs

        private string? _value;
        public string? Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    OnPropertyChanged(nameof(DetailTitle));
                    OnPropertyChanged(nameof(DetailSubtitle));
                    OnPropertyChanged(nameof(HasDetailSubtitle));
                }
            }
        }

        private string? _detail;
        public string? Detail
        {
            get => _detail;
            set
            {
                if (SetProperty(ref _detail, value))
                {
                    OnPropertyChanged(nameof(DetailTokens));
                    OnPropertyChanged(nameof(HasDetail));
                }
            }
        }

        // Nombre técnico/chip secundario (p.ej. "Intel AX211"). Vacío si no aporta. Se muestra pequeño.
        private string? _techDetail;
        public string? TechDetail
        {
            get => _techDetail;
            set
            {
                if (SetProperty(ref _techDetail, value))
                {
                    OnPropertyChanged(nameof(HasTechDetail));
                    OnPropertyChanged(nameof(DetailTitle));
                    OnPropertyChanged(nameof(DetailSubtitle));
                    OnPropertyChanged(nameof(HasDetailSubtitle));
                }
            }
        }
        public bool HasTechDetail => !string.IsNullOrWhiteSpace(_techDetail);

        // Pantalla Detail: manda el titular amigable (Value, "32 GB"/"WiFi 6E") como título grande;
        // el nombre técnico/chip (TechDetail, "Kingston…"/"Intel AX211") pasa a subtítulo pequeño.
        // Si no hay nombre técnico, no se muestra subtítulo.
        public string DetailTitle => _value ?? string.Empty;
        public string DetailSubtitle => (HasTechDetail ? _techDetail : string.Empty) ?? string.Empty;
        public bool HasDetailSubtitle => HasTechDetail;

        // El equipo tiene este componente (detectado o forzado manualmente). Los ausentes no se muestran.
        public bool IsPresent { get; set; } = true;

        private string? _summary;
        public string? Summary { get => _summary; set => SetProperty(ref _summary, value); }

        public int Index { get; set; }
        public string? IndexText { get; set; }
        public string? IndexLabelFull { get; set; } // "01 / 10 · Procesador"
        public int BenchScore { get; set; }

        private string? _benchLabel;
        public string? BenchLabel { get => _benchLabel; set => SetProperty(ref _benchLabel, value); }

        public string BenchScoreText => $"{BenchScore}%";
        public double BenchBarWidth { get; set; }
        public bool HasBench { get; set; }

        // Detail "·"-joined string split into individual tokens for the StatStrip.
        public List<string> DetailTokens =>
            (Detail ?? string.Empty)
            .Split('·')
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .ToList();

        // Oculta el StatStrip cuando no hay detalle real (ni detectado ni override): mejor vacío que falso.
        public bool HasDetail => DetailTokens.Count > 0;

        // Etiqueta de gama amigable. Se calcula desde el hardware real (PerformanceScorer.TierLabel,
        // banda de score × matiz por componente) y se fija en tiempo de construcción del SpecItem.
        public string Tier { get; set; } = "";

        public string BenchPercentLabel => $"Percentil {BenchScore} · {BenchLabel}";

        // Marker position on the 0–640px honest scale (reuses the benchmark width math).
        public double BenchMarkerLeft => BenchBarWidth;
        public List<ProItem> Pros { get; set; } = new List<ProItem>();
        public string IconData { get; set; } = "";

        // Foto real del componente (p.ej. "Intel Core i5"). Resuelta desde %LOCALAPPDATA%\…\SpecImages.
        // Si está vacía, el spotlight cae al icono vectorial de siempre.
        private string? _imagePath;
        public string? ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetProperty(ref _imagePath, value))
                    OnPropertyChanged(nameof(HasImage));
            }
        }
        public bool HasImage => !string.IsNullOrWhiteSpace(_imagePath);

        private bool _isHighlighted;
        public bool IsHighlighted { get => _isHighlighted; set => SetProperty(ref _isHighlighted, value); }

        // True for the spec currently open in the Detail screen (drives the navigator highlight).
        private bool _isCurrentDetail;
        public bool IsCurrentDetail { get => _isCurrentDetail; set => SetProperty(ref _isCurrentDetail, value); }

        public SolidColorBrush? AccentBrush { get; set; }
        public Color AccentColor { get; set; }
    }
}
