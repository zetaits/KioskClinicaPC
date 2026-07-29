namespace KioskClinicaPC.Core.Sync
{
    /// <summary>
    /// Telemetría que cada kiosko envía al servidor por el hub de flota: al registrarse (al conectar) y
    /// después de forma periódica. Es lo que el panel pinta en la vista de Flota. Solo lleva estado que el
    /// encargado necesita ver; nada sensible. Serializa PascalCase (Newtonsoft) igual que el resto del
    /// contrato compartido.
    /// </summary>
    public sealed class KioskHeartbeat
    {
        /// <summary>Id estable del equipo (GUID persistido en KioskSettings). Clave del registro de flota.</summary>
        public string DeviceId { get; set; } = "";

        /// <summary>Nombre visible del equipo (editable en el propio kiosko o desde el panel).</summary>
        public string Name { get; set; } = "";

        /// <summary>Pantalla que muestra ahora mismo.</summary>
        public KioskScreen Screen { get; set; }

        /// <summary>Equipo expuesto (chasis/modelo detectado).</summary>
        public string Equipment { get; set; } = "";

        /// <summary>CPU (+ GPU si aplica) del equipo expuesto.</summary>
        public string Cpu { get; set; } = "";

        /// <summary>Precio expuesto actual.</summary>
        public decimal Price { get; set; }

        /// <summary>Precio anterior (tachado) si hay oferta; 0 si no.</summary>
        public decimal OldPrice { get; set; }

        /// <summary>Estado del equipo expuesto ("Nuevo"/"Ocasion").</summary>
        public string Condition { get; set; } = "";

        /// <summary>Versión de la app cliente.</summary>
        public string AppVersion { get; set; } = "";

        /// <summary>Instante de arranque del proceso (ms Unix UTC) para calcular el uptime en el panel.</summary>
        public long StartedAtUnixMs { get; set; }
    }
}
