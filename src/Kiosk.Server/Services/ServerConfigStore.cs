using System.Security.Cryptography;
using System.Text;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json;

namespace Kiosk.Server.Services;

/// <summary>
/// Almacén de la configuración de contenido del servidor (KioskConfig.json en el directorio de datos).
/// Fuente única que sirven todos los clientes. En Fase 1 es de solo-lectura para los clientes; el panel
/// de administración (Fase 3) será quien la escriba. Serializa con Newtonsoft (PascalCase) para que el
/// JSON sea idéntico al que el cliente ya sabe leer.
/// </summary>
public sealed class ServerConfigStore
{
    private readonly string _configPath;
    private readonly object _gate = new();

    public ServerConfigStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _configPath = Path.Combine(dataDir, "KioskConfig.json");
        EnsureSeeded();
    }

    /// <summary>Devuelve el JSON crudo tal cual está en disco (lo que se envía al cliente).</summary>
    public string ReadJson()
    {
        lock (_gate) return File.ReadAllText(_configPath);
    }

    /// <summary>Deserializa la config a objeto para editarla en el panel. Si el archivo estuviera
    /// corrupto/vacío, devuelve una config por defecto en lugar de lanzar (el panel nunca muere).</summary>
    public AppConfig Read()
    {
        lock (_gate)
        {
            var config = JsonConvert.DeserializeObject<AppConfig>(File.ReadAllText(_configPath));
            return config ?? new AppConfig { SchemaVersion = AppConfig.CurrentSchemaVersion };
        }
    }

    /// <summary>Versión corta del contenido (hash) para que el cliente detecte cambios sin re-parsear.</summary>
    public string Version()
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(ReadJson()));
        return Convert.ToHexString(hash, 0, 8); // 16 hex chars, suficiente
    }

    public void Write(AppConfig config)
    {
        lock (_gate)
            WriteAtomic(_configPath, JsonConvert.SerializeObject(config, Formatting.Indented));
    }

    private void EnsureSeeded()
    {
        if (!File.Exists(_configPath))
            Write(new AppConfig { SchemaVersion = AppConfig.CurrentSchemaVersion });
    }

    /// <summary>Escritura atómica (tmp + move) para no dejar el archivo a medias si el proceso cae.</summary>
    private static void WriteAtomic(string path, string content)
    {
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { /* limpieza best-effort */ }
        }
    }
}
