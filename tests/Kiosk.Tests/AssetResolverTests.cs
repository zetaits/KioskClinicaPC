using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    // Normalize es la base del emparejado nombre-de-archivo ↔ marca/componente. Si cambia su
    // comportamiento, dejan de encontrarse logos e imágenes silenciosamente.
    public class AssetResolverTests
    {
        [Theory]
        [InlineData("ASUSTeK Computer Inc.", "asustekcomputerinc")]
        [InlineData("Intel Core i5-1135G7", "intelcorei51135g7")]
        [InlineData("  NVIDIA  GeForce RTX 4060 ", "nvidiageforcertx4060")]
        public void Normalize_DejaSoloAlfanumericoEnMinusculas(string input, string expected)
        {
            Assert.Equal(expected, AssetResolver.Normalize(input));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        public void Normalize_VacioONulo_DevuelveCadenaVacia(string? input)
        {
            Assert.Equal("", AssetResolver.Normalize(input!));
        }
    }
}
