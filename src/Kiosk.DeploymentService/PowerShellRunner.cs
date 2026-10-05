using System.Diagnostics;
using System.Text.Json;
using Kiosk.Deployment;

namespace Kiosk.DeploymentService;

public static class PowerShellRunner
{
    public static async Task<T> Run<T>(string script, object parameters, CancellationToken ct)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "scripts", script);
        DeploymentPolicy.Require(File.Exists(path) && Path.GetFileName(script) == script, "Falta un componente de la estación. Reinstala el programa.");
        var start = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "WindowsPowerShell", "v1.0", "powershell.exe"))
        { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (string arg in new[] { "-NoLogo", "-NoProfile", "-NonInteractive", "-File", path }) start.ArgumentList.Add(arg);
        foreach (string key in start.Environment.Keys.Where(k => k.StartsWith("KIOSK_", StringComparison.OrdinalIgnoreCase)).ToList()) start.Environment.Remove(key);
        using var process = Process.Start(start) ?? throw new IOException("No se pudo iniciar el componente Windows.");
        // Protected parameters travel over stdin, never process command lines or diagnostic logs.
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(parameters, new JsonSerializerOptions(AtomicState.Json) { WriteIndented = false }));
        process.StandardInput.Close();
        var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync(ct); await stderr;
        DeploymentPolicy.Require(process.ExitCode == 0, "La comprobación Windows falló. Revisa permisos, espacio, ISO o puertos y vuelve a intentarlo.");
        return JsonSerializer.Deserialize<T>(await stdout, AtomicState.Json) ?? throw new InvalidDataException("Resultado Windows incompatible.");
    }
}
