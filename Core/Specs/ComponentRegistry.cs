using System;
using System.Collections.Generic;
using System.Linq;

namespace KioskClinicaPC.Core.Specs
{
    /// <summary>
    /// Cómo leer/escribir el valor y el detalle técnico de un componente en un <see cref="AppConfig"/>.
    /// Una fila por componente. Antes esta correspondencia id→campo vivía triplicada en tres switch
    /// paralelos del ViewModel (valor, detalle, asignación), de modo que añadir un componente exigía
    /// tocar los tres y no olvidarse de ninguno. Ahora es una sola fuente de verdad.
    /// </summary>
    public sealed class ComponentAccessor
    {
        public string Id { get; }
        public Func<AppConfig, string?> GetValue { get; }
        public Action<AppConfig, string?> SetValue { get; }
        public Func<AppConfig, string?> GetDetail { get; }

        public ComponentAccessor(string id, Func<AppConfig, string?> getValue,
            Action<AppConfig, string?> setValue, Func<AppConfig, string?> getDetail)
        {
            Id = id;
            GetValue = getValue;
            SetValue = setValue;
            GetDetail = getDetail;
        }
    }

    /// <summary>Registro único de los accessors de componente. Para añadir uno: una fila aquí
    /// (más su entrada en los catálogos de marketing/iconos donde corresponda).</summary>
    public static class ComponentRegistry
    {
        // Nota: el CPU usa Cores como "detalle"; el resto su campo *Detail dedicado.
        private static readonly ComponentAccessor[] All =
        {
            new(ComponentIds.Cpu,     c => c.Cpu,     (c, v) => c.Cpu = v,     c => c.Cores),
            new(ComponentIds.Gpu,     c => c.Gpu,     (c, v) => c.Gpu = v,     c => c.GpuDetail),
            new(ComponentIds.Ram,     c => c.Ram,     (c, v) => c.Ram = v,     c => c.RamDetail),
            new(ComponentIds.Storage, c => c.Storage, (c, v) => c.Storage = v, c => c.StorageDetail),
            new(ComponentIds.Screen,  c => c.Screen,  (c, v) => c.Screen = v,  c => c.ScreenDetail),
            new(ComponentIds.Battery, c => c.Battery, (c, v) => c.Battery = v, c => c.BatteryDetail),
            new(ComponentIds.Wifi,    c => c.Wifi,    (c, v) => c.Wifi = v,    c => c.WifiDetail),
            new(ComponentIds.Camera,  c => c.Camera,  (c, v) => c.Camera = v,  c => c.CameraDetail),
            new(ComponentIds.Ports,   c => c.Ports,   (c, v) => c.Ports = v,   c => c.PortsDetail),
            new(ComponentIds.Os,      c => c.Os,      (c, v) => c.Os = v,      c => c.OsDetail),
        };

        private static readonly Dictionary<string, ComponentAccessor> ById =
            All.ToDictionary(a => a.Id, StringComparer.OrdinalIgnoreCase);

        public static bool TryGet(string? id, out ComponentAccessor accessor)
            => ById.TryGetValue(id ?? string.Empty, out accessor!);
    }
}
