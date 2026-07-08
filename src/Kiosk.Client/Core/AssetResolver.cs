using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace KioskClinicaPC.Core
{
    /// <summary>
    /// Empareja una marca (Asus, Dell…) o un componente ("Intel Core i5") con un archivo de imagen.
    /// Busca primero en el override %LOCALAPPDATA%\KioskClinicaPC\… y, si no, en las imágenes
    /// EMPAQUETADAS junto al .exe (Assets\Brands, Assets\SpecImages). Emparejamiento por "slug"
    /// alfanumérico: el nombre del archivo debe estar contenido en el texto detectado (o al revés,
    /// para marcas). Devuelve ruta absoluta o null.
    /// </summary>
    public static class AssetResolver
    {
        private static readonly string[] Extensions = { ".png", ".jpg", ".jpeg", ".webp", ".bmp" };

        /// <summary>Minúsculas + solo letras/dígitos: "ASUSTeK Computer Inc." → "asustekcomputerinc".</summary>
        public static string Normalize(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "";
            var sb = new StringBuilder(s.Length);
            foreach (char c in s.ToLowerInvariant())
                if (char.IsLetterOrDigit(c)) sb.Append(c);
            return sb.ToString();
        }

        /// <summary>Logo de marca: archivo cuyo slug está contenido en la marca o viceversa.</summary>
        public static string? ResolveBrandLogo(string? brand)
        {
            string norm = Normalize(brand);
            if (norm.Length == 0) return null;
            return BestMatch(fileNorm => norm.Contains(fileNorm) || fileNorm.Contains(norm),
                             App.BrandsFolderPath, App.BundledBrandsFolderPath);
        }

        /// <summary>
        /// Imagen real del componente. Prueba varios textos candidatos (modelo concreto, valor amigable…)
        /// y devuelve el archivo cuyo slug esté contenido en alguno. Gana el nombre de archivo más largo
        /// (más específico): "intelcorei51135g7.png" vence a "intelcorei5.png".
        /// </summary>
        public static string? ResolveSpecImage(params string?[] candidates)
        {
            var norms = candidates.Select(Normalize).Where(n => n.Length > 0).ToList();
            if (norms.Count == 0) return null;
            return BestMatch(fileNorm => norms.Any(n => n.Contains(fileNorm)),
                             App.SpecImagesFolderPath, App.BundledSpecImagesFolderPath);
        }

        /// <summary>Recorre las carpetas en orden: la primera que dé match gana (override antes que empaquetado).</summary>
        private static string? BestMatch(System.Func<string, bool> matches, params string[] dirs)
        {
            foreach (var dir in dirs)
            {
                string? hit = BestMatchInDir(dir, matches);
                if (hit != null) return hit;
            }
            return null;
        }

        private static string? BestMatchInDir(string dir, System.Func<string, bool> matches)
        {
            if (!Directory.Exists(dir)) return null;
            string? best = null; int bestLen = 0;
            foreach (var file in EnumerateImages(dir))
            {
                string fileNorm = Normalize(Path.GetFileNameWithoutExtension(file));
                if (fileNorm.Length == 0) continue;
                if (fileNorm.Length > bestLen && matches(fileNorm))
                {
                    best = file;
                    bestLen = fileNorm.Length;
                }
            }
            return best;
        }

        // Recursivo: permite organizar en subcarpetas (cpu\, gpu\…). El match sigue siendo por nombre.
        private static IEnumerable<string> EnumerateImages(string dir)
            => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                        .Where(f => Extensions.Contains(Path.GetExtension(f).ToLowerInvariant()));
    }
}
