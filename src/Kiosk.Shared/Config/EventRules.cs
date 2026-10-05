using System;
using System.Collections.Generic;
using System.Linq;

namespace KioskClinicaPC.Core.Config
{
    public static class EventRules
    {
        public static IReadOnlyList<string> Validate(
            KioskEvent candidate,
            IEnumerable<KioskEvent> existing,
            TimeZoneInfo storeTimeZone,
            bool publishing)
        {
            var errors = ValidateSchedule(candidate, storeTimeZone).ToList();
            errors.AddRange(ThemePresetCatalog.Validate(candidate.Theme));

            if (publishing && existing.Any(other =>
                    other.Enabled && other.Id != candidate.Id &&
                    candidate.Start < other.End && other.Start < candidate.End))
                errors.Add("Las fechas se solapan con otro evento publicado.");

            return errors;
        }

        public static IReadOnlyList<string> ValidateSchedule(KioskEvent candidate, TimeZoneInfo storeTimeZone)
        {
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(candidate.Name))
                errors.Add("Indica un nombre para el evento.");
            if (candidate.End <= candidate.Start)
                errors.Add("La fecha de fin debe ser posterior a la de inicio.");

            DateTime start = DateTime.SpecifyKind(candidate.Start, DateTimeKind.Unspecified);
            DateTime end = DateTime.SpecifyKind(candidate.End, DateTimeKind.Unspecified);
            if (storeTimeZone.IsInvalidTime(start) || storeTimeZone.IsInvalidTime(end))
                errors.Add("El inicio o el fin cae en una hora inexistente por el cambio horario.");
            if (storeTimeZone.IsAmbiguousTime(start) || storeTimeZone.IsAmbiguousTime(end))
                errors.Add("El inicio o el fin cae en una hora ambigua por el cambio horario.");

            return errors;
        }
    }
}
