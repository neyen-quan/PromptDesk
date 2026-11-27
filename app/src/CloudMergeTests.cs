using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PromptDesk
{
    internal static class CloudMergeTests
    {
        static string Document(params Entry[] entries)
        {
            return CloudCatalog.Write(new XDocument(new XElement("promptdesk", new XAttribute("version", "1"),
                new XElement("columns", CloudCatalog.RequiredFields.Select(id => new XElement("col",
                    new XAttribute("id", id), new XAttribute("title", id), new XAttribute("visible", true)))),
                new XElement("categories"), new XElement("models"), new XElement("columnOrder"),
                new XElement("categoryOrder"), new XElement("disabledChoices"),
                new XElement("records", entries.Select(e => e.Xml())))));
        }

        static Entry Prompt(string id, string text)
        {
            var entry = new Entry { Id = id };
            entry.Set("name", "Prompt " + id); entry.Set("collection", "Personales"); entry.Set("prompt", text);
            return entry;
        }

        static string Change(string xml, Action<CloudCatalog.Catalog> edit)
        {
            var catalog = CloudCatalog.Read(xml); edit(catalog); return catalog.Text();
        }

        static Entry Record(string xml, string id) { return CloudCatalog.Read(xml).Entries.Single(e => e.Id == id); }
        static bool Has(string xml, string id) { return CloudCatalog.Read(xml).Entries.Any(e => e.Id == id); }

        static bool Rejects(Action action)
        {
            try { action(); return false; }
            catch (InvalidDataException) { return true; }
            catch (XmlException) { return true; }
            catch (ArgumentException) { return true; }
            catch (Exception e) { return e.Message.Contains("campos inválidos"); }
        }

        static string EditScenario(string xml, Random random, string device)
        {
            return Change(xml, catalog => {
                foreach (Entry entry in catalog.Entries.ToArray())
                {
                    switch (random.Next(8))
                    {
                        case 0: catalog.Entries.Remove(entry); break;
                        case 1: entry.Set("prompt", "Edited " + device); break;
                        case 2: entry.Set("name", "Name " + device); break;
                        case 3: entry.Hotkey = "Ctrl+Alt+NumPad" + random.Next(2); break;
                        case 4: entry.Favorite = true; break;
                        case 5: entry.Set("description", "Description " + device); break;
                        case 6: entry.LastUsed = "2026-11-16T12:00:00Z"; break;
                    }
                }
                if (random.Next(2) == 1) catalog.Entries.Add(Prompt("new-" + device, "New " + device));
                if (random.Next(2) == 1) catalog.Models.Add("Model " + device);
            });
        }

        public static void Run(Action<bool, string> check, string folder)
        {
            string empty = Document();
            string pc = Document(Prompt("pc-1", "Texto de la PC"), Prompt("pc-2", "Otro texto"));
            string notebook = Document(Prompt("notebook-1", "Texto creado durante el viaje"));
            var uploaded = CloudMerge.Merge(null, notebook, null);
            check(CloudCatalog.Equivalent(uploaded.Xml, notebook), "Primera notebook conserva todo al crear la biblioteca en nube");
            var joined = CloudMerge.Merge(null, pc, notebook);
            check(CloudCatalog.Read(joined.Xml).Entries.Count == 3 && joined.Conflicts.Count == 0, "Primera vinculación PC une bibliotecas sin borrados ni conflictos artificiales");
            check(Record(joined.Xml, "pc-1").Get("prompt") == "Texto de la PC" && Has(joined.Xml, "notebook-1"), "PC y notebook conservan sus prompts durante vinculación inicial");
            var emptyDevice = CloudMerge.Merge(null, empty, joined.Xml);
            check(CloudCatalog.Equivalent(emptyDevice.Xml, joined.Xml), "Equipo vacío recibe la biblioteca completa sin reemplazarla");
            var oldPc = CloudMerge.Merge(pc, pc, joined.Xml);
            check(CloudCatalog.Equivalent(oldPc.Xml, joined.Xml), "PC sin cambios recibe altas remotas sin interpretarlas como borrados locales");

            string baseline = Document(Prompt("a", "Original A"), Prompt("b", "Original B"));
            string local = Change(baseline, c => c.Entries.Single(e => e.Id == "a").Set("prompt", "Local A"));
            string remote = Change(baseline, c => c.Entries.Single(e => e.Id == "b").Set("prompt", "Remoto B"));
            var separate = CloudMerge.Merge(baseline, local, remote);
            check(Record(separate.Xml, "a").Get("prompt") == "Local A" && Record(separate.Xml, "b").Get("prompt") == "Remoto B", "Cambios simultáneos en prompts distintos se combinan");
            check(separate.Conflicts.Count == 0 && CloudCatalog.Read(separate.Xml).Entries.Count == 2, "Cambios independientes no crean copias");
            string renamed = Change(baseline, c => c.Entries[0].Set("name", "Nuevo título"));
            string edited = Change(baseline, c => c.Entries[0].Set("prompt", "Texto remoto"));
            string archived = Change(baseline, c => c.Entries[0].Archived = true);
            var archiveAndEdit = CloudMerge.Merge(baseline, archived, edited);
            Entry archivedEntry = Record(archiveAndEdit.Xml, "a");
            check(archivedEntry.Archived && archivedEntry.Get("prompt") == "Texto remoto" && archiveAndEdit.Conflicts.Count == 0,
                "Archivar y editar desde equipos distintos conserva ambos cambios");
            var restored = CloudMerge.Merge(archived, baseline, archiveAndEdit.Xml);
            check(!Record(restored.Xml, "a").Archived && Record(restored.Xml, "a").Get("prompt") == "Texto remoto",
                "Restaurar un prompt se propaga sin perder cambios del otro equipo");
            check(CloudCatalog.Equivalent(CloudMerge.Merge(null, archived, baseline).Xml, archived) &&
                CloudCatalog.Equivalent(CloudMerge.Merge(null, baseline, archived).Xml, archived),
                "Una primera vinculación mantiene el archivado en ambos sentidos sin duplicar el prompt");
            check(Rejects(() => CloudCatalog.Validate(archived.Replace("archived=\"true\"", "archived=\"error\""))),
                "El cliente de nube detecta un estado de archivo inválido");
            var disjoint = CloudMerge.Merge(baseline, renamed, edited);
            check(Record(disjoint.Xml, "a").Get("name") == "Nuevo título" && Record(disjoint.Xml, "a").Get("prompt") == "Texto remoto" && disjoint.Conflicts.Count == 0,
                "Cambios en campos independientes del mismo prompt se combinan");
            check(CloudMerge.Merge(baseline, local, local).Conflicts.Count == 0, "Ediciones iguales no generan conflicto");

            string deleteA = Change(baseline, c => c.Entries.RemoveAll(e => e.Id == "a"));
            check(!Has(CloudMerge.Merge(baseline, deleteA, baseline).Xml, "a"), "Borrado local se propaga si la cuenta no cambió ese prompt");
            check(!Has(CloudMerge.Merge(baseline, baseline, deleteA).Xml, "a"), "Borrado remoto se propaga si el equipo no cambió ese prompt");
            check(!Has(CloudMerge.Merge(baseline, deleteA, deleteA).Xml, "a"), "Borrado en ambos equipos no resucita un prompt");
            var keepLocalEdit = CloudMerge.Merge(baseline, local, deleteA);
            check(Record(keepLocalEdit.Xml, "a").Get("prompt") == "Local A" && keepLocalEdit.Conflicts.Count > 0, "Eliminar remoto vs editar local conserva la edición y avisa");
            var keepRemoteEdit = CloudMerge.Merge(baseline, deleteA, edited);
            check(Record(keepRemoteEdit.Xml, "a").Get("prompt") == "Texto remoto" && keepRemoteEdit.Conflicts.Count > 0, "Eliminar local vs editar remoto conserva la edición y avisa");

            string used = Change(baseline, c => c.Entries[0].LastUsed = "2026-11-16T12:00:00.0000000Z");
            check(!Has(CloudMerge.Merge(baseline, deleteA, used).Xml, "a"), "Usar un prompt no impide un borrado explícito en el otro equipo");
            var usage = CloudMerge.Merge(baseline, local, used);
            check(Record(usage.Xml, "a").Get("prompt") == "Local A" && Record(usage.Xml, "a").LastUsed.EndsWith("Z") && usage.Conflicts.Count == 0,
                "Uso reciente y edición se combinan sin copiar el prompt");

            string remoteEditA = Change(baseline, c => c.Entries[0].Set("prompt", "Remoto A"));
            var conflict = CloudMerge.Merge(baseline, local, remoteEditA);
            string copyId = conflict.LocalConflictIds["a"];
            Guid parsed;
            check(Guid.TryParse(copyId, out parsed) && copyId != "a", "Conflicto crea una identidad distinta y estable");
            check(Record(conflict.Xml, "a").Get("prompt") == "Remoto A" && Record(conflict.Xml, copyId).Get("prompt") == "Local A", "Conflicto preserva las dos versiones completas");
            check(Record(conflict.Xml, copyId).Get("name").EndsWith(" · versión de este equipo") && conflict.Conflicts.Count > 0,
                "Copia de conflicto queda identificada y se avisa");
            var repeated = CloudMerge.Merge(baseline, local, conflict.Xml);
            check(repeated.LocalConflictIds["a"] == copyId && CloudCatalog.Equivalent(repeated.Xml, conflict.Xml), "Reintento tras respuesta perdida no duplica copias de conflicto");
            check(CloudMerge.Merge(conflict.Xml, conflict.Xml, conflict.Xml).Conflicts.Count == 0, "Conflicto resuelto no genera avisos en cada sincronización");
            var firstDivergence = CloudMerge.Merge(null, local, remoteEditA);
            check(CloudCatalog.Read(firstDivergence.Xml).Entries.Count == 3 && firstDivergence.LocalConflictIds.ContainsKey("a"), "Primera vinculación preserva variantes del mismo identificador");
            string changedOldCopy = Change(conflict.Xml, c => c.Entries.Single(e => e.Id == copyId).Set("prompt", "Copia editada después"));
            var copyCollision = CloudMerge.Merge(baseline, local, changedOldCopy);
            check(Record(copyCollision.Xml, copyId).Get("prompt") == "Copia editada después" &&
                Record(copyCollision.Xml, copyCollision.LocalConflictIds["a"]).Get("prompt") == "Local A", "Un ID determinista ocupado por una copia editada no se sobrescribe");
            check(CloudCatalog.Equivalent(CloudMerge.Merge(baseline, local, copyCollision.Xml).Xml, copyCollision.Xml), "Colisiones de copias también son idempotentes");

            string late = Change(local, c => c.Entries[0].Set("prompt", "Local A siguió escribiendo"));
            var lateMerge = CloudMerge.Merge(local, late, separate.Xml);
            check(Record(lateMerge.Xml, "a").Get("prompt") == "Local A siguió escribiendo" && Record(lateMerge.Xml, "b").Get("prompt") == "Remoto B", "Edición durante una petición de red no se pierde al aplicar el resultado");
            var lateConflict = CloudMerge.Merge(local, late, conflict.Xml);
            check(Record(lateConflict.Xml, lateConflict.LocalConflictIds["a"]).Get("prompt") == "Local A siguió escribiendo", "Edición tardía en conflicto redirige a su copia local sin perder texto");

            string rich = Change(baseline, c => {
                c.Fields.Add(new Field { Id = "custom_ref", Title = "Referencia", Visible = false });
                c.Entries[0].Set("custom_ref", "Detalle íntegro Ω");
                c.ColumnOrder.AddRange(new[] { "custom_ref", "name", "prompt" });
                c.Categories.AddRange(new[] { "Personales::Fotos", "404::Escenas" });
                c.Models.AddRange(new[] { "Modelo A", "Modelo B" });
                c.CategoryOrder.AddRange(new[] { "Personales::Fotos", "404::Escenas" });
                c.DisabledChoices.Add("model::Modelo A");
            });
            var richJoin = CloudCatalog.Read(CloudMerge.Merge(null, rich, notebook).Xml);
            check(richJoin.Fields.Any(f => f.Id == "custom_ref" && !f.Visible && f.Title == "Referencia") && richJoin.Entries.Single(e => e.Id == "a").Get("custom_ref") == "Detalle íntegro Ω",
                "Vinculación inicial conserva definición y valor de campos personalizados");
            check(richJoin.ColumnOrder.IndexOf("custom_ref") < richJoin.ColumnOrder.IndexOf("name") && richJoin.Categories.Contains("404::Escenas") && richJoin.Models.Contains("Modelo B"),
                "Vinculación inicial conserva orden, categorías y modelos");
            check(richJoin.DisabledChoices.Contains("model::Modelo A") && richJoin.CategoryOrder.SequenceEqual(new[] { "Personales::Fotos", "404::Escenas" }),
                "Vinculación inicial conserva opciones ocultas y orden de categorías");
            string localMeta = Change(rich, c => { c.Models.Remove("Modelo A"); c.Models.Add("Modelo C"); c.DisabledChoices.Clear(); });
            string remoteMeta = Change(rich, c => { c.Models.Add("Modelo D"); c.Categories.Add("Personales::Video"); });
            var mergedMeta = CloudCatalog.Read(CloudMerge.Merge(rich, localMeta, remoteMeta).Xml);
            check(!mergedMeta.Models.Contains("Modelo A") && mergedMeta.Models.Contains("Modelo C") && mergedMeta.Models.Contains("Modelo D"), "Altas y bajas independientes de modelos se combinan");
            check(mergedMeta.DisabledChoices.Count == 0 && mergedMeta.Categories.Contains("Personales::Video"), "Cambios de categorías y opciones ocultas sobreviven a la sincronización");

            string deleteField = Change(rich, c => { c.Fields.RemoveAll(f => f.Id == "custom_ref"); c.ColumnOrder.Remove("custom_ref"); foreach (Entry e in c.Entries) e.Cells.Remove("custom_ref"); });
            var fieldDeleted = CloudCatalog.Read(CloudMerge.Merge(rich, deleteField, rich).Xml);
            check(fieldDeleted.Fields.All(f => f.Id != "custom_ref") && !fieldDeleted.ColumnOrder.Contains("custom_ref"), "Borrar una columna sin cambios remotos se propaga con sus valores");
            string editField = Change(rich, c => c.Entries[0].Set("custom_ref", "Referencia remota actualizada"));
            var rescuedField = CloudCatalog.Read(CloudMerge.Merge(rich, deleteField, editField).Xml);
            check(rescuedField.Fields.Any(f => f.Id == "custom_ref") && rescuedField.Entries.Any(e => e.Get("custom_ref") == "Referencia remota actualizada"),
                "Borrar columna vs editar su valor conserva columna y datos remotos");
            string renameField = Change(rich, c => c.Fields.Single(f => f.Id == "custom_ref").Title = "Referencia renombrada");
            string showField = Change(rich, c => c.Fields.Single(f => f.Id == "custom_ref").Visible = true);
            var independentField = CloudCatalog.Read(CloudMerge.Merge(rich, renameField, showField).Xml).Fields.Single(f => f.Id == "custom_ref");
            check(independentField.Title == "Referencia renombrada" && independentField.Visible, "Renombrar y mostrar la misma columna se combinan");
            string orderLocal = Change(rich, c => c.ColumnOrder = new List<string> { "name", "custom_ref", "prompt" });
            string orderRemote = Change(rich, c => c.ColumnOrder = new List<string> { "custom_ref", "prompt", "name" });
            var orderConflict = CloudMerge.Merge(rich, orderLocal, orderRemote);
            check(CloudCatalog.Read(orderConflict.Xml).ColumnOrder.SequenceEqual(new[] { "custom_ref", "prompt", "name" }) && orderConflict.Conflicts.Count > 0,
                "Orden incompatible en ambos equipos se resuelve explícitamente con el de la cuenta");
            string addRemoteField = Change(rich, c => { c.Fields.Add(new Field { Id = "custom_new", Title = "Nueva" }); c.ColumnOrder.Add("custom_new"); });
            var orderWithAddition = CloudCatalog.Read(CloudMerge.Merge(rich, orderLocal, addRemoteField).Xml);
            check(orderWithAddition.ColumnOrder.Take(3).SequenceEqual(new[] { "name", "custom_ref", "prompt" }) && orderWithAddition.ColumnOrder.Contains("custom_new"),
                "Reordenar localmente no pierde una columna agregada remotamente");

            Entry keyedLocal = Prompt("key-local", "Local con atajo"), keyedRemote = Prompt("key-remote", "Remoto con atajo");
            keyedLocal.Hotkey = "Ctrl+Alt+NumPad1"; keyedRemote.Hotkey = "alt+ctrl+numpad1";
            var hotkeys = CloudMerge.Merge(null, Document(keyedLocal), Document(keyedRemote));
            check(Record(hotkeys.Xml, "key-remote").Hotkey.Length > 0 && Record(hotkeys.Xml, "key-local").Hotkey.Length == 0 && hotkeys.Conflicts.Count > 0,
                "Atajos globales equivalentes conservan asignación remota y avisan sin perder prompts");
            check(CloudCatalog.Equivalent(CloudMerge.Merge(null, Document(keyedLocal), hotkeys.Xml).Xml, hotkeys.Xml),
                "Reintentar una primera vinculación con atajos liberados no crea copias falsas");
            Entry alternateLocal = Prompt("key-alternate", "Alternativa"), alternateRemote = Prompt("key-alternate", "Alternativa");
            Entry firstOwner = Prompt("key-a", "Dueño del atajo");
            firstOwner.Hotkey = "Ctrl+Alt+NumPad0"; alternateRemote.Hotkey = firstOwner.Hotkey; alternateLocal.Hotkey = "Ctrl+Alt+NumPad2";
            var alternate = CloudMerge.Merge(null, Document(alternateLocal), Document(firstOwner, alternateRemote));
            check(Record(alternate.Xml, alternateLocal.Id).Hotkey == alternateLocal.Hotkey &&
                CloudCatalog.Equivalent(CloudMerge.Merge(null, Document(alternateLocal), alternate.Xml).Xml, alternate.Xml),
                "Si el atajo remoto está ocupado se conserva la alternativa local disponible de forma estable");
            string shortcutBaseline = Document(Prompt("a", "A"), Prompt("b", "B"));
            string shortcutDeletion = Change(shortcutBaseline, c => c.Entries.RemoveAll(e => e.Id == "b"));
            string shortcutEdits = Change(shortcutBaseline, c => { foreach (Entry e in c.Entries) e.Hotkey = "Ctrl+Alt+NumPad0"; });
            var shortcutRescue = CloudMerge.Merge(shortcutBaseline, shortcutDeletion, shortcutEdits);
            check(Has(shortcutRescue.Xml, "b") && Record(shortcutRescue.Xml, "b").Get("name").EndsWith(" · recuperado tras conflicto") &&
                CloudCatalog.Equivalent(shortcutRescue.Xml, CloudMerge.Merge(shortcutBaseline, shortcutDeletion, shortcutRescue.Xml).Xml),
                "Un prompt recuperado por editar contra borrar no desaparece al reintentar después de liberar su atajo");
            var favoriteLocal = Prompt("favorite-only", "El mismo texto"); favoriteLocal.Favorite = true;
            var favoriteRemote = Prompt("favorite-only", "El mismo texto");
            var favoritesJoined = CloudMerge.Merge(null, Document(favoriteLocal), Document(favoriteRemote));
            check(CloudCatalog.Read(favoritesJoined.Xml).Entries.Count == 1 && Record(favoritesJoined.Xml, "favorite-only").Favorite,
                "Primera vinculación conserva favoritos sin duplicar prompts idénticos");
            string keyedBaseline = Change(baseline, c => c.Entries[0].Hotkey = "Ctrl+Alt+NumPad2");
            string keyedL = Change(keyedBaseline, c => c.Entries[0].Set("prompt", "Edición uno"));
            string keyedR = Change(keyedBaseline, c => c.Entries[0].Set("prompt", "Edición dos"));
            var keyedConflict = CloudMerge.Merge(keyedBaseline, keyedL, keyedR);
            check(Record(keyedConflict.Xml, keyedConflict.LocalConflictIds["a"]).Hotkey == "" && Record(keyedConflict.Xml, "a").Hotkey.Length > 0,
                "Copia de conflicto no duplica el atajo global");

            string mixed = "Árbol Ω 日本語\r\nSegunda\rTercera\n  \t";
            var spaced = Prompt("space", mixed);
            spaced.Set("note", " \r\n ");
            spaced.Variables.Add(new Variable { Token = "{{x}}", Label = "Etiqueta\r\nNueva", Value = mixed });
            string whitespace = Document(spaced);
            string withOptions = Change(whitespace, c => {
                var options = c.Entries[0].Variables[0].Options;
                options.Add(new VariableOption { Label = "Luz suave", Value = mixed, Selected = true });
                options.Add(new VariableOption { Label = "Luz intensa", Value = "Luz & reflejos" });
            });
            string remoteNote = Change(whitespace, c => c.Entries[0].Set("note", "Cambio remoto"));
            var mergedOptions = Record(CloudMerge.Merge(whitespace, withOptions, remoteNote).Xml, spaced.Id);
            check(mergedOptions.Get("note") == "Cambio remoto" && mergedOptions.Variables[0].Options.Count == 2 &&
                mergedOptions.Variables[0].Options[0].Value == mixed && mergedOptions.Variables[0].Options[0].Selected &&
                mergedOptions.Variables[0].Options[1].Label == "Luz intensa" && !mergedOptions.Variables[0].Options[1].Selected,
                "La sincronización combina opciones multilínea con una edición independiente del otro equipo");
            check(Rejects(() => CloudCatalog.Validate(withOptions.Replace("label=\"Luz intensa\"", "label=\"Luz intensa\" selected=\"true\""))),
                "La nube detecta una variable con dos botones seleccionados");
            var roundtrip = CloudCatalog.Read(CloudMerge.Merge(null, whitespace, empty).Xml).Entries.Single();
            check(roundtrip.Get("prompt") == mixed && roundtrip.Get("note") == " \r\n ", "Sincronización conserva Unicode y saltos CRLF, CR, LF y espacios");
            check(roundtrip.Variables[0].Value == mixed && roundtrip.Variables[0].Label == "Etiqueta\r\nNueva", "Variables conservan saltos de línea en atributos XML");
            var pretty = new StringBuilder();
            using (XmlWriter writer = XmlWriter.Create(pretty, new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true, NewLineHandling = NewLineHandling.Entitize }))
                CloudCatalog.ParseDocument(whitespace).WriteTo(writer);
            check(CloudCatalog.Equivalent(whitespace, pretty.ToString()), "Diferencias de formato XML no provocan cambios falsos");
            string reorderedCells = Change(whitespace, c => {
                var e = c.Entries[0]; e.Cells = e.Cells.Reverse().ToDictionary(pair => pair.Key, pair => pair.Value);
                e.Set("description", "");
            });
            check(CloudCatalog.Equivalent(whitespace, reorderedCells), "Orden del diccionario y campos vacíos equivalentes no provocan conflictos");
            string swappedRecords = CloudCatalog.Write(new XDocument(CloudCatalog.ParseDocument(baseline)));
            var reverseDoc = CloudCatalog.ParseDocument(swappedRecords);
            var items = reverseDoc.Root.Element("records").Elements().ToList(); reverseDoc.Root.Element("records").ReplaceNodes(items.AsEnumerable().Reverse());
            check(CloudCatalog.Equivalent(baseline, CloudCatalog.Write(reverseDoc)), "Orden físico de registros no provoca transferencias innecesarias");

            check(Rejects(() => CloudCatalog.Validate("<!DOCTYPE promptdesk [<!ENTITY x SYSTEM 'file:///never-read'>]>" + empty)), "Catálogo remoto rechaza DTD antes de resolver entidades");
            check(Rejects(() => CloudCatalog.Validate(empty.Replace("version=\"1\"", "version=\"2\""))), "Catálogo remoto rechaza versiones incompatibles");
            check(Rejects(() => CloudCatalog.Validate(Document(Prompt("duplicate", "Uno"), Prompt("duplicate", "Dos")))), "Catálogo remoto rechaza IDs duplicados");
            var noId = CloudCatalog.ParseDocument(baseline); noId.Root.Element("records").Element("record").Attribute("id").Remove();
            check(Rejects(() => CloudCatalog.Validate(CloudCatalog.Write(noId))), "Catálogo remoto rechaza IDs ausentes en vez de inventar identidades");
            var doubleField = CloudCatalog.ParseDocument(empty); doubleField.Root.Element("columns").Add(new XElement(doubleField.Root.Element("columns").Elements().First()));
            check(Rejects(() => CloudCatalog.Validate(CloudCatalog.Write(doubleField))), "Catálogo remoto rechaza columnas duplicadas");
            var doubleCell = CloudCatalog.ParseDocument(baseline); var firstRecord = doubleCell.Root.Element("records").Elements().First(); firstRecord.Add(new XElement(firstRecord.Element("cell")));
            check(Rejects(() => CloudCatalog.Validate(CloudCatalog.Write(doubleCell))), "Catálogo remoto rechaza celdas duplicadas");
            var unknown = CloudCatalog.ParseDocument(empty); unknown.Root.Add(new XElement("unsupported", "datos"));
            check(Rejects(() => CloudCatalog.Validate(CloudCatalog.Write(unknown))), "Catálogo remoto no descarta silenciosamente secciones desconocidas");
            var duplicateSection = CloudCatalog.ParseDocument(empty); duplicateSection.Root.Add(new XElement("records"));
            check(Rejects(() => CloudCatalog.Validate(CloudCatalog.Write(duplicateSection))), "Catálogo remoto rechaza secciones duplicadas");
            string originalLocal = local;
            check(Rejects(() => CloudMerge.Merge(baseline, local, "<promptdesk>")) && local == originalLocal, "Un remoto inválido no modifica el catálogo local");
            string tooLarge = "<promptdesk>" + new string('€', CloudCatalog.MaxBytes / 3 + 1) + "</promptdesk>";
            check(Rejects(() => CloudCatalog.Validate(tooLarge)), "Límite de biblioteca mide bytes UTF-8, no cantidad de caracteres");

            string storeFolder = Path.Combine(folder, "cloud-serialize");
            var store = new Store(storeFolder); store.ReadDocument(CloudCatalog.ParseDocument(whitespace));
            string before = store.Document().ToString(SaveOptions.DisableFormatting);
            string serialized = CloudCatalog.Serialize(store);
            check(CloudCatalog.Equivalent(serialized, whitespace) && before == store.Document().ToString(SaveOptions.DisableFormatting), "Serializar Store para nube no muta datos ni saltos de línea");
            check(!File.Exists(store.Pathname), "Serializador de nube no escribe un catálogo en disco");

            // A dropped HTTP response leaves the old baseline/local snapshot but
            // the server already contains the accepted result. Exercise mixed
            // edits and first-link imports against that exact retry condition.
            var random = new Random(88231);
            for (int scenario = 0; scenario < 400; scenario++)
            {
                string initial = Document(Enumerable.Range(0, 4).Select(i => Prompt("base-" + i, "Base " + i)).ToArray());
                string scenarioLocal = EditScenario(initial, random, "L"), scenarioRemote = EditScenario(initial, random, "R");
                string ancestor = scenario % 2 == 0 ? null : initial;
                var accepted = CloudMerge.Merge(ancestor, scenarioLocal, scenarioRemote);
                var retry = CloudMerge.Merge(ancestor, scenarioLocal, accepted.Xml);
                check(CloudCatalog.Equivalent(accepted.Xml, retry.Xml), "Respuesta perdida: reintento conserva datos e identidades en escenario " + scenario);
            }
        }
    }
}
