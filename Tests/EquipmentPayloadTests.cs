using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using KioskClinicaPC.Core.Config;
using Newtonsoft.Json.Linq;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class EquipmentPayloadTests
    {
        private const string BaseUrl = "https://zetaits.github.io/KioskClinicaPC/";

        private static AppConfig MakeConfig() => new AppConfig
        {
            ChassisName = "TORRE GAMING",
            ModelName = "Nébula X",
            Price = "1299"
        };

        private static EquipmentPayload.SpecLine[] MakeSpecs() => new[]
        {
            new EquipmentPayload.SpecLine { Id = "cpu", Label = "Procesador", Value = "Ryzen 7 5800X", Detail = "8 núcleos / 16 hilos · 4,7 GHz boost" },
            new EquipmentPayload.SpecLine { Id = "ram", Label = "Memoria RAM", Value = "32 GB DDR4", Detail = "2×16 GB 3600 MHz" }
        };

        private static string DecodePayload(string url)
        {
            string hash = url.Substring(url.IndexOf('#') + 1);
            string b64 = hash.Replace('-', '+').Replace('_', '/');
            while (b64.Length % 4 != 0) b64 += "=";
            byte[] bytes = Convert.FromBase64String(b64);

            // Mismo camino que la web: deflate crudo (pako.inflateRaw ⇔ DeflateStream).
            using var ms = new MemoryStream(bytes);
            using var inflate = new DeflateStream(ms, CompressionMode.Decompress);
            using var reader = new StreamReader(inflate, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        [Fact]
        public void BuildUrl_PayloadEsDeflateCrudoDecodificable()
        {
            string? url = EquipmentPayload.BuildUrl(BaseUrl, MakeConfig(), MakeSpecs(), shopName: null);

            Assert.NotNull(url);
            Assert.StartsWith(BaseUrl + "#", url);

            var dto = JObject.Parse(DecodePayload(url!));
            Assert.Equal("TORRE GAMING", (string?)dto["ch"]);
            Assert.Equal("Ryzen 7 5800X", (string?)dto["c"]![0]!["v"]);
            Assert.Equal("8 núcleos / 16 hilos · 4,7 GHz boost", (string?)dto["c"]![0]!["d"]);
        }

        [Fact]
        public void BuildUrl_PayloadNoLlevaCabeceraGzip()
        {
            // La web distingue formatos por los magic bytes de gzip: el payload nuevo no debe tenerlos.
            string? url = EquipmentPayload.BuildUrl(BaseUrl, MakeConfig(), MakeSpecs(), shopName: null);
            string b64 = url!.Substring(url.IndexOf('#') + 1).Replace('-', '+').Replace('_', '/');
            while (b64.Length % 4 != 0) b64 += "=";
            byte[] bytes = Convert.FromBase64String(b64);

            Assert.False(bytes.Length > 2 && bytes[0] == 0x1f && bytes[1] == 0x8b);
        }

        [Fact]
        public void BuildUrl_SinDetalles_OmiteDetailYAcorta()
        {
            string? full = EquipmentPayload.BuildUrl(BaseUrl, MakeConfig(), MakeSpecs(), shopName: null);
            string? slim = EquipmentPayload.BuildUrl(BaseUrl, MakeConfig(), MakeSpecs(), shopName: null, includeDetails: false);

            var dto = JObject.Parse(DecodePayload(slim!));
            Assert.Null(dto["c"]![0]!["d"]);
            Assert.Equal("Ryzen 7 5800X", (string?)dto["c"]![0]!["v"]);
            Assert.True(slim!.Length < full!.Length);
        }
    }
}
