using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PromptDesk
{
    public class Field
    {
        public string Id, Title;
        public bool Visible = true;
    }

    public class VariableOption
    {
        public string Label = "", Value = "";
    }

    public class Variable
    {
        public string Token = "", Label = "", Value = "";
        public List<VariableOption> Options = new List<VariableOption>();

        internal XElement Xml()
        {
            return new XElement("var", new XAttribute("token", Token), new XAttribute("label", Label),
                new XAttribute("value", Value), Options.Select(option => new XElement("option",
                    new XAttribute("label", option.Label), new XAttribute("value", option.Value))));
        }

        internal static Variable Read(XElement element)
        {
            return new Variable
            {
                Token = (string)element.Attribute("token") ?? "",
                Label = (string)element.Attribute("label") ?? "",
                Value = (string)element.Attribute("value") ?? "",
                Options = element.Elements("option").Select(option => new VariableOption
                {
                    Label = (string)option.Attribute("label") ?? "",
                    Value = (string)option.Attribute("value") ?? ""
                }).ToList()
            };
        }
    }

    public class Entry
    {
        public string Id = Guid.NewGuid().ToString(), Hotkey = "", LastUsed = "";
        public bool Favorite, Archived;
        public Dictionary<string, string> Cells = new Dictionary<string, string>();
        public List<Variable> Variables = new List<Variable>();

        public string Get(string key)
        {
            string value;
            return Cells.TryGetValue(key, out value) ? value : "";
        }

        public void Set(string key, string value)
        {
            Cells[key] = value ?? "";
        }

        public XElement Xml()
        {
            return new XElement("record",
                new XAttribute("id", Id), new XAttribute("favorite", Favorite),
                Archived ? new XAttribute("archived", true) : null,
                new XAttribute("hotkey", Hotkey), new XAttribute("lastUsed", LastUsed),
                Cells.Select(cell => new XElement("cell", new XAttribute("key", cell.Key), cell.Value)),
                new XElement("variables", Variables.Select(variable => variable.Xml())));
        }

        public static Entry Read(XElement element)
        {
            var entry = new Entry
            {
                Id = (string)element.Attribute("id") ?? Guid.NewGuid().ToString(),
                Favorite = (string)element.Attribute("favorite") == "true",
                Archived = (string)element.Attribute("archived") == "true",
                Hotkey = (string)element.Attribute("hotkey") ?? "",
                LastUsed = (string)element.Attribute("lastUsed") ?? ""
            };
            if (String.IsNullOrWhiteSpace(entry.Id))
                throw new Exception("Un prompt tiene un identificador vacío.");
            foreach (XElement cell in element.Elements("cell"))
            {
                string key = (string)cell.Attribute("key");
                if (String.IsNullOrEmpty(key) || entry.Cells.ContainsKey(key))
                    throw new Exception("Un prompt contiene campos inválidos o repetidos.");
                entry.Set(key, cell.Value);
            }
            XElement variables = element.Element("variables");
            if (variables != null)
                entry.Variables = variables.Elements("var").Select(Variable.Read).ToList();
            return entry;
        }

        public Entry Clone()
        {
            Entry clone = Read(Xml());
            clone.Id = Guid.NewGuid().ToString();
            clone.Hotkey = "";
            clone.LastUsed = "";
            clone.Archived = false;
            return clone;
        }
    }

    public class Store
    {
        public List<Field> Fields = new List<Field>();
        public List<Entry> Entries = new List<Entry>();
        public List<string> Categories = new List<string>();
        public List<string> Models = new List<string>();
        public List<string> ColumnOrder = new List<string>();
        public List<string> CategoryOrder = new List<string>();
        public List<string> DisabledChoices = new List<string>();
        public string Folder, Pathname;

        public Store(string folder)
        {
            Folder = folder;
            Pathname = Path.Combine(folder, "datos.xml");
            Directory.CreateDirectory(folder);
            Directory.CreateDirectory(Path.Combine(folder, "Historial"));
        }

        public void Load(string file)
        {
            using (XmlReader reader = XmlReader.Create(file, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            }))
                ReadDocument(XDocument.Load(reader, LoadOptions.PreserveWhitespace));
        }

        static List<string> ReadList(XElement root, string container, string item)
        {
            XElement element = root.Element(container);
            return element == null ? new List<string>()
                : element.Elements(item).Select(value => value.Value).Distinct().ToList();
        }

        public void ReadDocument(XDocument document)
        {
            XElement root = document.Root;
            if (root == null || root.Name != "promptdesk" || (string)root.Attribute("version") != "1")
                throw new Exception("El archivo no es un respaldo compatible de PromptDesk.");
            XElement columns = root.Element("columns"), records = root.Element("records");
            if (columns == null || records == null)
                throw new Exception("Respaldo incompleto.");
            var fields = columns.Elements("col").Select(column => new Field
            {
                Id = (string)column.Attribute("id"),
                Title = (string)column.Attribute("title"),
                Visible = (string)column.Attribute("visible") != "false"
            }).ToList();
            if (fields.Any(field => String.IsNullOrEmpty(field.Id) || String.IsNullOrEmpty(field.Title)) ||
                fields.Select(field => field.Id).Distinct().Count() != fields.Count)
                throw new Exception("Columnas inválidas.");
            foreach (string required in new[] { "collection", "category", "name", "description", "model", "prompt", "note" })
                if (!fields.Any(field => field.Id == required))
                    throw new Exception("Falta la columna " + required);
            var entries = records.Elements("record").Select(Entry.Read).ToList();
            if (entries.Select(entry => entry.Id).Distinct().Count() != entries.Count)
                throw new Exception("Identificadores repetidos.");

            // Parse the entire candidate before replacing any in-memory collection.
            var categories = ReadList(root, "categories", "category");
            var models = ReadList(root, "models", "model");
            var columnOrder = ReadList(root, "columnOrder", "column");
            var categoryOrder = ReadList(root, "categoryOrder", "category");
            var disabledChoices = ReadList(root, "disabledChoices", "choice");
            Fields = fields;
            Entries = entries;
            Categories = categories;
            Models = models;
            ColumnOrder = columnOrder;
            CategoryOrder = categoryOrder;
            DisabledChoices = disabledChoices;
        }

        public XDocument Document()
        {
            return Document(true);
        }

        internal XDocument Document(bool includeEntries)
        {
            return new XDocument(new XElement("promptdesk", new XAttribute("version", "1"),
                new XElement("columns", Fields.Select(field => new XElement("col",
                    new XAttribute("id", field.Id), new XAttribute("title", field.Title),
                    new XAttribute("visible", field.Visible)))),
                new XElement("categories", Categories.Select(value => new XElement("category", value))),
                new XElement("models", Models.Select(value => new XElement("model", value))),
                new XElement("columnOrder", ColumnOrder.Select(value => new XElement("column", value))),
                new XElement("categoryOrder", CategoryOrder.Select(value => new XElement("category", value))),
                new XElement("disabledChoices", DisabledChoices.Select(value => new XElement("choice", value))),
                new XElement("records", includeEntries ? Entries.Select(entry => entry.Xml()) : null)));
        }

        static void WriteDocumentAtomically(string destination, XDocument document, string backup)
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
                    {
                        Encoding = new UTF8Encoding(false),
                        Indent = true,
                        NewLineHandling = NewLineHandling.Entitize,
                        CloseOutput = false
                    }))
                        document.Save(writer);
                    stream.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, backup);
                else File.Move(temporary, destination);
            }
            finally
            {
                // Failed serialization or replacement never publishes a partial catalog.
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        public void Export(string path)
        {
            if (String.Equals(Path.GetFullPath(path), Path.GetFullPath(Pathname), StringComparison.OrdinalIgnoreCase))
                Save();
            else
                WriteDocumentAtomically(path, Document(), null);
        }

        public void Save()
        {
            string historyFolder = Path.Combine(Folder, "Historial");
            string backup = Path.Combine(historyFolder,
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff") + "-" + Guid.NewGuid().ToString("N") + ".xml");
            WriteDocumentAtomically(Pathname, Document(), backup);
            // Retention is best effort after a completed atomic save.
            try
            {
                foreach (FileInfo file in new DirectoryInfo(historyFolder).GetFiles("*.xml")
                    .OrderByDescending(file => file.Name).Skip(200))
                    try { file.Delete(); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    internal static class PromptText
    {
        // The Windows multiline editor and clipboard expect a CRLF for each actual line break.
        internal static string ForWindows(string text)
        {
            return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", "\r\n");
        }
    }

    public static class Templates
    {
        public static string Render(Entry entry)
        {
            string source = entry.Get("prompt");
            var variables = entry.Variables.Where(variable => variable.Token.Length > 0)
                .OrderByDescending(variable => variable.Token.Length).ToList();
            if (variables.Select(variable => variable.Token).Distinct().Count() != variables.Count)
                throw new Exception("Hay variables repetidas.");
            if (variables.Count == 0) return source;
            bool json = source.TrimStart().StartsWith("{");
            var replacements = variables.ToDictionary(variable => variable.Token,
                variable => json ? EscapeJson(variable.Value) : variable.Value);
            string pattern = String.Join("|", variables.Select(variable => Pattern(variable.Token)).ToArray());
            // One pass: replacement text is never interpreted as another variable.
            return Regex.Replace(source, pattern, match => replacements[match.Value]);
        }

        static string EscapeJson(string value)
        {
            var result = new StringBuilder(value.Length);
            foreach (char character in value)
            {
                switch (character)
                {
                    case '"': result.Append("\\\""); break;
                    case '\\': result.Append("\\\\"); break;
                    case '\b': result.Append("\\b"); break;
                    case '\f': result.Append("\\f"); break;
                    case '\n': result.Append("\\n"); break;
                    case '\r': result.Append("\\r"); break;
                    case '\t': result.Append("\\t"); break;
                    default:
                        if (character < 0x20) result.Append("\\u" + ((int)character).ToString("x4"));
                        else result.Append(character);
                        break;
                }
            }
            return result.ToString();
        }

        public static string Pattern(string token)
        {
            if (String.IsNullOrEmpty(token)) throw new Exception("El marcador no puede estar vacío.");
            return (Regex.IsMatch(token.Substring(0, 1), @"\w") ? @"(?<!\w)" : "") +
                Regex.Escape(token) +
                (Regex.IsMatch(token.Substring(token.Length - 1), @"\w") ? @"(?!\w)" : "");
        }
    }
}
