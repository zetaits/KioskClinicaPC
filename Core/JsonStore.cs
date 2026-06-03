using System.IO;

namespace KioskClinicaPC.Core
{
    /// <summary>Escritura de archivos resistente a fallos a mitad de escritura.</summary>
    public static class JsonStore
    {
        /// <summary>
        /// Escribe de forma atómica: vuelca a un temporal y lo mueve sobre el destino.
        /// Evita dejar el archivo truncado/corrupto si el proceso falla durante el volcado.
        /// </summary>
        public static void WriteAtomic(string path, string content)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, content);
            File.Move(tmp, path, overwrite: true);
        }
    }
}
