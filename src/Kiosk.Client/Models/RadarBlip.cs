using System.Windows.Media;
using KioskClinicaPC.Core;

namespace KioskClinicaPC.Models
{
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
