using KioskClinicaPC.Core;
using Xunit;

namespace KioskClinicaPC.Tests
{
    public class PasswordServiceTests
    {
        [Fact]
        public void Hash_LuegoVerify_ConLaMismaClave_EsTrue()
        {
            string stored = PasswordService.Hash("clave-de-prueba-segura");
            Assert.True(PasswordService.Verify("clave-de-prueba-segura", stored));
        }

        [Fact]
        public void Verify_ConClaveIncorrecta_EsFalse()
        {
            string stored = PasswordService.Hash("correcta");
            Assert.False(PasswordService.Verify("incorrecta", stored));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("sin-dos-puntos")]
        [InlineData("no-base64:tampoco")]
        [InlineData(":")]
        [InlineData("AA==:")]
        public void Verify_ConAlmacenadoInvalido_EsFalse(string? stored)
        {
            Assert.False(PasswordService.Verify("loquesea", stored));
        }

        [Fact]
        public void Hash_GeneraSaltDistintoCadaVez()
        {
            // Mismo password, hash distinto (salt aleatorio) → no se puede comparar por igualdad de cadena.
            Assert.NotEqual(PasswordService.Hash("misma"), PasswordService.Hash("misma"));
        }
    }
}
