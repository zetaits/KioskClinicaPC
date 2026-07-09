using KioskClinicaPC.Core;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>
/// Autenticación del panel de administración: un único hash de contraseña en <c>panel.json</c> (junto a
/// la config). Sin multi-usuario ni roles — un solo encargado accede desde el PC. Reutiliza el mismo
/// <see cref="PasswordService"/> (PBKDF2) que el kiosko, y se siembra con la misma contraseña por defecto
/// (<c>clinicapc2025</c>) para que el arranque no quede sin acceso. Cambiable desde el propio panel.
/// </summary>
public sealed class PanelAuthStore
{
    private const string DefaultPassword = "clinicapc2025";

    private readonly string _path;
    private readonly object _gate = new();

    public PanelAuthStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "panel.json");
        if (!File.Exists(_path)) WriteHash(PasswordService.Hash(DefaultPassword));
    }

    public bool Verify(string password) => PasswordService.Verify(password, ReadHash());

    public void SetPassword(string newPassword) => WriteHash(PasswordService.Hash(newPassword));

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
