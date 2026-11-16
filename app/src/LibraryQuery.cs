using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PromptDesk
{
    // Shared by the library and table views, without creating controls per record.
    public static class LibraryQuery
    {
        public static List<Entry> Filter(IEnumerable<Entry> entries, string collection,
            string category, string model, string view, string query, string sortKey,
            bool descending)
        {
            string[] terms = (query ?? "").Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
            CompareInfo comparison = CultureInfo.CurrentCulture.CompareInfo;
            IEnumerable<Entry> matches = entries.Where(entry =>
                (String.IsNullOrEmpty(collection) || collection == "Todos" || entry.Get("collection") == collection) &&
                (String.IsNullOrEmpty(category) || entry.Get("category") == category) &&
                (String.IsNullOrEmpty(model) || entry.Get("model") == model) &&
                (view != "Favoritos" || entry.Favorite) &&
                (view != "Recientes" || entry.LastUsed.Length > 0) &&
                terms.All(term => entry.Cells.Values.Any(value => comparison.IndexOf(value, term,
                    CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)));

            if (view == "Recientes")
                return matches.OrderByDescending(entry => entry.LastUsed, StringComparer.Ordinal).ToList();

            string field = String.IsNullOrEmpty(sortKey) ? "name" : sortKey;
            return (descending
                ? matches.OrderByDescending(entry => entry.Get(field), StringComparer.CurrentCultureIgnoreCase)
                : matches.OrderBy(entry => entry.Get(field), StringComparer.CurrentCultureIgnoreCase)).ToList();
        }
    }
}
