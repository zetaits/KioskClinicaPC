using System;

namespace KioskClinicaPC.Core
{
    /// <summary>Resuelve el destino público de la ficha PDF sin acoplar los QR a GitHub Pages.</summary>
    public static class FichaPdfUrl
    {
        public const string PublicFallback = "https://panel.clinicapc.es/ficha/";

        public static string Resolve(string? serverUrl)
        {
            string candidate = serverUrl?.Trim().TrimEnd('/') ?? "";
            if (Uri.TryCreate(candidate, UriKind.Absolute, out Uri? uri) &&
                uri.Scheme == Uri.UriSchemeHttps && !string.IsNullOrWhiteSpace(uri.Host))
                return candidate + "/ficha/";
            return PublicFallback;
        }
    }
}
