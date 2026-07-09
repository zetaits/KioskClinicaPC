using System;
using System.Collections.Generic;
using System.Windows.Threading;

namespace KioskClinicaPC.Core.Platform
{
    public enum KioskTimer
    {
        /// <summary>Inactividad: vuelve a la pantalla de espera. One-shot (se rearma con Restart).</summary>
        Inactivity,
        /// <summary>Avance automático de slides en la pantalla de atracción. Repetitivo.</summary>
        AttractAdvance,
        /// <summary>Auto-escaneo desde atracción. One-shot.</summary>
        AutoScan,
        /// <summary>Rotación del componente resaltado en Main. Repetitivo.</summary>
        Highlight,
        /// <summary>Recorrido automático por la pantalla Detail de cada spec. Repetitivo.</summary>
        DetailAdvance,
        /// <summary>Episodios de giro del orbe en atracción. Repetitivo.</summary>
        OrbEpisode,
        /// <summary>Ventana para contar los 3 clics del hotspot de ajustes. One-shot.</summary>
        HotspotReset
    }

    /// <summary>
    /// Centraliza los <see cref="DispatcherTimer"/> del kiosco. Antes vivían como 6 campos sueltos en
    /// MainWindow con su cableado de Tick, paro/arranque disperso y los intervalos calculados a mano.
    /// Aquí cada timer es un <see cref="KioskTimer"/>; el code-behind solo asigna QUÉ hace cada tick
    /// (callbacks) y pide Start/Stop/Restart. Los one-shot se paran solos antes de invocar su callback.
    /// </summary>
    public sealed class KioskTimers
    {
        private readonly Dictionary<KioskTimer, DispatcherTimer> _timers = new();

        // Se paran solos al disparar (antes el handler hacía Stop() como primera línea).
        private static readonly HashSet<KioskTimer> OneShot = new()
        {
            KioskTimer.Inactivity, KioskTimer.AutoScan, KioskTimer.HotspotReset
        };

        // Callbacks: qué ocurre en cada tick. Los asigna el code-behind.
        public Action? Inactivity { get; set; }
        public Action? AttractAdvance { get; set; }
        public Action? AutoScan { get; set; }
        public Action? Highlight { get; set; }
        public Action? DetailAdvance { get; set; }
        public Action? OrbEpisode { get; set; }
        public Action? HotspotReset { get; set; }

        public KioskTimers()
        {
            // Intervalos por defecto; ApplyIntervals sobreescribe los configurables tras cargar ajustes.
            Add(KioskTimer.Inactivity, TimeSpan.FromSeconds(90), () => Inactivity?.Invoke());
            Add(KioskTimer.AttractAdvance, TimeSpan.FromSeconds(5.2), () => AttractAdvance?.Invoke());
            Add(KioskTimer.AutoScan, TimeSpan.FromSeconds(18), () => AutoScan?.Invoke());
            Add(KioskTimer.Highlight, TimeSpan.FromSeconds(4.5), () => Highlight?.Invoke());
            Add(KioskTimer.DetailAdvance, TimeSpan.FromSeconds(6.5), () => DetailAdvance?.Invoke());
            Add(KioskTimer.OrbEpisode, TimeSpan.FromSeconds(11), () => OrbEpisode?.Invoke());
            Add(KioskTimer.HotspotReset, TimeSpan.FromSeconds(1.5), () => HotspotReset?.Invoke());
        }

        private void Add(KioskTimer kind, TimeSpan interval, Action onTick)
        {
            var timer = new DispatcherTimer { Interval = interval };
            timer.Tick += (_, _) =>
            {
                if (OneShot.Contains(kind)) timer.Stop();
                onTick();
            };
            _timers[kind] = timer;
        }

        /// <summary>Cambia el intervalo de un timer en caliente (p.ej. el attract alterna entre el ritmo
        /// local configurado y el sondeo rápido cuando sigue el reloj maestro del servidor).</summary>
        public void SetInterval(KioskTimer kind, TimeSpan interval) => _timers[kind].Interval = interval;

        public void Start(KioskTimer kind) => _timers[kind].Start();

        public void Stop(KioskTimer kind) => _timers[kind].Stop();

        public void Restart(KioskTimer kind)
        {
            var timer = _timers[kind];
            timer.Stop();
            timer.Start();
        }

        public void StopAll()
        {
            foreach (var timer in _timers.Values) timer.Stop();
        }

        /// <summary>Reaplica los intervalos configurables desde ajustes (con sus mínimos de seguridad).</summary>
        public void ApplyIntervals(KioskSettings settings)
        {
            _timers[KioskTimer.Inactivity].Interval = TimeSpan.FromSeconds(Math.Max(5, settings.InactivitySeconds));
            _timers[KioskTimer.AttractAdvance].Interval = TimeSpan.FromSeconds(Math.Max(1, settings.SlideIntervalSeconds));
            _timers[KioskTimer.AutoScan].Interval = TimeSpan.FromSeconds(Math.Max(3, settings.AutoScanSeconds));
        }
    }
}
