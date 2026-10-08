using KioskClinicaPC.Core.Sync;
namespace KioskClinicaPC.Equipment;
public sealed record PackWorkerStart(PackCatalog Snapshot, bool Resume, bool AllowPartial = false);
