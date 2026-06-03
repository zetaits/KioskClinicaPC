namespace KioskClinicaPC.Core
{
    /// <summary>Identificadores canónicos de cada componente. Evita strings mágicos repartidos por el código.</summary>
    public static class ComponentIds
    {
        public const string Cpu = "cpu";
        public const string Gpu = "gpu";
        public const string Ram = "ram";
        public const string Storage = "storage";
        public const string Screen = "screen";
        public const string Battery = "battery";
        public const string Wifi = "wifi";
        public const string Camera = "camera";
        public const string Ports = "ports";
        public const string Os = "os";

        /// <summary>Componentes nucleares: siempre se muestran aunque la detección no dé valor
        /// (caen al valor por defecto/marketing). El resto se ocultan si no están presentes.</summary>
        public static bool IsAlwaysPresent(string? id) => id?.ToLowerInvariant() switch
        {
            Cpu or Gpu or Ram or Storage or Screen or Os => true,
            _ => false
        };
    }
}
