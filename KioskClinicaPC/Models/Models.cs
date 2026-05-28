using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows.Media;

namespace KioskClinicaPC.Models
{
    public class SpecItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public string Id { get; set; }          // key: "cpu", "gpu" …
        public string Family { get; set; }      // display family: "CPU", "DISPLAY", "POWER" …
        public string Label { get; set; }       // Spanish label: "Procesador", "Gráfica" …
        public string LabelShort { get; set; }  // first 6 chars for mini-thumbs
        public string Value { get; set; }
        public string Detail { get; set; }
        public string Summary { get; set; }
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
        public string BenchLabel { get; set; }
        public string BenchScoreText => $"{BenchScore}%";
        public double BenchBarWidth { get; set; }
        public bool HasBench { get; set; }
        public List<ProItem> Pros { get; set; }
        public string IconData { get; set; }

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

        public SolidColorBrush AccentBrush { get; set; }
        public Color AccentColor { get; set; }
    }

    public class ProItem
    {
        public string Index { get; set; }
        public string Text { get; set; }
    }

    public class ScanLogItem
    {
        public string Time { get; set; }
        public string Step { get; set; }
        public string Color { get; set; }
    }
}