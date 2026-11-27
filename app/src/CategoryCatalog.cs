using System;
using System.Collections.Generic;
using System.Linq;

namespace PromptDesk
{
    internal static class CategoryCatalog
    {
        internal static List<string> Values(Store db, string scope)
        {
            string prefix = scope + "::";
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (string stored in db.Categories)
            {
                if (stored.StartsWith(prefix, StringComparison.Ordinal)) names.Add(stored.Substring(prefix.Length));
                else if (!stored.Contains("::")) names.Add(stored);
            }
            foreach (var entry in db.Entries.Where(e => e.Get("collection") == scope))
                names.Add(entry.Get("category"));
            foreach (string stored in db.Subfolders.Where(s => s.StartsWith(prefix, StringComparison.Ordinal)))
            {
                string path = stored.Substring(prefix.Length);
                names.Add(path);
                string parent = CategoryFolders.Parent(db, scope, path);
                if (!String.IsNullOrEmpty(parent)) names.Add(parent);
            }
            names.Remove("");
            var preferred = db.CategoryOrder.Where(s => s.StartsWith(prefix, StringComparison.Ordinal))
                .Select(s => s.Substring(prefix.Length)).Where(names.Contains);
            var siblings = preferred.Concat(names.OrderBy(v => v)).Distinct()
                .ToLookup(path => CategoryFolders.Parent(db, scope, path));
            var result = new List<string>();
            var pending = new Stack<string>(siblings[null].Reverse());
            while (pending.Count != 0)
            {
                string path = pending.Pop();
                result.Add(path);
                foreach (string child in siblings[path].Reverse()) pending.Push(child);
            }
            return result;
        }

        internal static List<string> Branch(Store db, string scope, string root)
        {
            return Values(db, scope).Where(path => CategoryFolders.IsAtOrBelow(db, scope, path, root)).ToList();
        }

        internal static void Rename(Store db, string scope, string old, string newName)
        {
            string parent = CategoryFolders.Parent(db, scope, old);
            string target = parent == null ? newName.Trim() : parent + CategoryFolders.Separator + CategoryFolders.LeafName(newName);
            if (target == old) return;
            if (String.IsNullOrWhiteSpace(target) || target == "＋ Nuevo…") throw new Exception("Elegí otro nombre.");
            var values = Values(db, scope);
            var changes = values.Where(path => CategoryFolders.IsAtOrBelow(db, scope, path, old))
                .ToDictionary(path => path, path => target + path.Substring(old.Length));
            if (changes.Count == 0) return;
            var occupied = new HashSet<string>(values.Except(changes.Keys), StringComparer.OrdinalIgnoreCase);
            if (changes.Values.Any(occupied.Contains) || (target != old && changes.ContainsKey(target)))
                throw new Exception("Ya existe una carpeta con ese nombre o ruta en esta colección.");
            UpdateBranch(db, scope, changes);
        }

        internal static void Remove(Store db, string scope, string path)
        {
            UpdateBranch(db, scope, Branch(db, scope, path).ToDictionary(key => key, key => (string)null));
        }

        static void UpdateBranch(Store db, string scope, Dictionary<string, string> changes)
        {
            foreach (var entry in db.Entries.Where(e => e.Get("collection") == scope))
            {
                string replacement;
                if (changes.TryGetValue(entry.Get("category"), out replacement)) entry.Set("category", replacement ?? "");
            }
            string prefix = scope + "::";
            // Bare names from older catalogs belong to both collections. Materialize
            // their scoped equivalents before changing just this collection.
            foreach (string bare in db.Categories.Where(c => !c.Contains("::") && changes.ContainsKey(c)).Distinct().ToList())
            {
                db.Categories.RemoveAll(c => c == bare);
                db.Categories.Add("Personales::" + bare);
                db.Categories.Add("404::" + bare);
            }
            db.Categories = Rewrite(db.Categories, prefix, changes);
            db.Subfolders = Rewrite(db.Subfolders, prefix, changes);
            db.CategoryOrder = Rewrite(db.CategoryOrder, prefix, changes);
            db.DisabledChoices = Rewrite(db.DisabledChoices, "category::" + prefix, changes);
        }

        static List<string> Rewrite(List<string> source, string prefix, Dictionary<string, string> changes)
        {
            var result = new List<string>();
            foreach (string value in source)
            {
                string replacement;
                if (value.StartsWith(prefix, StringComparison.Ordinal) && changes.TryGetValue(value.Substring(prefix.Length), out replacement))
                {
                    if (replacement != null) result.Add(prefix + replacement);
                }
                else result.Add(value);
            }
            return result.Distinct().ToList();
        }
    }
}
