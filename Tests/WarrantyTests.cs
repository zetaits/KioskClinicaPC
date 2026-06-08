using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class WarrantyTests
    {
        [Theory]
        [InlineData("Nuevo", 36)]
        [InlineData("nuevo", 36)]
        [InlineData("Ocasion", 12)]
        [InlineData("", 12)]
        [InlineData(null, 12)]
        public void Months_DependeDelEstado(string? condition, int expected)
        {
            Assert.Equal(expected, Warranty.Months(condition));
        }

        [Fact]
        public void Label_FormateaAnios()
        {
            Assert.Equal("3 AÑOS DE GARANTÍA", Warranty.Label("Nuevo"));
            Assert.Equal("1 AÑO DE GARANTÍA", Warranty.Label("Ocasion"));
        }
    }
}
