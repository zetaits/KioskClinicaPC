using KioskClinicaPC.Core;
using Newtonsoft.Json;
using KioskClinicaPC.Equipment;

namespace Kiosk.Server.Services;

/// <summary>
/// Autenticación del panel de administración: un único hash de contraseña en <c>panel.json</c> (junto a
/// la config). Sin multi-usuario ni roles — un solo encargado accede desde el PC. Reutiliza el mismo
/// <see cref="PasswordService"/> (PBKDF2) que el kiosko. En el primer arranque exige una contraseña
/// proporcionada desde configuración; nunca crea una credencial conocida dentro del código.
/// </summary>
public sealed class PanelAuthStore
{
    public const int MinimumPasswordLength = 12;

    private readonly string _path;
    private readonly object _gate = new();

    public PanelAuthStore(string dataDir, string? initialPassword = null)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "panel.json");
        if (!File.Exists(_path))
        {
            ValidatePassword(initialPassword, "Kiosk:PanelInitialPassword");
            WriteHash(PasswordService.Hash(initialPassword!));
        }
    }

    public bool Verify(string password) => PasswordService.Verify(password, ReadHash());

    public PanelPasswordProvisioning? ExportKioskPassword()
    {
        string? hash = ReadHash();
        return PasswordService.IsValidHash(hash)
            ? new(1, hash!, PanelPasswordProvisioning.CurrentPasswordPolicyVersion) : null;
    }

    public void SetPassword(string newPassword)
    {
        ValidatePassword(newPassword, "La nueva contraseña");
        WriteHash(PasswordService.Hash(newPassword));
    }

    private static void ValidatePassword(string? password, string name)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumPasswordLength)
            throw new InvalidOperationException($"{name} debe tener al menos {MinimumPasswordLength} caracteres.");
    }

    private string? ReadHash()
    {
        lock (_gate)
        {
            try { return JsonConvert.DeserializeObject<Record>(File.ReadAllText(_path))?.PasswordHash; }
            catch { return null; }
        }
    }

    private void WriteHash(string hash)
    {
        lock (_gate)
        {
            string tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonConvert.SerializeObject(new Record { PasswordHash = hash }, Formatting.Indented));
                File.Move(tmp, _path, overwrite: true);
            }
            finally { try { if (File.Exists(tmp)) File.Delete(tmp); } catch { } }
        }
    }

    private sealed class Record { public string? PasswordHash { get; set; } }
}
