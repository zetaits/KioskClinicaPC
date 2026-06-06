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
        public void Label_FormateaMeses()
        {
            Assert.Equal("36 MESES DE GARANTÍA", Warranty.Label("Nuevo"));
            Assert.Equal("12 MESES DE GARANTÍA", Warranty.Label("Ocasion"));
        }
    }
}
