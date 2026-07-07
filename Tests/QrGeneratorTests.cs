using System.Linq;
using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class QrGeneratorTests
    {
        [Fact]
        public void Generate_BitmapCabeEnElTargetSinReescalado()
        {
            // Payload realista (~450 chars): el bitmap debe salir a escala entera de módulos,
            // nunca mayor que el hueco (Stretch=None lo muestra 1:1).
            string url = "https://zetaits.github.io/KioskClinicaPC/#" + string.Concat(Enumerable.Repeat("Abc123_-", 51));
            var qr = QrGenerator.Generate(url, 232);

            Assert.NotNull(qr);
            Assert.True(qr!.PixelWidth <= 232, $"El QR ({qr.PixelWidth}px) desborda el hueco de 232px.");
            Assert.True(qr.PixelWidth >= 116, $"El QR ({qr.PixelWidth}px) desaprovecha el hueco: la escala entera debería llegar al menos a la mitad.");
        }

        [Fact]
        public void Generate_TextoCorto_TambienCabe()
        {
            var qr = QrGenerator.Generate("https://zetaits.github.io/KioskClinicaPC/", 72);
            Assert.NotNull(qr);
            Assert.True(qr!.PixelWidth <= 72);
        }

        [Fact]
        public void Generate_TextoVacio_DevuelveNull()
        {
            Assert.Null(QrGenerator.Generate("   ", 232));
        }
    }
}
