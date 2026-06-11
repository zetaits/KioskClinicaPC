using System.Windows.Media;

namespace KioskClinicaPC.Models
{
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
}
