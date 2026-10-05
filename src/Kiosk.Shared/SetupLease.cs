namespace KioskClinicaPC.Equipment;
/// <summary>Equipment coordinator and legacy pack workers share the native installation lease.</summary>
public sealed class SetupLease : IDisposable
{
    private readonly List<FileStream> _files = [];
    private SetupLease() { }
    public static SetupLease Equipment(string stateRoot, bool pack)
    {
        var lease = new SetupLease();
        try
        {
            lease._files.Add(Open(stateRoot, "equipment.lock"));
            if (!pack) lease._files.Add(Open(stateRoot, "run.lock"));
            return lease;
        }
        catch { lease.Dispose(); throw; }
    }
    public static FileStream Pack(string stateRoot) => Open(stateRoot, "run.lock");
    private static FileStream Open(string root, string name) => new(Path.Combine(root, name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
    public void Dispose() { foreach (var file in _files) file.Dispose(); }
}
