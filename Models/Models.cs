using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;

namespace KioskClinicaPC.Models
{
    public class SpecItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetProperty<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(storage, value)) return false;
            storage = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            return true;
        }

        public string Id { get; set; }          // key: "cpu", "gpu" …

        private string _family;
        public string Family { get => _family; set => SetProperty(ref _family, value); }      // display family

        private string _label;
        public string Label { get => _label; set => SetProperty(ref _label, value); }       // Spanish label

        public string LabelShort { get; set; }  // first 6 chars for mini-thumbs

        private string _value;
        public string Value
        {
            get => _value;
            set
            {
                if (SetProperty(ref _value, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailTitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailSubtitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailSubtitle)));
                }
            }
        }

        private string _detail;
        public string Detail { get => _detail; set => SetProperty(ref _detail, value); }

        // Nombre técnico/chip secundario (p.ej. "Intel AX211"). Vacío si no aporta. Se muestra pequeño.
        private string _techDetail;
        public string TechDetail
        {
            get => _techDetail;
            set
            {
                if (SetProperty(ref _techDetail, value))
                {
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasTechDetail)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailTitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DetailSubtitle)));
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDetailSubtitle)));
                }
            }
        }
        public bool HasTechDetail => !string.IsNullOrWhiteSpace(_techDetail);

        // Pantalla Detail: el modelo concreto manda como título; el nombre amigable pasa a subtítulo.
        // Si no hay modelo concreto, el título cae al nombre amigable y no se muestra subtítulo.
        public string DetailTitle => HasTechDetail ? _techDetail : _value;
        public string DetailSubtitle => HasTechDetail ? _value : string.Empty;
        public bool HasDetailSubtitle => HasTechDetail && !string.IsNullOrWhiteSpace(_value);

        // El equipo tiene este componente (detectado o forzado manualmente). Los ausentes no se muestran.
        public bool IsPresent { get; set; } = true;

        private string _summary;
        public string Summary { get => _summary; set => SetProperty(ref _summary, value); }

        public int Index { get; set; }
        public string IndexText { get; set; }
        public string IndexLabelFull { get; set; } // "01 / 10 · Procesador"
        public double Angle { get; set; }
        public double NodeX { get; set; }
        public double NodeY { get; set; }
        public double ConnectorLeft { get; set; } // legacy
        public bool ConnectorOnRight { get; set; }
        public double ConnectorX { get; set; }
        public double ConnectorY { get; set; }
        public TimeSpan NodeAnimDelay { get; set; }
        public int BenchScore { get; set; }

        private string _benchLabel;
        public string BenchLabel { get => _benchLabel; set => SetProperty(ref _benchLabel, value); }

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

        // Qualitative tier derived from the benchmark percentile (computed, not invented data).
        public string Tier =>
            BenchScore >= 88 ? "Gama alta"
            : BenchScore >= 75 ? "Gama media-alta"
            : BenchScore >= 60 ? "Equilibrado"
            : "Uso ligero";

        public string BenchPercentLabel => $"Percentil {BenchScore} · {BenchLabel}";

        // Marker position on the 0–640px honest scale (reuses the benchmark width math).
        public double BenchMarkerLeft => BenchBarWidth;
        public List<ProItem> Pros { get; set; }
        public string IconData { get; set; }

        // Foto real del componente (p.ej. "Intel Core i5"). Resuelta desde %LOCALAPPDATA%\…\SpecImages.
        // Si está vacía, el spotlight cae al icono vectorial de siempre.
        private string _imagePath;
        public string ImagePath
        {
            get => _imagePath;
            set
            {
                if (SetProperty(ref _imagePath, value))
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasImage)));
            }
        }
        public bool HasImage => !string.IsNullOrWhiteSpace(_imagePath);

        private bool _isHighlighted;
        public bool IsHighlighted
        {
            get => _isHighlighted;
            set
            {
                _isHighlighted = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsHighlighted)));
            }
        }

        // True for the spec currently open in the Detail screen (drives the navigator highlight).
        private bool _isCurrentDetail;
        public bool IsCurrentDetail
        {
            get => _isCurrentDetail;
            set
            {
                _isCurrentDetail = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsCurrentDetail)));
            }
        }

        public SolidColorBrush AccentBrush { get; set; }
        public Color AccentColor { get; set; }
    }

    public class ProItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Index { get; set; }

        private string _text;
        public string Text
        {
            get => _text;
            set
            {
                if (_text == value) return;
                _text = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text)));
            }
        }
    }

    public class ScanLogItem
    {
        public string Time { get; set; }
        public string Step { get; set; }
        public string Color { get; set; }
    }
}