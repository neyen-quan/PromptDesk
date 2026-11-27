using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace PromptDesk
{
    public static class BackendTests
    {
        static Store EmptyStore(string folder)
        {
            var database = new Store(folder);
            database.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
            return database;
        }

        static bool Fails(Action action)
        {
            try { action(); return false; }
            catch { return true; }
        }

        static Entry Prompt(string name, string collection, string category, string model, string text)
        {
            var entry = new Entry();
            entry.Set("name", name);
            entry.Set("collection", collection);
            entry.Set("category", category);
            entry.Set("model", model);
            entry.Set("prompt", text);
            return entry;
        }

        public static void Run(Action<bool, string> check, string folder)
        {
            CheckStorage(check, Path.Combine(folder, "storage"));
            CheckFolders(check, Path.Combine(folder, "subfolders"));
            CheckFolderManagement(check, Path.Combine(folder, "category-manager"));
            CheckVariableOptions(check, Path.Combine(folder, "variable-options"));
            CheckHistory(check, Path.Combine(folder, "history"));
            CheckSearch(check);
            CheckTemplates(check);
        }

        static void CheckFolders(Action<bool, string> check, string folder)
        {
            var db = EmptyStore(folder);
            db.Categories.Add("404::Reels");
            db.Categories.Add("404::Reels › Accesorios"); // An existing flat category with punctuation.
            db.Entries.Add(Prompt("Ropa", "Personales", "Reels", "Modelo", "Texto de prueba"));
            check(db.Document().Root.Element("subfolders") == null, "Una biblioteca anterior conserva el formato de categorías planas");
            var history = new UndoHistory(db);
            string child = CategoryFolders.Create(db, "Personales", "Reels", " Accesorios ");
            history.Record(db, "");
            check(CategoryFolders.Parent(db, "Personales", child) == "Reels" && CategoryFolders.Parent(db, "404", child) == null,
                "La jerarquía es por colección y conserva las categorías planas anteriores con separadores");
            check(Fails(() => CategoryFolders.Create(db, "Personales", "Reels", "Accesorios")) &&
                Fails(() => CategoryFolders.Create(db, "404", "Reels", "Accesorios")),
                "La creación rechaza colisiones con subcarpetas y categorías existentes");
            check(history.Undo(db) && db.Subfolders.Count == 0 && history.Redo(db) && db.Subfolders.Count == 1,
                "Deshacer y rehacer conservan la estructura de las carpetas");
            string deep = CategoryFolders.Create(db, "Personales", child, "Collares");
            db.Entries[0].Set("category", deep);
            db.Save();
            var reread = EmptyStore(Path.Combine(folder, "otra"));
            reread.Load(db.Pathname);
            check(CategoryFolders.IsAtOrBelow(reread, "Personales", deep, "Reels") &&
                reread.Categories.Contains("Personales::Reels") && reread.Entries[0].Get("category") == deep,
                "Reabrir conserva los ancestros, incluso si el último prompt del padre se movió a un nieto");
        }

        static void CheckFolderManagement(Action<bool, string> check, string folder)
        {
            const string scope = "Personales";
            var db = EmptyStore(folder);
            db.Categories.Add("Campaña"); // Old bare names are available in both collections.
            db.Categories.Add("Personales::Campaña › Independiente");
            string product = CategoryFolders.Create(db, scope, "Campaña", "Producto");
            string blue = CategoryFolders.Create(db, scope, product, "Azul");
            string other = CategoryFolders.Create(db, scope, "Campaña", "Otro");
            string remote = CategoryFolders.Create(db, "404", "Campaña", "Producto");
            db.CategoryOrder.AddRange(new[] { scope+"::"+blue, scope+"::"+product, scope+"::"+other });
            db.DisabledChoices.Add("category::"+scope+"::"+product);
            var archived = Prompt("Catálogo", scope, blue, "", "Ejemplo"); archived.Archived = true;
            var another = Prompt("Colección 404", "404", remote, "", "Ejemplo");
            db.Entries.Add(archived); db.Entries.Add(another);
            check(CategoryCatalog.Values(db, scope).Take(4).SequenceEqual(new[] { "Campaña", product, blue, other }),
                "El orden manual de cada carpeta conserva juntas sus subcarpetas incluso al ordenar nietos");
            string before = db.Document().ToString();
            check(Fails(() => CategoryCatalog.Rename(db, scope, product, "Otro")) && db.Document().ToString() == before,
                "Renombrar a una carpeta ocupada conserva intacta toda la rama");
            CategoryCatalog.Rename(db, scope, "Campaña", "Anuncios");
            check(archived.Get("category") == "Anuncios › Producto › Azul" && archived.Archived &&
                another.Get("category") == remote && CategoryCatalog.Values(db, "404").Contains("Campaña") &&
                db.DisabledChoices.Contains("category::Personales::Anuncios › Producto") &&
                db.CategoryOrder.Contains("Personales::Anuncios › Producto › Azul") &&
                CategoryCatalog.Values(db, scope).Contains("Campaña › Independiente"),
                "Renombrar respeta la otra colección, las categorías planas anteriores y los datos de la rama");
            CategoryCatalog.Rename(db, scope, "Anuncios › Producto", "Material");
            check(CategoryFolders.Parent(db, scope, archived.Get("category")) == "Anuncios › Material",
                "Renombrar una subcarpeta conserva su padre y la jerarquía de las carpetas hijas");
            CategoryCatalog.Remove(db, scope, "Anuncios › Material");
            check(archived.Get("category") == "" && archived.Archived && db.Entries.Contains(archived) &&
                another.Get("category") == remote && CategoryCatalog.Values(db, scope).Contains("Anuncios › Otro") &&
                !db.CategoryOrder.Any(v => v.StartsWith("Personales::Anuncios › Material")) &&
                !db.DisabledChoices.Contains("category::Personales::Anuncios › Material") &&
                !CategoryCatalog.Values(db, scope).Contains("Anuncios › Material › Azul"),
                "Eliminar una rama conserva los prompts, sus hermanos y las categorías de otras colecciones");
        }

        static void CheckStorage(Action<bool, string> check, string folder)
        {
            Store database = EmptyStore(folder);
            database.Entries.Add(Prompt("Original", "Personales", "Prueba", "Modelo", "Texto intacto"));
            check(database.Entries[0].Xml().Attribute("archived") == null &&
                !Entry.Read(XElement.Parse("<record id='anterior'/>")).Archived,
                "Los prompts existentes y nuevos empiezan activos");
            database.Save();
            byte[] bytes = File.ReadAllBytes(database.Pathname);
            string state = database.Document().ToString();
            XDocument invalid = database.Document();
            invalid.Descendants("record").First().Add(new XElement("cell", new XAttribute("key", "prompt"), "Duplicado"));
            check(Fails(() => database.ReadDocument(invalid)), "Un campo duplicado se sobrescribe silenciosamente");
            check(database.Document().ToString() == state, "Una carga inválida modifica el catálogo en memoria");

            string dtd = Path.Combine(folder, "with-dtd.xml");
            File.WriteAllText(dtd, "<!DOCTYPE promptdesk [<!ENTITY text 'alterado'>]>" +
                database.Document().ToString(SaveOptions.DisableFormatting));
            check(Fails(() => database.Load(dtd)), "El catálogo acepta definiciones DTD");
            check(database.Document().ToString() == state, "Una carga rechazada altera el estado anterior");

            string otherTemporary = database.Pathname + ".tmp";
            File.WriteAllText(otherTemporary, "archivo previo que no pertenece a este guardado");
            database.Entries[0].Set("prompt", "Texto inválido \u0001");
            check(Fails(database.Save), "El guardado aceptó caracteres XML inválidos");
            check(File.ReadAllBytes(database.Pathname).SequenceEqual(bytes), "Un fallo de guardado alteró el catálogo anterior");
            check(File.ReadAllText(otherTemporary).StartsWith("archivo previo"), "El guardado sobrescribió un temporal ajeno");
            check(Directory.GetFiles(folder, "*.tmp").Length == 1, "El guardado fallido dejó su archivo incompleto");

            database.Entries[0].Set("prompt", "\t texto con espacios \r\n\n y Unicode ñ 🚀\rúltima línea ");
            database.Entries[0].Set("note", " \t\r\n\n ");
            string exported = Path.Combine(folder, "streamed.xlsx");
            File.WriteAllText(exported + ".tmp", "temporal ajeno");
            Excel.Write(exported, database);
            SheetData sheet = Excel.Read(exported)[0];
            int promptColumn = database.Fields.FindIndex(field => field.Id == "prompt");
            check(sheet.Rows[1][promptColumn] == database.Entries[0].Get("prompt"), "La exportación streaming altera espacios/Unicode");
            check(File.ReadAllText(exported + ".tmp") == "temporal ajeno", "La exportación sobrescribió un temporal ajeno");
            database.Save();
            Store reread = EmptyStore(folder);
            reread.Load(database.Pathname);
            check(reread.Entries[0].Get("prompt") == database.Entries[0].Get("prompt"),
                "Guardar/cargar normaliza los saltos CRLF, LF o CR del prompt");
            check(reread.Entries[0].Get("note") == database.Entries[0].Get("note"),
                "Guardar/cargar elimina un campo que contiene solo espacios");
            string fullBackup = Path.Combine(folder, "exported-backup.xml");
            database.Entries[0].Archived = true;
            database.Export(fullBackup);
            reread.Load(fullBackup);
            Entry archived = reread.Entries[0];
            check(archived.Archived && archived.Id == database.Entries[0].Id &&
                archived.Get("collection") == "Personales" && archived.Get("category") == "Prueba",
                "El respaldo XML conserva el estado archivado, identidad y carpeta de origen");
            check(!archived.Clone().Archived && archived.Archived, "Duplicar un prompt archivado crea una copia activa");
            check(reread.Entries[0].Get("prompt") == database.Entries[0].Get("prompt") &&
                reread.Entries[0].Get("note") == database.Entries[0].Get("note"),
                "Exportar un respaldo modifica saltos de línea o espacios");
            byte[] exportedBytes = File.ReadAllBytes(fullBackup);
            database.Entries[0].Set("prompt", "XML inválido \u0001");
            check(Fails(() => database.Export(fullBackup)), "La exportación XML aceptó caracteres inválidos");
            check(File.ReadAllBytes(fullBackup).SequenceEqual(exportedBytes), "Una exportación fallida truncó el respaldo anterior");
            check(Directory.GetFiles(folder, "exported-backup.xml.*.tmp").Length == 0,
                "Una exportación XML fallida dejó archivos temporales");
            check(Directory.GetFiles(Path.Combine(folder, "Historial"), "*.xml").Length == 1,
                "Un guardado fallido agregó un respaldo vacío");
        }

        static void CheckVariableOptions(Action<bool, string> check, string folder)
        {
            Store database = EmptyStore(folder);
            Entry entry = Prompt("Collar", "Personales", "", "", "Modo: {{collar}}");
            Variable variable = Variable.Read(XElement.Parse("<var token='{{collar}}' label='Collar' value='A mano'/>"));
            entry.Variables.Add(variable);
            database.Entries.Add(entry);
            check(variable.Options.Count == 0 && Templates.Render(entry) == "Modo: A mano",
                "Una variable anterior debe conservar su valor libre");

            const string longValue = " Acentos, <tela> & \"collar\".\r\nSegunda línea.\n\t ";
            variable.Options.Add(new VariableOption { Label = "Por debajo", Value = longValue });
            variable.Options.Add(new VariableOption { Label = "Sin collar", Value = "Sin cadena." });
            database.Save();
            database.Load(database.Pathname);
            Entry loaded = database.Entries.Single();
            Variable restored = loaded.Variables.Single();
            check(restored.Options.Select(option => option.Label).SequenceEqual(new[] { "Por debajo", "Sin collar" }) &&
                restored.Options[0].Value == longValue && restored.Value == "A mano",
                "El catálogo altera las opciones de una variable o reemplaza su último valor");
            check(restored.Options.All(option => !option.Selected) && !restored.Xml().Descendants().Attributes("selected").Any(),
                "Las opciones sin selección guardada se abren en Libre");
            Entry copy = loaded.Clone();
            copy.Variables[0].Options[0].Value = "Otro texto";
            check(restored.Options[0].Value == longValue, "El duplicado comparte sus opciones con el original");
            restored.Value = restored.Options[0].Value;
            restored.Options[0].Selected = true;
            restored.Options.Reverse();
            database.Save();
            database.Load(database.Pathname);
            Variable reopened = database.Entries.Single().Variables.Single();
            check(reopened.Options[0].Label == "Sin collar" && !reopened.Options[0].Selected &&
                reopened.Options[1].Label == "Por debajo" && reopened.Options[1].Selected && reopened.Value == longValue,
                "Reabrir recupera el orden elegido y el botón seleccionado independientemente de su posición");
            check(Templates.Render(loaded) == "Modo: " + longValue && loaded.Get("prompt") == "Modo: {{collar}}",
                "La opción elegida no conserva su texto al sustituir el marcador");
        }

        static void CheckHistory(Action<bool, string> check, string folder)
        {
            Store database = EmptyStore(folder);
            const string before = "antes\r\nsegunda línea\nlínea con LF\rúltima";
            const string after = "edición 9\r\nsegunda línea\ntercera\rúltima";
            Entry first = Prompt("Primero", "Personales", "Retrato", "Modelo", before);
            Entry second = Prompt("Segundo", "404", "Paisaje", "Otro", "estable");
            database.Entries.Add(first);
            database.Entries.Add(second);
            string firstId = first.Id, secondId = second.Id;
            var history = new UndoHistory(database);
            for (int index = 0; index < 10; index++)
            {
                first.Set("prompt", "edición " + index + "\r\nsegunda línea\ntercera\rúltima");
                history.Record(database, "prompt:" + first.Id);
            }
            first.LastUsed = "2026-11-16T12:00:00.0000000Z";
            check(history.Undo(database), "No se puede deshacer la edición agrupada");
            check(database.Entries[0].Get("prompt") == before, "Deshacer altera el texto o normaliza sus saltos de línea");
            check(!history.CanUndo && history.CanRedo, "Una ráfaga de escritura creó varios pasos de historial");
            check(database.Entries[0].LastUsed == first.LastUsed, "Deshacer elimina la fecha de uso reciente");
            check(history.Redo(database) && database.Entries[0].Get("prompt") == after, "Rehacer no restaura el texto y sus saltos de línea exactos");
            database.Entries[1].Set("prompt", "otro cambio");
            history.Record(database, "prompt:" + secondId);
            check(history.Undo(database) && database.Entries[1].Get("prompt") == "estable", "Se mezclan ediciones de prompts distintos");
            check(database.Entries[0].Get("prompt") == after, "Deshacer un prompt alteró otro");

            database.Entries[0].Set("name", "Nombre nuevo");
            history.Record(database, "name:" + firstId);
            check(!history.CanRedo, "Una nueva edición conserva una rama de rehacer incompatible");
            check(history.Undo(database) && database.Entries[0].Get("name") == "Primero", "No se puede deshacer el nombre");
            check(database.Entries[0].Get("prompt") == after, "La edición del nombre afecta al contenido");
            check(history.Redo(database), "No se puede rehacer el nombre");

            database.Fields.Add(new Field { Id = "custom_test", Title = "Referencia" });
            database.Entries[0].Set("custom_test", "valor original");
            database.ColumnOrder.Add("custom_test");
            database.CategoryOrder.Add("Personales::Retrato");
            database.DisabledChoices.Add("model::Modelo oculto");
            history.Record(database, "");
            check(history.Undo(database) && !database.Fields.Any(field => field.Id == "custom_test"), "Deshacer no elimina el campo recién creado");
            check(history.Redo(database) && database.Entries[0].Get("custom_test") == "valor original" &&
                database.ColumnOrder.Contains("custom_test") && database.CategoryOrder.Contains("Personales::Retrato") &&
                database.DisabledChoices.Contains("model::Modelo oculto"), "Rehacer pierde ajustes o campos personalizados");

            database.Entries.RemoveAt(1);
            history.Record(database, "");
            check(history.Undo(database) && database.Entries[1].Id == secondId, "Deshacer una eliminación pierde identidad u orden");
            history = new UndoHistory(database);
            for (int index = 0; index < 160; index++)
            {
                database.Entries[0].Set("name", "nombre " + index);
                history.Record(database, "");
            }
            int undone = 0;
            while (history.Undo(database)) undone++;
            check(undone == 150 && database.Entries[0].Get("name") == "nombre 9", "El límite de historial no conserva los 150 cambios más recientes");
        }

        static void CheckSearch(Action<bool, string> check)
        {
            Entry first = Prompt("A Fotografía", "404", "Retrato", "Imagen", "Luz suave");
            Entry second = Prompt("B Video", "Personales", "Retrato", "Video", "Calle de noche");
            Entry third = Prompt("C Paisaje", "404", "Paisaje", "Imagen", "Luz de ciudad");
            first.Favorite = third.Favorite = true;
            second.LastUsed = "2026-11-16T13:00:00Z";
            third.LastUsed = "2026-11-15T13:00:00Z";
            third.Set("custom_reference", "Otoño");
            var entries = new List<Entry> { third, first, second };
            Func<string, List<Entry>> find = query => LibraryQuery.Filter(entries, "Todos", null, null, "Todos", query, "name", false);
            check(find("FOTOGRAFIA suave").SequenceEqual(new[] { first }), "La búsqueda no combina campos o no ignora tildes/mayúsculas");
            check(find("suave fotografia").SequenceEqual(new[] { first }), "El orden de palabras cambia la búsqueda");
            check(find("otono").SequenceEqual(new[] { third }), "La búsqueda no encuentra campos personalizados");
            check(find("luz retrato").SequenceEqual(new[] { first }), "Las palabras deben coincidir entre campos de un mismo prompt");
            check(find("   ").SequenceEqual(new[] { first, second, third }), "Una búsqueda vacía no devuelve la biblioteca ordenada");
            check(find("inexistente").Count == 0, "La búsqueda devuelve resultados ajenos");
            check(LibraryQuery.Filter(entries, "404", "Retrato", "Imagen", "Favoritos", "", "name", false)
                .SequenceEqual(new[] { first }), "Los filtros combinados no se respetan");
            check(LibraryQuery.Filter(entries, null, null, null, "Recientes", "", "name", false)
                .SequenceEqual(new[] { second, third }), "Recientes no respeta las fechas o incluye prompts sin uso");
            check(LibraryQuery.Filter(entries, null, null, null, "Todos", "", "name", true)
                .SequenceEqual(new[] { third, second, first }), "Orden descendente incorrecto");
            check(entries.SequenceEqual(new[] { third, first, second }), "Una consulta reordena el catálogo original");
        }

        static void CheckTemplates(Action<bool, string> check)
        {
            var json = new Entry();
            json.Set("prompt", "{\"text\":\"{{value}}\"}");
            json.Variables.Add(new Variable { Token = "{{value}}", Value = "\u0001\b\f\n\r\t\\\"" });
            check(Templates.Render(json) == "{\"text\":\"\\u0001\\b\\f\\n\\r\\t\\\\\\\"\"}", "Las variables JSON no escapan todos los caracteres de control");
            json.Variables.Add(new Variable { Token = "{{value}}", Value = "duplicado" });
            check(Fails(() => Templates.Render(json)), "Variables repetidas aceptadas");
            check(Fails(() => Templates.Pattern("")), "Marcador vacío aceptado");
        }
    }
}
