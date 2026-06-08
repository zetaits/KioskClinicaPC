using System;
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

        // Pantalla Detail: el modelo concreto manda como título; el nombre amigable pasa a subtítulo.
        // Si no hay modelo concreto, el título cae al nombre amigable y no se muestra subtítulo.
        public string DetailTitle => (HasTechDetail ? _techDetail : _value) ?? string.Empty;
        public string DetailSubtitle => (HasTechDetail ? _value : string.Empty) ?? string.Empty;
        public bool HasDetailSubtitle => HasTechDetail && !string.IsNullOrWhiteSpace(_value);

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

    public class ProItem : ObservableObject
    {
        public string? Index { get; set; }

        private string? _text;
        public string? Text { get => _text; set => SetProperty(ref _text, value); }
    }

    public class ScanLogItem
    {
        public string? Time { get; set; }
        public string? Step { get; set; }
        public string? Color { get; set; }

        // Icono vectorial del componente al que pertenece la línea (vacío en INIT/PROBE/VERIFY/…).
        public string? IconData { get; set; }
        // Color de acento del componente para teñir el icono; null en líneas sin componente.
        public Brush? AccentBrush { get; set; }
        public bool HasIcon => !string.IsNullOrWhiteSpace(IconData);
    }

    // Punto del radar de Scan (variación lock-on): ligado a un componente real, posicionado en
    // coordenadas px del radar 760×760. IsDetected lo activa la secuencia de escaneo y dispara el
    // ping/lock-on de ese blip. Concern separado de SpecItem.IsHighlighted (que lo usa Main).
    public class RadarBlip : ObservableObject
    {
        public string? Id { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public bool Flip { get; set; }          // tag a la izquierda si el blip cae a la derecha del centro
        public string? IconData { get; set; }
        public Brush? AccentBrush { get; set; }
        public Color AccentColor { get; set; }
        public string? Label { get; set; }
        // El tag del lock-on va en MAYÚSCULAS como el mockup (`label.toUpperCase() · OK`).
        public string? LabelUpper => Label?.ToUpperInvariant();
        public double PingDelaySeconds { get; set; }  // desfase del anillo de ping (--pd del mockup)

        // Posiciones ya descentradas para colocar directamente con Canvas.Left/Top.
        public double BlipLeft => X - 5;        // punto de 10px
        public double BlipTop => Y - 5;
        public double LockonLeft => X;          // la retícula se centra con su propio RenderTransform
        public double LockonTop => Y;

        private bool _isDetected;
        public bool IsDetected { get => _isDetected; set => SetProperty(ref _isDetected, value); }
    }
}
