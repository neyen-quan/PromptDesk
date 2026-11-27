using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PromptDesk
{
    public static class CategoryFolders
    {
        public const string Separator = " › ";

        public static string LeafName(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0 || name == "＋ Nuevo…")
                throw new ArgumentException("Ingresá un nombre de subcarpeta.");
            if (name.Contains("›") || name.Contains("::"))
                throw new ArgumentException("El nombre de una subcarpeta no admite los separadores › ni ::.");
            return name;
        }

        public static string Parent(Store db, string scope, string path)
        {
            if (String.IsNullOrEmpty(path) || !db.Subfolders.Contains(scope + "::" + path)) return null;
            return path.Substring(0, path.LastIndexOf(Separator, StringComparison.Ordinal));
        }

        public static bool IsAtOrBelow(Store db, string scope, string path, string ancestor)
        {
            while (path != null)
            {
                if (path == ancestor) return true;
                path = Parent(db, scope, path);
            }
            return false;
        }

        public static string Create(Store db, string scope, string parent, string name)
        {
            if (scope != "Personales" && scope != "404")
                throw new ArgumentException("Elegí una colección para crear la subcarpeta.");
            var existing = CategoryCatalog.Values(db, scope);
            if (!existing.Contains(parent))
                throw new ArgumentException("Elegí la carpeta donde querés crear la subcarpeta.");
            name = LeafName(name);
            string path = parent + Separator + name;
            if (existing.Contains(path, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentException("Ya existe una categoría o subcarpeta con ese nombre en esa ubicación.");
            string prefix = scope + "::";
            // A category discovered only through a prompt must outlive that prompt once it has children.
            if (!db.Categories.Contains(prefix + parent) && !db.Categories.Contains(parent))
                db.Categories.Add(prefix + parent);
            db.Subfolders.Add(prefix + path);
            db.Categories.Add(prefix + path);
            return path;
        }

        internal static void Validate(IEnumerable<string> folders)
        {
            foreach (string folder in folders)
            {
                int start = folder.IndexOf("::", StringComparison.Ordinal);
                int split = folder.LastIndexOf(Separator, StringComparison.Ordinal);
                if (start <= 0 || split <= start + 2 || split + Separator.Length >= folder.Length ||
                    (folder.Substring(0, start) != "Personales" && folder.Substring(0, start) != "404") ||
                    String.IsNullOrWhiteSpace(folder.Substring(start + 2, split - start - 2)) ||
                    String.IsNullOrWhiteSpace(folder.Substring(split + Separator.Length)))
                    throw new InvalidDataException("El catálogo contiene una ruta de subcarpeta inválida.");
            }
        }
    }
}
