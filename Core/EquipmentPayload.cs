using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Newtonsoft.Json;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Empaqueta la ficha del equipo (identidad, precio, componentes presentes) en un payload compacto
    /// que viaja DENTRO del QR (en el #hash de la URL). La web destino lo descomprime y genera el PDF
    /// en el propio móvil del cliente: sin backend, sin subir nada, sin que el kiosko necesite red.
    ///
    /// Formato: JSON con claves cortas → gzip → Base64Url. La web hace el camino inverso (pako.ungzip).
    /// Se usan claves de 1-2 letras para que quepa de sobra en un QR (límite práctico ~2,9 KB).
    /// </summary>
    public static class EquipmentPayload
    {
        // DTO con nombres cortos: cada byte cuenta para el tamaño del QR.
        private class Dto
        {
            [JsonProperty("v")] public int Version { get; set; } = 1;
            [JsonProperty("ch")] public string? Chassis { get; set; }
            [JsonProperty("mo")] public string? Model { get; set; }
            [JsonProperty("fa")] public string? Family { get; set; }
            [JsonProperty("sk")] public string? Sku { get; set; }
            [JsonProperty("pr")] public string? Price { get; set; }
            [JsonProperty("dp")] public string? DiscountedPrice { get; set; }
            [JsonProperty("sh")] public string? Shop { get; set; }
            [JsonProperty("ad")] public string? Address { get; set; }
            [JsonProperty("c")] public List<Comp> Components { get; set; } = new();
        }

        private class Comp
        {
            [JsonProperty("i")] public string? Id { get; set; }
            [JsonProperty("l")] public string? Label { get; set; }
            [JsonProperty("v")] public string? Value { get; set; }
            [JsonProperty("d")] public string? Detail { get; set; }
        }

        public class SpecLine
        {
            public string? Id { get; set; }
            public string? Label { get; set; }
            public string? Value { get; set; }
            public string? Detail { get; set; }
        }

        /// <summary>Construye la URL completa "{baseUrl}#{payload}" o null si no hay baseUrl válida.</summary>
        public static string? BuildUrl(string? baseUrl, AppConfig config, IEnumerable<SpecLine> specs, string? shopName)
        {
            if (string.IsNullOrWhiteSpace(baseUrl)) return null;

            var dto = new Dto
            {
                Chassis = NullIfEmpty(config.ChassisName),
                Model = NullIfEmpty(config.ModelName),
                Family = NullIfEmpty(config.Family),
                Sku = NullIfEmpty(config.Sku),
                Price = NullIfEmpty(config.Price),
                DiscountedPrice = NullIfEmpty(config.DiscountedPrice),
                Shop = NullIfEmpty(shopName),
                // La dirección estándar la pone la web (docs/app.js → SHOP.address): no la mandamos.
                // Solo viaja si el comercio la ha personalizado.
                Address = IsDefaultAddress(config.ShopAddress) ? null : NullIfEmpty(config.ShopAddress)
            };

            foreach (var s in specs)
            {
                dto.Components.Add(new Comp
                {
                    Id = s.Id,
                    // La etiqueta estándar la conoce la web por el id (docs/app.js → LABELS): no la
                    // mandamos para aligerar el QR. Solo viaja si el comercio la ha personalizado.
                    Label = IsDefaultLabel(s.Id, s.Label) ? null : NullIfEmpty(s.Label),
                    Value = NullIfEmpty(s.Value),
                    Detail = NullIfEmpty(s.Detail)
                });
            }

            string json = JsonConvert.SerializeObject(dto, new JsonSerializerSettings
            {
                NullValueHandling = NullValueHandling.Ignore
            });

            string encoded = Base64UrlEncode(Gzip(json));
            string sep = baseUrl.Contains('#') ? "" : "#";
            return baseUrl + sep + encoded;
        }

        private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

        // Etiquetas canónicas por id (mismas que docs/app.js → LABELS). Fuente única: SpecCatalog.
        private static readonly Dictionary<string, string> DefaultLabels =
            SpecCatalog.DefaultMarketing()
                .Where(m => !string.IsNullOrWhiteSpace(m.Id) && !string.IsNullOrWhiteSpace(m.Label))
                .ToDictionary(m => m.Id!, m => m.Label!, StringComparer.OrdinalIgnoreCase);

        /// <summary>True si la etiqueta coincide con la estándar del componente: en ese caso no hace
        /// falta enviarla (la web la deduce del id), encogiendo el QR.</summary>
        private static bool IsDefaultLabel(string? id, string? label)
        {
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(label)) return false;
            return DefaultLabels.TryGetValue(id!, out var def)
                && string.Equals(def.Trim(), label!.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True si la dirección es la estándar: la web la conoce, no hace falta mandarla.</summary>
        private static bool IsDefaultAddress(string? address) =>
            !string.IsNullOrWhiteSpace(address) &&
            string.Equals(address!.Trim(), AppConfig.DefaultShopAddress, StringComparison.OrdinalIgnoreCase);

        private static byte[] Gzip(string text)
        {
            byte[] raw = Encoding.UTF8.GetBytes(text);
            using var ms = new MemoryStream();
            using (var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true))
                gz.Write(raw, 0, raw.Length);
            return ms.ToArray();
        }

        private static string Base64UrlEncode(byte[] data) =>
            Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
