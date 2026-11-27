using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PromptDesk
{
    // The wire format stays compatible with existing local backups. Parsing and
    // merging never touch the active Store or the filesystem.
    public static class CloudCatalog
    {
        public const int MaxBytes = 10 * 1024 * 1024;
        internal static readonly string[] RequiredFields =
            { "collection", "category", "name", "description", "model", "prompt", "note" };

        public static string Serialize(Store store)
        {
            if (store == null) throw new ArgumentNullException("store");
            return ReadDocument(store.Document()).Text();
        }

        public static void Validate(string xml) { Read(xml); }

        public static XDocument ParseDocument(string xml) { return Read(xml).Document(); }

        public static bool Equivalent(string left, string right)
        {
            if (left == null || right == null) return left == right;
            return Read(left).Text() == Read(right).Text();
        }

        internal sealed class Catalog
        {
            internal List<Field> Fields = new List<Field>();
            internal List<Entry> Entries = new List<Entry>();
            internal List<string> Categories = new List<string>(), Models = new List<string>();
            internal List<string> ColumnOrder = new List<string>(), CategoryOrder = new List<string>();
            internal List<string> DisabledChoices = new List<string>();

            internal XDocument Document()
            {
                return new XDocument(new XElement("promptdesk", new XAttribute("version", "1"),
                    new XElement("columns", Fields.Select(f => new XElement("col",
                        new XAttribute("id", f.Id), new XAttribute("title", f.Title), new XAttribute("visible", f.Visible)))),
                    List("categories", "category", Categories, false),
                    List("models", "model", Models, false),
                    List("columnOrder", "column", ColumnOrder, true),
                    List("categoryOrder", "category", CategoryOrder, true),
                    List("disabledChoices", "choice", DisabledChoices, false),
                    new XElement("records", Entries.OrderBy(e => e.Id, StringComparer.Ordinal).Select(e => EntryXml(e, true)))));
            }

            internal string Text()
            {
                string value = Write(Document());
                CheckSize(value);
                return value;
            }

            static XElement List(string name, string item, IEnumerable<string> values, bool ordered)
            {
                IEnumerable<string> unique = values.Distinct(StringComparer.Ordinal);
                if (!ordered) unique = unique.OrderBy(v => v, StringComparer.Ordinal);
                return new XElement(name, unique.Select(v => new XElement(item, v)));
            }
        }

        internal static Catalog Read(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) throw Invalid("El catálogo está vacío.");
            CheckSize(value);
            using (var input = new StringReader(value))
            using (var reader = XmlReader.Create(input, new XmlReaderSettings {
                DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null,
                MaxCharactersInDocument = MaxBytes, IgnoreWhitespace = false
            }))
                return ReadDocument(XDocument.Load(reader, LoadOptions.PreserveWhitespace));
        }

        internal static Catalog ReadDocument(XDocument document)
        {
            XElement root = document.Root;
            if (document.DocumentType != null || root == null || root.Name != "promptdesk" || (string)root.Attribute("version") != "1")
                throw Invalid("El catálogo no es compatible con PromptDesk.");
            Attributes(root, "version");
            Children(root, "columns", "categories", "models", "columnOrder", "categoryOrder", "disabledChoices", "records");
            foreach (string name in new[] { "columns", "categories", "models", "columnOrder", "categoryOrder", "disabledChoices", "records" })
                if (root.Elements(name).Skip(1).Any()) throw Invalid("El catálogo contiene secciones repetidas.");
            if (root.Element("columns") == null || root.Element("records") == null)
                throw Invalid("El catálogo está incompleto.");
            var result = new Catalog();
            XElement columns = root.Element("columns");
            Attributes(columns); Children(columns, "col");
            var fieldIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement item in columns.Elements("col"))
            {
                Attributes(item, "id", "title", "visible"); Children(item);
                string id = (string)item.Attribute("id"), title = (string)item.Attribute("title");
                if (String.IsNullOrWhiteSpace(id) || String.IsNullOrEmpty(title) || !fieldIds.Add(id))
                    throw Invalid("El catálogo contiene columnas inválidas o repetidas.");
                BooleanAttribute(item, "visible");
                result.Fields.Add(new Field { Id = id, Title = title, Visible = (string)item.Attribute("visible") != "false" });
            }
            if (RequiredFields.Any(id => !fieldIds.Contains(id))) throw Invalid("Faltan columnas obligatorias.");
            XElement records = root.Element("records");
            Attributes(records); Children(records, "record");
            var recordIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (XElement item in records.Elements("record"))
            {
                Attributes(item, "id", "favorite", "archived", "hotkey", "lastUsed"); Children(item, "cell", "variables");
                string id = (string)item.Attribute("id");
                if (String.IsNullOrWhiteSpace(id) || !recordIds.Add(id)) throw Invalid("El catálogo contiene identificadores inválidos o repetidos.");
                BooleanAttribute(item, "favorite");
                BooleanAttribute(item, "archived");
                foreach (XElement cell in item.Elements("cell")) { Attributes(cell, "key"); TextOnly(cell); }
                if (item.Elements("variables").Skip(1).Any()) throw Invalid("Un prompt contiene variables repetidas.");
                XElement variables = item.Element("variables");
                if (variables != null)
                {
                    Attributes(variables); Children(variables, "var");
                    foreach (XElement variable in variables.Elements("var"))
                    {
                        Attributes(variable, "token", "label", "value");
                        Children(variable, "option");
                        foreach (XElement option in variable.Elements("option"))
                        {
                            Attributes(option, "label", "value");
                            Children(option);
                            if (String.IsNullOrWhiteSpace((string)option.Attribute("label")) ||
                                String.IsNullOrWhiteSpace((string)option.Attribute("value")))
                                throw Invalid("Una opción de variable necesita nombre y texto.");
                        }
                    }
                }
                result.Entries.Add(Entry.Read(item));
            }
            result.Categories = ReadList(root, "categories", "category");
            result.Models = ReadList(root, "models", "model");
            result.ColumnOrder = ReadList(root, "columnOrder", "column");
            result.CategoryOrder = ReadList(root, "categoryOrder", "category");
            result.DisabledChoices = ReadList(root, "disabledChoices", "choice");
            return result;
        }

        static List<string> ReadList(XElement root, string section, string item)
        {
            XElement list = root.Element(section);
            if (list == null) return new List<string>();
            Attributes(list); Children(list, item);
            foreach (XElement value in list.Elements(item)) { Attributes(value); TextOnly(value); }
            return list.Elements(item).Select(e => e.Value).Distinct(StringComparer.Ordinal).ToList();
        }

        static void Attributes(XElement element, params string[] names)
        {
            if (element.Attributes().Any(a => !names.Contains(a.Name.ToString())))
                throw Invalid("El catálogo contiene atributos de una versión no compatible.");
        }

        static void Children(XElement element, params string[] names)
        {
            if (element.Elements().Any(e => !names.Contains(e.Name.ToString())) ||
                element.Nodes().OfType<XText>().Any(t => !String.IsNullOrWhiteSpace(t.Value)))
                throw Invalid("La estructura del catálogo no es compatible.");
        }

        static void TextOnly(XElement element)
        {
            if (element.HasElements) throw Invalid("Un campo del catálogo contiene elementos no compatibles.");
        }

        static void BooleanAttribute(XElement element, string name)
        {
            string value = (string)element.Attribute(name);
            if (value != null && value != "true" && value != "false") throw Invalid("Un indicador del catálogo no es válido.");
        }

        static InvalidDataException Invalid(string message) { return new InvalidDataException(message); }

        static void CheckSize(string value)
        {
            if (new UTF8Encoding(false, true).GetByteCount(value) > MaxBytes)
                throw Invalid("La biblioteca supera los 10 MB permitidos para sincronizar. No se reemplazaron los datos.");
        }

        internal static string Write(XNode value)
        {
            var text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings {
                OmitXmlDeclaration = true, Indent = false, NewLineHandling = NewLineHandling.Entitize
            })) value.WriteTo(writer);
            return text.ToString();
        }

        internal static XElement EntryXml(Entry entry, bool includeUsage)
        {
            return new XElement("record", new XAttribute("id", entry.Id), new XAttribute("favorite", entry.Favorite),
                entry.Archived ? new XAttribute("archived", true) : null,
                new XAttribute("hotkey", entry.Hotkey), new XAttribute("lastUsed", includeUsage ? entry.LastUsed : ""),
                entry.Cells.Where(c => c.Value.Length != 0).OrderBy(c => c.Key, StringComparer.Ordinal)
                    .Select(c => new XElement("cell", new XAttribute("key", c.Key), c.Value)),
                new XElement("variables", entry.Variables.Select(v => v.Xml())));
        }

        internal static Entry Copy(Entry entry) { return entry == null ? null : Entry.Read(EntryXml(entry, true)); }
        internal static string Content(Entry entry) { return entry == null ? null : Write(EntryXml(entry, false)); }
        internal static string Variables(Entry entry) { return Write(EntryXml(entry, false).Element("variables")); }
    }

    public sealed class CloudMergeResult
    {
        public string Xml;
        public List<string> Conflicts = new List<string>();
        public Dictionary<string, string> LocalConflictIds = new Dictionary<string, string>(StringComparer.Ordinal);
    }

    public static class CloudMerge
    {
        const string ConflictSuffix = " · versión de este equipo";

        public static CloudMergeResult Merge(string baselineXml, string localXml, string remoteXml)
        {
            var local = CloudCatalog.Read(localXml);
            var baseline = baselineXml == null ? null : CloudCatalog.Read(baselineXml);
            var remote = remoteXml == null ? null : CloudCatalog.Read(remoteXml);
            var result = new CloudMergeResult();
            // No cloud library exists yet. Retain the local library exactly in
            // semantic terms; an absent cloud row is not a request to delete it.
            if (remote == null) { result.Xml = local.Text(); return result; }
            var merged = new CloudCatalog.Catalog();
            var bEntries = Entries(baseline); var lEntries = Entries(local); var rEntries = Entries(remote);
            var winners = new Dictionary<string, Entry>(StringComparer.Ordinal);
            var copies = new List<Entry>();
            foreach (string id in Keys(bEntries, lEntries, rEntries))
            {
                Entry b = Get(bEntries, id), l = Get(lEntries, id), r = Get(rEntries, id);
                Entry chosen = null;
                if (b == null)
                {
                    if (l == null || r == null) chosen = CloudCatalog.Copy(l ?? r);
                    else if (Same(l, r)) chosen = WithUsage(l, l, r);
                    else if (SameTemplate(l, r))
                    {
                        // First-link metadata has no known ancestor. A favorite
                        // or a released duplicate hotkey must not duplicate an
                        // otherwise identical prompt on a retried upload.
                        chosen = WithUsage(r, l, r); chosen.Favorite = l.Favorite || r.Favorite;
                        chosen.Archived = l.Archived || r.Archived;
                        chosen.Hotkey = r.Hotkey.Length > 0 ? r.Hotkey : l.Hotkey;
                        if (l.Hotkey.Length > 0 && r.Hotkey.Length > 0 && l.Hotkey != r.Hotkey)
                            result.Conflicts.Add("El atajo de «" + chosen.Get("name") + "» difería entre equipos; se conservó el de la cuenta.");
                    }
                    else { chosen = CloudCatalog.Copy(r); copies.Add(l); }
                }
                else if (l == null || r == null)
                {
                    Entry remaining = l ?? r;
                    if (remaining != null && !Same(remaining, b))
                    {
                        chosen = CloudCatalog.Copy(remaining);
                        result.Conflicts.Add("Se conservó «" + remaining.Get("name") + "»: se editó en un equipo y se eliminó en el otro.");
                    }
                }
                else
                {
                    bool conflict;
                    chosen = MergeEntry(b, l, r, out conflict);
                    if (conflict) { chosen = CloudCatalog.Copy(r); copies.Add(l); }
                }
                if (chosen != null) winners.Add(id, chosen);
            }
            var reservedIds = new HashSet<string>(Keys(bEntries, lEntries, rEntries), StringComparer.Ordinal);
            foreach (Entry localVersion in copies)
            {
                Entry copy = CloudCatalog.Copy(localVersion);
                copy.Set("name", copy.Get("name") + ConflictSuffix); copy.Hotkey = "";
                string fingerprint = localVersion.Id + "\n" + CloudCatalog.Content(localVersion);
                int collision = 0;
                while (true)
                {
                    copy.Id = ConflictId(fingerprint, collision++);
                    Entry existing;
                    if (winners.TryGetValue(copy.Id, out existing))
                    {
                        if (Same(copy, existing)) { copy.LastUsed = Latest(copy.LastUsed, existing.LastUsed); break; }
                    }
                    else if (!reservedIds.Contains(copy.Id)) break;
                }
                winners[copy.Id] = copy;
                result.LocalConflictIds[localVersion.Id] = copy.Id;
                result.Conflicts.Add("Se conservaron ambas versiones de «" + localVersion.Get("name") + "». La de este equipo quedó como una copia.");
            }
            merged.Entries = winners.Values.OrderBy(e => e.Id, StringComparer.Ordinal).ToList();
            MergeFields(baseline, local, remote, merged, result);
            merged.Categories = MergeSet(List(baseline, c => c.Categories), local.Categories, remote.Categories, baseline == null);
            merged.Models = MergeSet(List(baseline, c => c.Models), local.Models, remote.Models, baseline == null);
            merged.DisabledChoices = MergeSet(List(baseline, c => c.DisabledChoices), local.DisabledChoices, remote.DisabledChoices, baseline == null);
            var columnMembers = MergeSet(List(baseline, c => c.ColumnOrder), local.ColumnOrder, remote.ColumnOrder, baseline == null);
            var allFieldIds = new HashSet<string>(local.Fields.Concat(remote.Fields).Concat(baseline == null ? new List<Field>() : baseline.Fields).Select(f => f.Id));
            var keptFieldIds = new HashSet<string>(merged.Fields.Select(f => f.Id));
            columnMembers.RemoveAll(id => allFieldIds.Contains(id) && !keptFieldIds.Contains(id));
            merged.ColumnOrder = MergeOrder(List(baseline, c => c.ColumnOrder), local.ColumnOrder, remote.ColumnOrder,
                columnMembers, baseline == null, "columnas", result);
            var categoryMembers = MergeSet(List(baseline, c => c.CategoryOrder), local.CategoryOrder, remote.CategoryOrder, baseline == null);
            merged.CategoryOrder = MergeOrder(List(baseline, c => c.CategoryOrder), local.CategoryOrder, remote.CategoryOrder,
                categoryMembers, baseline == null, "categorías", result);
            ResolveHotkeys(merged, lEntries, rEntries, result);
            // A delete/edit rescue can lose its only changed attribute when a
            // colliding hotkey is released above. Keep the rescue visibly marked
            // so a lost-response retry cannot mistake it for the deleted base.
            foreach (Entry entry in merged.Entries)
            {
                Entry previous = Get(bEntries, entry.Id);
                if (previous != null && (lEntries.ContainsKey(entry.Id) != rEntries.ContainsKey(entry.Id)) && Same(entry, previous))
                    entry.Set("name", entry.Get("name") + " · recuperado tras conflicto");
            }
            result.Xml = merged.Text();
            CloudCatalog.Validate(result.Xml);
            return result;
        }

        static Entry MergeEntry(Entry b, Entry l, Entry r, out bool conflict)
        {
            conflict = false;
            var result = new Entry { Id = l.Id, LastUsed = Latest(l.LastUsed, r.LastUsed) };
            result.Favorite = Pick(b.Favorite, l.Favorite, r.Favorite, ref conflict);
            result.Archived = Pick(b.Archived, l.Archived, r.Archived, ref conflict);
            result.Hotkey = Pick(b.Hotkey, l.Hotkey, r.Hotkey, ref conflict);
            foreach (string key in b.Cells.Keys.Concat(l.Cells.Keys).Concat(r.Cells.Keys).Distinct(StringComparer.Ordinal))
            {
                string value = Pick(b.Get(key), l.Get(key), r.Get(key), ref conflict);
                if (value.Length > 0) result.Set(key, value);
            }
            string variables = Pick(CloudCatalog.Variables(b), CloudCatalog.Variables(l), CloudCatalog.Variables(r), ref conflict);
            var shell = new XElement("record", new XAttribute("id", result.Id), XElement.Parse(variables, LoadOptions.PreserveWhitespace));
            result.Variables = Entry.Read(shell).Variables;
            return result;
        }

        static T Pick<T>(T baseline, T local, T remote, ref bool conflict)
        {
            var comparer = EqualityComparer<T>.Default;
            if (comparer.Equals(local, baseline)) return remote;
            if (comparer.Equals(remote, baseline) || comparer.Equals(local, remote)) return local;
            conflict = true; return remote;
        }

        static void MergeFields(CloudCatalog.Catalog baseline, CloudCatalog.Catalog local, CloudCatalog.Catalog remote,
            CloudCatalog.Catalog merged, CloudMergeResult result)
        {
            var bFields = Fields(baseline); var lFields = Fields(local); var rFields = Fields(remote);
            var chosen = new Dictionary<string, Field>(StringComparer.Ordinal);
            foreach (string id in Keys(bFields, lFields, rFields))
            {
                Field b = Get(bFields, id), l = Get(lFields, id), r = Get(rFields, id), winner;
                if (b == null) winner = r ?? l;
                else if (SameField(l, b)) winner = r;
                else if (SameField(r, b) || SameField(l, r)) winner = l;
                else winner = r ?? l;
                if (l != null && r != null && !SameField(l, r))
                {
                    bool conflict = false;
                    string title = b == null ? r.Title : Pick(b.Title, l.Title, r.Title, ref conflict);
                    bool visible = b == null ? l.Visible || r.Visible : Pick(b.Visible, l.Visible, r.Visible, ref conflict);
                    winner = new Field { Id = id, Title = title, Visible = visible };
                    if (b == null || conflict) result.Conflicts.Add("La columna «" + title + "» tenía ajustes distintos; se conservó el nombre de la cuenta.");
                }
                if (winner != null) chosen[id] = CopyField(winner);
            }
            // A simultaneous delete/edit must never hide data preserved by a
            // conflict copy. Restore its column definition when still referenced.
            foreach (string id in merged.Entries.SelectMany(e => e.Cells.Keys).Concat(CloudCatalog.RequiredFields).Distinct())
            {
                if (chosen.ContainsKey(id)) continue;
                Field source = Get(rFields, id) ?? Get(lFields, id) ?? Get(bFields, id);
                chosen[id] = source == null ? new Field { Id = id, Title = id } : CopyField(source);
                result.Conflicts.Add("Se conservó la columna «" + chosen[id].Title + "» porque todavía contiene datos.");
            }
            var order = MergeOrder(baseline == null ? new List<string>() : baseline.Fields.Select(f => f.Id).ToList(),
                local.Fields.Select(f => f.Id).ToList(), remote.Fields.Select(f => f.Id).ToList(), chosen.Keys.ToList(), baseline == null, "campos", result);
            merged.Fields = order.Select(id => chosen[id]).ToList();
        }

        static List<string> MergeSet(List<string> baseline, List<string> local, List<string> remote, bool firstLink)
        {
            var b = new HashSet<string>(baseline, StringComparer.Ordinal);
            var l = new HashSet<string>(local, StringComparer.Ordinal);
            var r = new HashSet<string>(remote, StringComparer.Ordinal);
            var result = new List<string>();
            foreach (string item in b.Concat(l).Concat(r).Distinct(StringComparer.Ordinal).OrderBy(v => v, StringComparer.Ordinal))
            {
                bool keep = firstLink ? l.Contains(item) || r.Contains(item)
                    : l.Contains(item) == b.Contains(item) ? r.Contains(item) : l.Contains(item);
                if (keep) result.Add(item);
            }
            return result;
        }

        static List<string> MergeOrder(List<string> baseline, List<string> local, List<string> remote,
            ICollection<string> included, bool firstLink, string label, CloudMergeResult result)
        {
            var allowed = new HashSet<string>(included, StringComparer.Ordinal);
            var common = new HashSet<string>(baseline.Intersect(local).Intersect(remote).Where(allowed.Contains), StringComparer.Ordinal);
            var b = baseline.Where(common.Contains).ToList();
            var l = local.Where(common.Contains).ToList();
            var r = remote.Where(common.Contains).ToList();
            bool localChanged = !firstLink && !b.SequenceEqual(l);
            bool remoteChanged = !firstLink && !b.SequenceEqual(r);
            if (localChanged && remoteChanged && !l.SequenceEqual(r))
                result.Conflicts.Add("El orden de " + label + " cambió en ambos equipos; se conservó el de la cuenta.");
            List<string> preferred = localChanged && !remoteChanged ? local : remote;
            List<string> secondary = Object.ReferenceEquals(preferred, local) ? remote : local;
            var order = preferred.Where(allowed.Contains).Distinct(StringComparer.Ordinal).ToList();
            for (int i = 0; i < secondary.Count; i++)
            {
                string item = secondary[i];
                if (!allowed.Contains(item) || order.Contains(item)) continue;
                int insert = -1;
                for (int before = i - 1; before >= 0; before--)
                {
                    int index = order.IndexOf(secondary[before]);
                    if (index >= 0) { insert = index + 1; break; }
                }
                if (insert < 0)
                    for (int after = i + 1; after < secondary.Count; after++)
                    {
                        int index = order.IndexOf(secondary[after]);
                        if (index >= 0) { insert = index; break; }
                    }
                order.Insert(insert < 0 ? order.Count : insert, item);
            }
            order.AddRange(allowed.Where(id => !order.Contains(id)).OrderBy(id => id, StringComparer.Ordinal));
            return order;
        }

        static void ResolveHotkeys(CloudCatalog.Catalog catalog, Dictionary<string, Entry> local, Dictionary<string, Entry> remote, CloudMergeResult result)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Entry entry in catalog.Entries.OrderBy(e => RemoteHotkeyMatches(e, remote) ? 0 : 1).ThenBy(e => e.Id, StringComparer.Ordinal))
            {
                if (String.IsNullOrWhiteSpace(entry.Hotkey)) continue;
                string key = HotkeyKey(entry.Hotkey);
                if (!used.Add(key))
                {
                    Entry alternative;
                    if (local.TryGetValue(entry.Id, out alternative) && !String.IsNullOrWhiteSpace(alternative.Hotkey) && used.Add(HotkeyKey(alternative.Hotkey)))
                    {
                        entry.Hotkey = alternative.Hotkey;
                        result.Conflicts.Add("El atajo de «" + entry.Get("name") + "» estaba ocupado en la cuenta; se conservó el atajo disponible de este equipo.");
                    }
                    else
                    {
                        entry.Hotkey = "";
                        result.Conflicts.Add("El atajo de «" + entry.Get("name") + "» estaba ocupado por otro prompt; se conservó el texto y se liberó ese atajo.");
                    }
                }
            }
        }

        static bool RemoteHotkeyMatches(Entry entry, Dictionary<string, Entry> remote)
        {
            Entry previous;
            return remote.TryGetValue(entry.Id, out previous) && !String.IsNullOrWhiteSpace(previous.Hotkey) &&
                String.Equals(HotkeyKey(previous.Hotkey), HotkeyKey(entry.Hotkey), StringComparison.OrdinalIgnoreCase);
        }

        static string HotkeyKey(string hotkey)
        {
            var parts = hotkey.Split('+').Select(p => p.Trim()).ToArray();
            return String.Join("+", parts.Take(parts.Length - 1).OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToArray()) + "+" + parts.Last();
        }

        static string ConflictId(string value, int collision)
        {
            using (var hash = SHA256.Create())
            {
                byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes("PromptDesk-sync-conflict-v1\n" + value + "\n" + collision));
                byte[] id = new byte[16]; Array.Copy(bytes, id, 16);
                return new Guid(id).ToString();
            }
        }

        static bool Same(Entry left, Entry right) { return CloudCatalog.Content(left) == CloudCatalog.Content(right); }
        static bool SameTemplate(Entry left, Entry right)
        {
            XElement l = CloudCatalog.EntryXml(left, false), r = CloudCatalog.EntryXml(right, false);
            l.Attribute("hotkey").Remove(); r.Attribute("hotkey").Remove();
            l.Attribute("favorite").Remove(); r.Attribute("favorite").Remove();
            l.SetAttributeValue("archived", null); r.SetAttributeValue("archived", null);
            return CloudCatalog.Write(l) == CloudCatalog.Write(r);
        }
        static Entry WithUsage(Entry source, Entry left, Entry right) { var copy = CloudCatalog.Copy(source); copy.LastUsed = Latest(left.LastUsed, right.LastUsed); return copy; }
        static string Latest(string left, string right) { return StringComparer.Ordinal.Compare(left, right) >= 0 ? left : right; }
        static bool SameField(Field left, Field right) { return left == null || right == null ? left == right : left.Id == right.Id && left.Title == right.Title && left.Visible == right.Visible; }
        static Field CopyField(Field field) { return new Field { Id = field.Id, Title = field.Title, Visible = field.Visible }; }
        static Dictionary<string, Entry> Entries(CloudCatalog.Catalog catalog) { return catalog == null ? new Dictionary<string, Entry>(StringComparer.Ordinal) : catalog.Entries.ToDictionary(e => e.Id, StringComparer.Ordinal); }
        static Dictionary<string, Field> Fields(CloudCatalog.Catalog catalog) { return catalog == null ? new Dictionary<string, Field>(StringComparer.Ordinal) : catalog.Fields.ToDictionary(f => f.Id, StringComparer.Ordinal); }
        static T Get<T>(Dictionary<string, T> items, string id) where T : class { T value; return items.TryGetValue(id, out value) ? value : null; }
        static IEnumerable<string> Keys<T>(params Dictionary<string, T>[] dictionaries) { return dictionaries.SelectMany(d => d.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal); }
        static List<string> List(CloudCatalog.Catalog catalog, Func<CloudCatalog.Catalog, List<string>> read) { return catalog == null ? new List<string>() : read(catalog); }
    }
}
