using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using KioskClinicaPC.Core;
using KioskClinicaPC.Equipment;
using Newtonsoft.Json;

namespace KioskClinicaPC.Services;

public sealed class PanelPasswordAuthenticator(HttpClient http)
{
    public async Task<(bool Allowed, string Error)> VerifyAsync(KioskSettings settings, string password,
        string settingsPath)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
            return PasswordService.Verify(password, settings.PasswordHash)
                ? (true, string.Empty) : (false, "Contraseña incorrecta.");

        if (!Uri.TryCreate(settings.ServerUrl, UriKind.Absolute, out var serverUri) ||
            (serverUri.Scheme != Uri.UriSchemeHttps &&
             !(serverUri.Scheme == Uri.UriSchemeHttp && serverUri.IsLoopback)))
            return (false, "La dirección del panel no es válida para comprobar la contraseña.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post,
                serverUri.ToString().TrimEnd('/') + "/api/kiosk/password/verify")
            {
                Content = JsonContent.Create(new { password })
            };
            if (!string.IsNullOrWhiteSpace(settings.ServerApiKey))
                request.Headers.Add("X-Api-Key", settings.ServerApiKey);
            using var response = await http.SendAsync(request);
            if (response.StatusCode == HttpStatusCode.Forbidden)
                return (false, "Contraseña incorrecta.");
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
                return (false, "Demasiados intentos. Espera unos minutos.");
            if ((int)response.StatusCode >= 500)
                return VerifyOffline(settings, password);
            if (!response.IsSuccessStatusCode)
                return (false, "No se pudo validar con el panel. Comprueba la conexión y la clave API.");

            string body = await response.Content.ReadAsStringAsync();
            var verifier = JsonConvert.DeserializeObject<PanelPasswordProvisioning>(body);
            if (verifier?.IsCompatible() != true || !PasswordService.Verify(password, verifier.PasswordHash))
                return (false, "La respuesta de seguridad del panel no es válida.");
            if (settings.ApplyVerifiedPanelPassword(verifier)) settings.Save(settingsPath);
            return (true, string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return VerifyOffline(settings, password);
        }
        catch (Exception ex) when (ex is JsonException or UriFormatException)
        {
            return (false, "La respuesta de seguridad del panel no es válida.");
        }
        catch (Exception)
        {
            return (false, "No se pudo comprobar la contraseña con el panel.");
        }
    }

    private static (bool Allowed, string Error) VerifyOffline(KioskSettings settings, string password)
        => PasswordService.Verify(password, settings.PasswordHash)
            ? (true, string.Empty)
            : (false, "Panel sin conexión. Usa la última contraseña validada en este equipo.");
}
