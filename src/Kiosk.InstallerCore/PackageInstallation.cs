using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using KioskClinicaPC.Core.Sync;

namespace Kiosk.InstallerCore;

public enum SignatureResult { Valid, Unsigned, Invalid }

public sealed class InstallerExitException(int exitCode) : Exception($"El instalador termin\u00f3 con c\u00f3digo {exitCode}.")
{
    public int ExitCode { get; } = exitCode;
}

public static class PackageInstallation
{
    private static readonly TimeSpan InstallTimeout = TimeSpan.FromMinutes(60);

    public static async Task DownloadResumable(HttpClient http, string relativeUrl, string path, long expected,
        Func<int, Task>? progress, CancellationToken ct)
    {
        Exception? last = null;
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                long offset = File.Exists(path) ? new FileInfo(path).Length : 0;
                if (offset > expected) { File.Delete(path); offset = 0; }
                using var req = new HttpRequestMessage(HttpMethod.Get, relativeUrl);
                if (offset > 0) req.Headers.Range = new RangeHeaderValue(offset, null);
                using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
                if (offset > 0 && resp.StatusCode == HttpStatusCode.OK) { File.Delete(path); offset = 0; }
                resp.EnsureSuccessStatusCode();
                await using var input = await resp.Content.ReadAsStreamAsync(ct);
                await using var output = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.None, 81920, true);
                byte[] buffer = new byte[81920];
                int read;
                long total = offset;
                int lastPercent = -1;
                while ((read = await input.ReadAsync(buffer, ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), ct);
                    total += read;
                    if (total > expected) throw new InvalidDataException("La descarga supera el tama\u00f1o autorizado.");
                    int percent = expected == 0 ? 0 : (int)Math.Min(99, total * 100 / expected);
                    if (progress != null && percent >= lastPercent + 5) { lastPercent = percent; await progress(percent); }
                }
                if (total == expected) return;
                throw new IOException("Descarga incompleta.");
            }
            catch (Exception ex) when (attempt < 2 && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                last = ex;
            }
        }
        throw new IOException("No se pudo descargar el instalador tras tres intentos.", last);
    }

    public static void Verify(string file, long expectedSize, string expectedSha256, bool allowUnsigned)
    {
        if (new FileInfo(file).Length != expectedSize || !Hash(file).Equals(expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Tama\u00f1o o SHA-256 incorrecto.");
        SignatureResult signature = Authenticode.Verify(file);
        if (signature == SignatureResult.Invalid || (signature == SignatureResult.Unsigned && !allowUnsigned))
            throw new InvalidDataException(signature == SignatureResult.Invalid ? "La firma digital no es v\u00e1lida." : "El instalador no est\u00e1 firmado.");
    }

    public static async Task<int> Execute(string file, InstallerPackageKind kind, CancellationToken ct)
    {
        string exe;
        string[] args;
        switch (kind)
        {
            case InstallerPackageKind.Msi:
                exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "msiexec.exe");
                args = ["/i", file, "/qn", "/norestart"];
                break;
            case InstallerPackageKind.InnoSetup:
                exe = file;
                args = ["/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/NOCANCEL"];
                break;
            case InstallerPackageKind.Nsis:
                exe = file;
                args = ["/S"];
                break;
            default:
                throw new InvalidDataException("Tipo de instalador no permitido.");
        }
        var psi = new ProcessStartInfo(exe) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = Path.GetDirectoryName(file)! };
        foreach (string arg in args) psi.ArgumentList.Add(arg);
        using var process = Process.Start(psi) ?? throw new InvalidOperationException("No se pudo iniciar el instalador.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(InstallTimeout);
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            try { process.Kill(true); } catch { }
            throw new TimeoutException("La instalaci\u00f3n super\u00f3 60 minutos.");
        }
        return process.ExitCode;
    }

    private static string Hash(string path)
    {
        using var sha = SHA256.Create();
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(sha.ComputeHash(fs));
    }
}

public static class Authenticode
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustFileInfo
    {
        public int StructSize = Marshal.SizeOf<WinTrustFileInfo>();
        public IntPtr FilePath;
        public IntPtr FileHandle;
        public IntPtr KnownSubject;
        public WinTrustFileInfo(string path) { FilePath = Marshal.StringToCoTaskMemUni(path); }
        ~WinTrustFileInfo() { if (FilePath != IntPtr.Zero) Marshal.FreeCoTaskMem(FilePath); }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private sealed class WinTrustData
    {
        public int StructSize = Marshal.SizeOf<WinTrustData>();
        public IntPtr PolicyCallbackData;
        public IntPtr SIPClientData;
        public int UIChoice = 2;
        public int RevocationChecks;
        public int UnionChoice = 1;
        public IntPtr FileInfo;
        public int StateAction;
        public IntPtr StateData;
        public string? URLReference;
        public int ProvFlags = 0x1000;
        public int UIContext;
        public WinTrustData(WinTrustFileInfo file)
        {
            FileInfo = Marshal.AllocCoTaskMem(Marshal.SizeOf<WinTrustFileInfo>());
            Marshal.StructureToPtr(file, FileInfo, false);
        }
        ~WinTrustData() { if (FileInfo != IntPtr.Zero) Marshal.FreeCoTaskMem(FileInfo); }
    }

    [DllImport("wintrust.dll", ExactSpelling = true, PreserveSig = true, SetLastError = false)]
    private static extern uint WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, WinTrustData data);

    public static SignatureResult Verify(string file)
    {
        var info = new WinTrustFileInfo(file);
        var data = new WinTrustData(info);
        uint result = WinVerifyTrust(new IntPtr(-1), new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE"), data);
        return result switch
        {
            0 => SignatureResult.Valid,
            0x800B0100 or 0x800B0003 or 0x800B0001 => SignatureResult.Unsigned,
            _ => SignatureResult.Invalid
        };
    }
}
