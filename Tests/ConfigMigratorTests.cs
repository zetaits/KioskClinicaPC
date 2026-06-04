using KioskClinicaPC.Core;
using Newtonsoft.Json;
using Xunit;

namespace KioskClinicaPC.Tests
{
    // El migrador protege contra pérdida de datos al cambiar el formato de config. Si esto se rompe,
    // una actualización de la app podría resetear el contenido del cliente: es el test más crítico.
    public class ConfigMigratorTests
    {
        [Fact]
        public void LegacyConfigSinVersion_SeSellaConLaVersionActual_YSeMarcaCambiado()
        {
            string json = "{\"Price\":\"999\"}"; // archivo previo al versionado

            var config = ConfigMigrator.Migrate(json, out bool changed);

            Assert.True(changed);
            Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
        }

        [Fact]
        public void ConfigEnVersionActual_NoSeMarcaCambiado()
        {
            string json = JsonConvert.SerializeObject(new AppConfig { SchemaVersion = AppConfig.CurrentSchemaVersion, Price = "999" });

            var config = ConfigMigrator.Migrate(json, out bool changed);

            Assert.False(changed);
            Assert.Equal(AppConfig.CurrentSchemaVersion, config.SchemaVersion);
        }

        [Fact]
        public void Migracion_PreservaLosDatosDelUsuario()
        {
            string json = "{\"Price\":\"1299\",\"ChassisName\":\"ASUS\",\"AttractSlides\":[{\"Title1\":\"Hola\"}]}";

            var config = ConfigMigrator.Migrate(json, out _);

            Assert.Equal("1299", config.Price);
            Assert.Equal("ASUS", config.ChassisName);
            Assert.Single(config.AttractSlides);
            Assert.Equal("Hola", config.AttractSlides[0].Title1);
        }

        [Fact]
        public void JsonCorrupto_Lanza_ParaQueElLlamadorRespalde()
        {
            // No es un esquema antiguo: es JSON malformado → debe lanzar (el llamador hace backup).
            Assert.ThrowsAny<System.Exception>(() => ConfigMigrator.Migrate("{ esto no es json", out _));
        }
    }
}
