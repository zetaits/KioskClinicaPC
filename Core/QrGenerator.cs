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
        /// ECC nivel L para maximizar la capacidad (las URLs con datos embebidos son largas).</summary>
        public static BitmapSource? Generate(string? text, int pixelsPerModule = 20)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            try
            {
                using var generator = new QRCodeGenerator();
                using var data = generator.CreateQrCode(text, QRCodeGenerator.ECCLevel.L);
                var png = new PngByteQRCode(data);
                byte[] bytes = png.GetGraphic(pixelsPerModule);

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
