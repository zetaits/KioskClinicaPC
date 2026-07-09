using System.Collections.Generic;

namespace KioskClinicaPC.Core.Config
{
    /// <summary>Aplicación de un evento activo sobre el contenido compartido base.</summary>
    public static class EventContent
    {
        /// <summary>Sobrepone los overrides del evento sobre <paramref name="baseConfig"/> (in-place):
        /// reemplaza los sets de slides que el evento traiga y funde sus textos de UI. Los sets vacíos del
        /// evento no borran nada (se conserva el contenido base).</summary>
        public static void Apply(AppConfig baseConfig, KioskEvent ev)
        {
            if (ev.AttractSlides.Count > 0) baseConfig.AttractSlides = ev.AttractSlides;
            if (ev.AttractSlidesNew.Count > 0) baseConfig.AttractSlidesNew = ev.AttractSlidesNew;

            if (ev.UiTextOverrides.Count > 0)
            {
                // Copia para no mutar el diccionario base compartido (Read() puede devolver referencias).
                var texts = new Dictionary<string, string>(baseConfig.UiTexts);
                foreach (var kv in ev.UiTextOverrides) texts[kv.Key] = kv.Value;
                baseConfig.UiTexts = texts;
            }
        }

        /// <summary>Evento vigente en <paramref name="nowLocal"/>. Si varios se solapan, gana el de inicio
        /// más reciente (el más específico/último programado).</summary>
        public static KioskEvent? ActiveAt(IEnumerable<KioskEvent> events, System.DateTime nowLocal)
        {
            KioskEvent? best = null;
            foreach (var ev in events)
            {
                if (!ev.IsActiveAt(nowLocal)) continue;
                if (best == null || ev.Start > best.Start) best = ev;
            }
            return best;
        }
    }
}
