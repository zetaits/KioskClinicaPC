using System;
using System.Collections.Generic;

namespace KioskClinicaPC.Core.Config
{
    /// <summary>
    /// Evento con vigencia temporal (p.ej. "Black Friday") que reemplaza parte del contenido COMPARTIDO
    /// mientras está activo. Solo toca lo global (slides de atracción y textos de UI); el precio/estado/
    /// especificaciones son por-máquina y ningún evento los altera. Las fechas son hora LOCAL de la tienda
    /// (no del VPS), así el rango cuadra con lo que el encargado escribe en el panel.
    /// </summary>
    public sealed class KioskEvent
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string? Name { get; set; }

        /// <summary>Interruptor manual: un evento programado se puede apagar sin borrarlo.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Inicio (inclusive) en hora local de la tienda.</summary>
        public DateTime Start { get; set; }

        /// <summary>Fin (exclusive) en hora local de la tienda.</summary>
        public DateTime End { get; set; }

        /// <summary>Si no está vacío, reemplaza los slides "De ocasión" mientras el evento está activo.</summary>
        public List<AttractSlide> AttractSlides { get; set; } = new();

        /// <summary>Si no está vacío, reemplaza los slides "Nuevo" mientras el evento está activo.</summary>
        public List<AttractSlide> AttractSlidesNew { get; set; } = new();

        /// <summary>Textos de UI a sobreponer (merge por clave) sobre los base mientras el evento activo.</summary>
        public Dictionary<string, string> UiTextOverrides { get; set; } = new();

        /// <summary>¿Está vigente en el instante <paramref name="nowLocal"/> (hora local de tienda)?</summary>
        public bool IsActiveAt(DateTime nowLocal) => Enabled && Start <= nowLocal && nowLocal < End;
    }
}
