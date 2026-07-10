namespace KioskClinicaPC.Services
{
    /// <summary>
    /// Cliente de sincronización del bucle de atracción. Cuando hay servidor y conexión, entrega el
    /// índice de slide que TODOS los kioscos deben mostrar en este instante (calculado del reloj maestro).
    /// Sin servidor o sin conexión, <see cref="IsSynced"/> es false y el kiosko rota los slides él solo
    /// (comportamiento previo al rework). Nunca lanza: la sincronización es un extra, no un requisito.
    /// </summary>
    public interface ISyncClient
    {
        /// <summary>Se dispara cuando el contenido compartido del servidor cambió (edición en el panel o
        /// evento que entra/sale). El suscriptor debe recargar la config. Puede llegar en un hilo de fondo.</summary>
        event System.Action? ContentChanged;

        /// <summary>true si hay conexión viva con el reloj maestro y un estado válido recibido.</summary>
        bool IsSynced { get; }

        /// <summary>Arranca la conexión (no-op si no hay servidor configurado). No bloquea.</summary>
        void Start();

        /// <summary>Índice de slide sincronizado para <paramref name="slideCount"/> slides ahora mismo.
        /// Devuelve false si no está sincronizado (el llamador rota entonces por su cuenta).</summary>
        bool TryGetSlideIndex(int slideCount, out int index);
    }
}
