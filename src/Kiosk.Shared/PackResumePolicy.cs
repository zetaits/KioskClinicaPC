using KioskClinicaPC.Core.Sync;
namespace KioskClinicaPC.Equipment;

public static class PackResumePolicy
{
    public static PackRun Create(PackCatalog snapshot, PackRun? previous, bool resume)
    {
        static bool Uncertain(PackItemResult item) => item.RequiresRebootBeforeRetry || item.State is PackItemState.Installing or PackItemState.VerificationPending;
        DateTime boot = DateTime.UtcNow - TimeSpan.FromMilliseconds(Environment.TickCount64);
        bool SameBoot(PackItemResult item) => Math.Abs((boot - (item.LastAttemptBootTimeUtc ?? previous!.HostBootTimeUtc)).TotalMinutes) < 1;
        if (previous?.Items.Any(item => Uncertain(item) && SameBoot(item) && !snapshot.Applications.Any(a => a.WingetId == item.Application.WingetId)) == true)
            throw new NativeStateException("Hay un instalador anterior sin verificar fuera de esta selección. Reinicia manualmente antes de iniciar otra cola.");
        return new PackRun { CatalogRevision = snapshot.Revision, Items = snapshot.Applications.Select(app =>
        {
            var old = previous?.Items.Find(x => x.Application.WingetId == app.WingetId);
            bool sameVersion = old?.Application.PinnedVersion == app.PinnedVersion;
            return new PackItemResult { Application = app with { }, State = resume && sameVersion ? old!.State : PackItemState.Pending,
                RequiresRebootBeforeRetry = old != null && Uncertain(old),
                RebootRequired = old != null && SameBoot(old) && old.RebootRequired,
                LastAttemptBootTimeUtc = old?.LastAttemptBootTimeUtc ?? previous?.HostBootTimeUtc };
        }).ToList() };
    }
}
