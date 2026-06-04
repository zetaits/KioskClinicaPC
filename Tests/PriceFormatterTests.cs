using System.Text.RegularExpressions;
using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class PriceFormatterTests
    {
        // es-ES separa el número del € con un espacio duro (NBSP/NNBSP) que varía entre versiones
        // de ICU. Comparamos sin espacios para no acoplar el test a ese detalle.
        private static string NoSpaces(string s) => Regex.Replace(s, @"\s", "");

        [Fact]
        public void Format_NumeroEntero_MuestraMonedaSinDecimales()
        {
            Assert.Equal("1.299€", NoSpaces(PriceFormatter.Format("1299")));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("   ")]
        public void Format_Vacio_DevuelveCadenaVacia(string? input)
        {
            Assert.Equal("", PriceFormatter.Format(input));
        }

        [Fact]
        public void Format_NoNumerico_DevuelveElTextoTalCual()
        {
            Assert.Equal("Consultar", PriceFormatter.Format("Consultar"));
        }

        [Fact]
        public void Discount_CalculaPorcentajeRedondeado()
        {
            Assert.Equal("-20%", PriceFormatter.Discount("1000", "800"));
        }

        [Theory]
        [InlineData("1000", null)]
        [InlineData(null, "800")]
        [InlineData("0", "800")]      // precio original 0 → evita división por cero
        [InlineData("abc", "800")]
        public void Discount_DatosInvalidos_DevuelveCadenaVacia(string? price, string? discounted)
        {
            Assert.Equal("", PriceFormatter.Discount(price, discounted));
        }

        [Fact]
        public void Monthly_DivideEntreDoce()
        {
            Assert.Equal("100,00€", NoSpaces(PriceFormatter.Monthly("1200")));
        }

        [Fact]
        public void Monthly_Vacio_DevuelveCadenaVacia()
        {
            Assert.Equal("", PriceFormatter.Monthly(null));
        }
    }
}
