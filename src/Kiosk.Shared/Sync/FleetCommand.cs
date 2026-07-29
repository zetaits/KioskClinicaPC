namespace KioskClinicaPC.Core.Sync
{
    /// <summary>Tipo de orden que el panel envía a un kiosko por el hub de flota.</summary>
    public enum FleetCommandKind
    {
        /// <summary>Reiniciar el equipo (shutdown /r).</summary>
        Reboot,

        /// <summary>Apagar el equipo (shutdown /s).</summary>
        Shutdown,

        /// <summary>Reiniciar solo la aplicación kiosko (relanzar el proceso).</summary>
        RestartApp,

        /// <summary>Fijar el precio/oferta expuesto en este equipo (<see cref="Price"/>/<see cref="OldPrice"/>).</summary>
        SetPrice,

        /// <summary>Renombrar el equipo (<see cref="Name"/>); se persiste en el propio kiosko.</summary>
        SetName
    }

    /// <summary>
    /// Orden dirigida del panel a un kiosko (o a varios, en las emergencias "todos"). El cliente la recibe
    /// por el evento "Command" del hub de flota y actúa según <see cref="Kind"/>. Los campos opcionales solo
    /// aplican a su orden correspondiente.
    /// </summary>
    public sealed class FleetCommand
    {
        public FleetCommandKind Kind { get; set; }

        /// <summary>Precio a fijar (solo <see cref="FleetCommandKind.SetPrice"/>).</summary>
        public decimal? Price { get; set; }

        /// <summary>Precio anterior/tachado a fijar; 0 o null = sin oferta (solo SetPrice).</summary>
        public decimal? OldPrice { get; set; }

        /// <summary>Nuevo nombre (solo <see cref="FleetCommandKind.SetName"/>).</summary>
        public string? Name { get; set; }

        public static FleetCommand Reboot() => new() { Kind = FleetCommandKind.Reboot };
        public static FleetCommand Shutdown() => new() { Kind = FleetCommandKind.Shutdown };
        public static FleetCommand RestartApp() => new() { Kind = FleetCommandKind.RestartApp };
        public static FleetCommand SetPrice(decimal price, decimal oldPrice) =>
            new() { Kind = FleetCommandKind.SetPrice, Price = price, OldPrice = oldPrice };
        public static FleetCommand SetName(string name) =>
            new() { Kind = FleetCommandKind.SetName, Name = name };
    }
}
