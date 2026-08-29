using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests;

public sealed class FichaPdfUrlTests
{
    [Theory]
    [InlineData("https://panel.example.test", "https://panel.example.test/ficha/")]
    [InlineData("https://panel.example.test/", "https://panel.example.test/ficha/")]
    public void Resolve_UsaElServidorHttpsConfigurado(string serverUrl, string expected) =>
        Assert.Equal(expected, FichaPdfUrl.Resolve(serverUrl));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("no-es-una-url")]
    public void Resolve_SinServidorPublicoUsaFallback(string? serverUrl) =>
        Assert.Equal(FichaPdfUrl.PublicFallback, FichaPdfUrl.Resolve(serverUrl));
}
