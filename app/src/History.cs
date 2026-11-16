using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PromptDesk
{
    public class UndoHistory
    {
        // States share unchanged record strings. Editing one prompt does not
        // serialize or duplicate every other prompt in the library.
        sealed class State
        {
            public string Metadata;
            public string[] Order;
            public Dictionary<string, string> Records;

            public State CopyForEditing()
            {
                return new State
                {
                    Metadata = Metadata,
                    Order = Order,
                    Records = new Dictionary<string, string>(Records)
                };
            }

            public bool SameAs(State other)
            {
                return Metadata == other.Metadata && Order.SequenceEqual(other.Order) &&
                    Records.All(pair => other.Records.ContainsKey(pair.Key) && other.Records[pair.Key] == pair.Value);
            }
        }

        readonly List<State> past = new List<State>();
        readonly List<State> future = new List<State>();
        State current;
        string group = "";
        DateTime lastChange = DateTime.MinValue;

        public UndoHistory(Store database)
        {
            current = Capture(database, null);
        }

        static string RecordText(Entry entry)
        {
            XElement record = entry.Xml();
            record.SetAttributeValue("lastUsed", "");
            return Serialize(record);
        }

        static string Serialize(XNode node)
        {
            var text = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(text, new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                NewLineHandling = NewLineHandling.Entitize
            }))
                node.WriteTo(writer);
            return text.ToString();
        }

        static State Capture(Store database, State previous)
        {
            var state = new State
            {
                Metadata = Serialize(database.Document(false)),
                Order = database.Entries.Select(entry => entry.Id).ToArray(),
                Records = new Dictionary<string, string>()
            };
            foreach (Entry entry in database.Entries)
            {
                string text = RecordText(entry), old;
                if (previous != null && previous.Records.TryGetValue(entry.Id, out old) && old == text)
                    text = old;
                state.Records.Add(entry.Id, text);
            }
            return state;
        }

        bool MergeWithPrevious(string key)
        {
            return key.Length > 0 && key == group && (DateTime.UtcNow - lastChange).TotalMilliseconds < 1000;
        }

        void RememberCurrent()
        {
            past.Add(current);
            if (past.Count > 150) past.RemoveAt(0);
        }

        public void Record(Store database, string key)
        {
            key = key ?? "";
            bool merge = MergeWithPrevious(key);
            // These keys are reserved for editor text/name changes only.
            int separator = key.IndexOf(':');
            string field = separator < 0 ? "" : key.Substring(0, separator);
            string id = separator < 0 ? "" : key.Substring(separator + 1);
            Entry edited = (field == "prompt" || field == "name")
                ? database.Entries.FirstOrDefault(entry => entry.Id == id) : null;
            if (edited != null && current.Records.ContainsKey(id))
            {
                string text = RecordText(edited);
                if (text == current.Records[id]) return;
                if (!merge)
                {
                    RememberCurrent();
                    current = current.CopyForEditing();
                }
                current.Records[id] = text;
            }
            else
            {
                State next = Capture(database, current);
                if (next.SameAs(current)) return;
                if (!merge) RememberCurrent();
                current = next;
            }
            group = key;
            lastChange = DateTime.UtcNow;
            future.Clear();
        }

        public void UpdateBaseline(Store database)
        {
            current = Capture(database, current);
            group = "";
        }

        public bool CanUndo { get { return past.Count > 0; } }
        public bool CanRedo { get { return future.Count > 0; } }

        public bool Undo(Store database)
        {
            if (!CanUndo) return false;
            State target = past[past.Count - 1];
            Apply(database, target);
            future.Add(current);
            current = target;
            past.RemoveAt(past.Count - 1);
            group = "";
            return true;
        }

        public bool Redo(Store database)
        {
            if (!CanRedo) return false;
            State target = future[future.Count - 1];
            Apply(database, target);
            RememberCurrent();
            current = target;
            future.RemoveAt(future.Count - 1);
            group = "";
            return true;
        }

        static void Apply(Store database, State state)
        {
            var usage = database.Entries.ToDictionary(entry => entry.Id, entry => entry.LastUsed);
            XDocument document = XDocument.Parse(state.Metadata, LoadOptions.PreserveWhitespace);
            XElement records = document.Root.Element("records");
            foreach (string id in state.Order)
                records.Add(XElement.Parse(state.Records[id], LoadOptions.PreserveWhitespace));
            database.ReadDocument(document);
            foreach (Entry entry in database.Entries)
                if (usage.ContainsKey(entry.Id)) entry.LastUsed = usage[entry.Id];
        }
    }
}
