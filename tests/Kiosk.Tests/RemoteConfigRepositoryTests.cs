using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using KioskClinicaPC.Services;
using Xunit;

namespace KioskClinicaPC.Tests
{
    /// <summary>Comportamiento crítico del kiosko: leer del servidor cuando responde y caer a la
    /// caché local cuando no, sin quedarse nunca sin config.</summary>
    public sealed class RemoteConfigRepositoryTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _cachePath;
        private readonly string _hwPath;

        public RemoteConfigRepositoryTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "kiosk-remote-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _cachePath = Path.Combine(_dir, "KioskConfig.json");
            _hwPath = Path.Combine(_dir, "KioskHardware.json");
        }

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
        }

        private static HttpClient Client(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => new HttpClient(new StubHandler(responder));

        [Fact]
        public async Task Sin_servidor_usa_solo_la_cache_local()
        {
            File.WriteAllText(_cachePath, "{\"Price\":\"111\",\"SchemaVersion\":1}");
            // Un cliente que lanzaría si se usara: prueba que NO se toca la red.
            var repo = new RemoteConfigRepository(Client(_ => throw new InvalidOperationException("no debería llamar")),
                baseUrl: null, _cachePath, _hwPath);

            var result = await repo.LoadConfigAsync();

            Assert.Equal("111", result.Config.Price);
        }

        [Fact]
        public async Task Con_servidor_funde_contenido_compartido_y_conserva_lo_local()
        {
            // Local (esta máquina): precio propio. El servidor NO debe pisarlo.
            File.WriteAllText(_cachePath, "{\"Price\":\"111\",\"SchemaVersion\":1}");
            var repo = new RemoteConfigRepository(
                Client(_ => new HttpResponseMessage(HttpStatusCode.OK)
                {
                    // El servidor manda contenido compartido (servicios de tienda) e intenta un precio
                    // que debe ignorarse por ser un campo por-máquina.
                    Content = new StringContent("{\"Price\":\"999\",\"ShopServices\":\"Reparación y montaje\",\"SchemaVersion\":1}")
                }),
                baseUrl: "https://server.test", _cachePath, _hwPath);

            var result = await repo.LoadConfigAsync();

            Assert.Equal("111", result.Config.Price);                          // precio LOCAL conservado
            Assert.Equal("Reparación y montaje", result.Config.ShopServices);  // contenido COMPARTIDO del servidor
            Assert.True(File.Exists(_cachePath));
            string cached = File.ReadAllText(_cachePath);
            Assert.Contains("Reparación y montaje", cached); // se cacheó la fusión para el próximo arranque offline
            Assert.Contains("111", cached);
            Assert.DoesNotContain("999", cached);            // el precio del servidor no se coló
        }

        [Fact]
        public async Task Servidor_caido_cae_a_la_cache()
        {
            File.WriteAllText(_cachePath, "{\"Price\":\"555\",\"SchemaVersion\":1}");
            var repo = new RemoteConfigRepository(
                Client(_ => throw new HttpRequestException("sin red")),
                baseUrl: "https://server.test", _cachePath, _hwPath);

            var result = await repo.LoadConfigAsync();

            Assert.Equal("555", result.Config.Price); // contenido viejo, pero nunca pantalla en negro
        }

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
            public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> responder) => _responder = responder;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
                => Task.FromResult(_responder(request));
        }
    }
}
