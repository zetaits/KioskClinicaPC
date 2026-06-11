using Newtonsoft.Json.Linq;

namespace KioskClinicaPC.Core.Config
{
    /// <summary>
    /// Migra KioskConfig.json entre versiones de esquema. Trabaja sobre el JSON crudo (JObject)
    /// para poder renombrar/mover campos sin perder datos del usuario, en vez de dejar que
    /// Newtonsoft descarte en silencio lo que ya no encaja. Distingue "esquema antiguo"
    /// (migrable) de "archivo corrupto" (JObject.Parse lanza) para no resetear contenido válido.
    /// </summary>
    public static class ConfigMigrator
    {
        /// <summary>
        /// Parsea y migra el JSON al esquema actual. Lanza si el JSON está malformado (corrupto),
        /// para que el llamador aplique su política de respaldo. <paramref name="changed"/> indica
        /// si hubo migración (el llamador debe persistir el resultado).
        /// </summary>
        public static AppConfig Migrate(string json, out bool changed)
        {
            changed = false;
            var root = JObject.Parse(json); // lanza JsonReaderException si está corrupto → lo maneja el llamador
            int from = root.Value<int?>("SchemaVersion") ?? 0;

            // Migraciones ordenadas: cada bloque transforma el JObject de la versión N a la N+1.
            // Al añadir una versión nueva, sube AppConfig.CurrentSchemaVersion y agrega su bloque.
            if (from < 1)
            {
                // 0 -> 1: introducción del versionado. Sin cambios estructurales en los datos.
                from = 1;
            }

            if (from != AppConfig.CurrentSchemaVersion || root.Value<int?>("SchemaVersion") == null)
                changed = true;

            root["SchemaVersion"] = AppConfig.CurrentSchemaVersion;
            return root.ToObject<AppConfig>() ?? new AppConfig();
        }
    }
}
