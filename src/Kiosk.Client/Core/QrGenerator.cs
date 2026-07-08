using System;
using System.IO;
using System.Windows.Media.Imaging;
using QRCoder;
using Serilog;

namespace KioskClinicaPC.Core
{
    /// <summary>Genera un código QR real (escaneable) como imagen WPF a partir de una URL/texto.
    /// Usa el renderer PNG de QRCoder (sin dependencia de System.Drawing en tiempo de ejecución).</summary>
    public static class QrGenerator
    {
        /// <summary>Devuelve un BitmapSource con el QR, o null si el texto es vacío o falla la generación.
        /// ECC nivel L para maximizar la capacidad (las URLs con datos embebidos son largas).
        /// El bitmap se genera al mayor múltiplo entero de módulos que quepa en targetPixels y se
        /// muestra 1:1 (Stretch=None): reescalar un QR denso emborrona los módulos y arruina el escaneo.</summary>
        public static BitmapSource? Generate(string? text, int targetPixels)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
                // Sin quiet zone en el bitmap: la aporta el Border blanco que lo enmarca (su Padding
                // debe ser ≥4 módulos). Así cada módulo gana píxeles: p.ej. payload v15 en 232px pasa
                // de 2 a 3 px/módulo, y en QRs densos eso decide si el móvil lo engancha o no.
                int modules = data.ModuleMatrix.Count - 8; // ModuleMatrix incluye 4 módulos de quiet zone por lado
                int pixelsPerModule = Math.Max(1, targetPixels / modules);
                var png = new PngByteQRCode(data);
                byte[] bytes = png.GetGraphic(pixelsPerModule, new byte[] { 0, 0, 0 }, new byte[] { 255, 255, 255 }, drawQuietZones: false);

                var image = new BitmapImage();
                using (var ms = new MemoryStream(bytes))
                {
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = ms;
                    image.EndInit();
                }
                image.Freeze(); // utilizable desde cualquier hilo / sin fugas
                return image;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "No se pudo generar el código QR.");
                return null;
            }
        }
    }
}
