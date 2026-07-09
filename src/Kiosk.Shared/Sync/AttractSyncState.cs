namespace KioskClinicaPC.Core.Sync
{
    /// <summary>
    /// Estado del reloj maestro del bucle de atracción que el servidor emite por SignalR. No lleva el
    /// índice de slide ya calculado a propósito: el servidor solo comparte el <see cref="EpochUnixMs"/>
    /// (origen del cronómetro) y la <see cref="SlideDurationMs"/> (cuánto dura cada slide); cada cliente
    /// deduce el índice con la misma fórmula. Así un mensaje perdido no congela la rotación y un cliente
    /// que reconecta cae en el slide correcto sin esperar al siguiente latido.
    ///
    /// <see cref="ServerTimeUnixMs"/> viaja para corregir el desfase de reloj entre máquinas: el cliente
    /// mide la diferencia con su propio reloj al recibirlo y la aplica, de modo que todos calculan el
    /// mismo índice aunque sus relojes no estén perfectamente sincronizados por NTP.
    /// </summary>
    public sealed class AttractSyncState
    {
        /// <summary>Origen del cronómetro (ms Unix UTC). Estable mientras el servidor no reinicie.</summary>
        public long EpochUnixMs { get; set; }

        /// <summary>Duración de cada slide en ms. Los clientes rotan a este ritmo, coordinados.</summary>
        public int SlideDurationMs { get; set; }

        /// <summary>Hora del servidor (ms Unix UTC) al emitir. Permite corregir el desfase de reloj.</summary>
        public long ServerTimeUnixMs { get; set; }

        /// <summary>Índice de slide para <paramref name="slideCount"/> slides en el instante
        /// <paramref name="nowUnixMs"/> (ya corregido de desfase). Determinista y compartido por todos
        /// los clientes. Devuelve 0 si no hay slides o la duración es inválida.</summary>
        public int SlideIndexAt(long nowUnixMs, int slideCount)
        {
            if (slideCount <= 0 || SlideDurationMs <= 0) return 0;
            long elapsed = nowUnixMs - EpochUnixMs;
            if (elapsed < 0) elapsed = 0;
            return (int)((elapsed / SlideDurationMs) % slideCount);
        }
    }
}
