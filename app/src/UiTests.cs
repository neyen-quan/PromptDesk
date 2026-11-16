using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Imaging;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;

namespace PromptDesk
{
    // Runs only through --ui-test with a temporary, synthetic catalogue.
    internal static class UiTests
    {
        static int checks;
        static string tracePath;

        static void Check(bool value, string message)
        {
            checks++;
            File.AppendAllText(tracePath, checks + ": " + (value ? "OK " : "ERROR ") + message + Environment.NewLine);
            if (!value) throw new Exception("Interfaz: " + message);
        }

        static T Field<T>(MainWindow window, string name)
        {
            return (T)typeof(MainWindow).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic).GetValue(window);
        }

        static void Call(MainWindow window, string name, params object[] arguments)
        {
            typeof(MainWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic).Invoke(window, arguments);
        }

        static void Choose(ComboBox box, string value)
        {
            Check(box.Items.Contains(value), "Falta la opción «" + value + "»");
            box.SelectedItem = value;
            // Exercise the same committed-selection event used by keyboard/mouse.
            typeof(ComboBox).GetMethod("OnSelectionChangeCommitted", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(box, new object[] { EventArgs.Empty });
            Pump(60);
        }

        static void Pump(int milliseconds)
        {
            var clock = Stopwatch.StartNew();
            do { Application.DoEvents(); Thread.Sleep(10); } while (clock.ElapsedMilliseconds < milliseconds);
        }

        static void Wait(Func<bool> condition, string failure)
        {
            var clock = Stopwatch.StartNew();
            while (!condition() && clock.ElapsedMilliseconds < 4000) Pump(20);
            Check(condition(), failure);
        }

        static void Capture(MainWindow window, string folder, string name)
        {
            Pump(80);
            using (var bitmap = new Bitmap(window.Width, window.Height))
            {
                using (var graphics = Graphics.FromImage(bitmap))
                    graphics.CopyFromScreen(window.Location, Point.Empty, window.Size);
                bitmap.Save(Path.Combine(folder, name + ".png"), ImageFormat.Png);
            }
        }

        static void Visible(MainWindow window, string name)
        {
            var control = Field<Control>(window, name);
            Check(control.Visible, name + " no está visible");
            var screen = control.RectangleToScreen(control.ClientRectangle);
            var bounds = new Rectangle(window.PointToClient(screen.Location), screen.Size);
            Check(window.ClientRectangle.Contains(bounds), name + " queda fuera de la ventana: " + bounds);
        }

        static void CloudHotkeyNotices(MainWindow window)
        {
            var notices = Field<System.Collections.Generic.List<string>>(window, "cloudNotices");
            notices.Clear();
            string[] errors = { "Prompt de prueba · Ctrl+Alt+NumPad1" };
            Call(window, "ReportCloudHotkeyErrors", (object)errors);
            Check(notices.Count == 1 && notices[0].Contains("Atajo no disponible"), "El atajo ocupado no genera un aviso de sincronización");
            Call(window, "ReportCloudHotkeyErrors", (object)errors);
            Check(notices.Count == 1, "Repetir la sincronización duplica el aviso del atajo ocupado");
            notices.Clear(); // The user read the notice in Mi cuenta.
            Call(window, "ReportCloudHotkeyErrors", (object)errors);
            Check(notices.Count == 0, "Un aviso de atajo ya leído reaparece en cada sincronización");
            Call(window, "ReportCloudHotkeyErrors", (object)new string[0]);
            Call(window, "ReportCloudHotkeyErrors", (object)errors);
            Check(notices.Count == 1, "Un nuevo fallo del atajo después de resolverse queda oculto");
            notices.Clear();
        }

        public static void Run(string outputFolder)
        {
            checks = 0;
            Directory.CreateDirectory(outputFolder);
            tracePath = Path.Combine(outputFolder, "pasos-ui.txt");
            File.WriteAllText(tracePath, "");
            string temp = Path.Combine(Path.GetTempPath(), "PromptDesk-ui-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new Store(temp);
                store.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
                store.Fields.Add(new Field { Id = "custom_ui", Title = "Referencia personal", Visible = true });
                store.ColumnOrder.Add("custom_ui");
                string[] titles = { "Retrato de estudio", "Escena interior", "Video con movimiento", "Retrato exterior", "Detalle de producto", "Edición localizada", "Composición editorial", "Descripción de escena" };
                for (int i = 0; i < titles.Length; i++)
                {
                    var entry = new Entry();
                    entry.Set("name", titles[i]);
                    entry.Set("collection", i == 7 ? "404" : "Personales");
                    entry.Set("category", i == 2 ? "Video" : i == 1 || i == 7 ? "Escenarios" : "Fotografía");
                    entry.Set("model", i == 2 ? "Kling" : "Higgsfield");
                    entry.Set("description", "Plantilla de ejemplo para organizar y reutilizar.");
                    entry.Set("custom_ui", "Referencia íntegra · " + i);
                    entry.Set("prompt", i == 0 || i == 3
                        ? "Retrato con luz suave. Conservá la composición, los colores y la expresión.\r\n\r\nCámara a la altura de los ojos. Texturas naturales y detalles definidos."
                        : "Describí la composición de la escena y conservá sus detalles visibles.\r\n\r\nNo agregues elementos nuevos. Mantené la iluminación y el encuadre.");
                    entry.Favorite = i == 0 || i == 2;
                    if (i == 1) entry.LastUsed = DateTime.UtcNow.ToString("o");
                    store.Entries.Add(entry);
                }
                store.Save();
                using (var window = new MainWindow(store, true))
                {
                    window.Show(); Pump(150);
                    var library = Field<ListBox>(window, "library");
                    var search = Field<TextBox>(window, "search");
                    var collection = Field<ComboBox>(window, "collection");
                    var view = Field<ComboBox>(window, "view");
                    var editor = Field<TextBox>(window, "editor");
                    collection.SelectedItem = "Todos";
                    Wait(() => library.Items.Count == 8, "El catálogo no se carga completo");
                    library.SelectedItem = store.Entries[0]; Pump(50);

                    window.Size = new Size(1440, 900); Pump(80);
                    Capture(window, outputFolder, "biblioteca-escritorio");
                    window.Size = new Size(1100, 700); Pump(80);
                    foreach (string name in new[] { "search", "library", "editor", "copyButton", "entryTitle", "entryCategory", "entryModel", "accountButton", "cloudStatus" }) Visible(window, name);
                    Check(editor.Width >= 250 && editor.Height >= 160, "El editor no tiene espacio útil en notebook");
                    Capture(window, outputFolder, "biblioteca-notebook");

                    search.Text = "RETRATO luz";
                    Wait(() => library.Items.Count == 2, "Búsqueda combinada por nombre y contenido");
                    search.Text = "sin coincidencias 123";
                    Wait(() => library.Items.Count == 0, "La búsqueda vacía conserva resultados anteriores");
                    Check(Field<Entry>(window, "selected") == null && !Field<Control>(window, "copyButton").Enabled, "Copiar permanece activo sin selección");
                    Capture(window, outputFolder, "busqueda-sin-resultados");
                    search.Clear(); Wait(() => library.Items.Count == 8, "Limpiar búsqueda no recupera el catálogo");
                    view.SelectedItem = "Favoritos";
                    Wait(() => library.Items.Count == 2, "Filtro de favoritos");
                    view.SelectedItem = "Recientes";
                    Wait(() => library.Items.Count == 1, "Filtro de recientes");
                    view.SelectedItem = "Todos";
                    Wait(() => library.Items.Count == 8, "Filtro de todos");

                    Entry target = store.Entries[0];
                    library.SelectedItem = target; Pump(40);
                    string original = target.Get("prompt");
                    editor.Text = original + "\r\nUna edición para comprobar el guardado.";
                    Wait(() => !Field<bool>(window, "dirty"), "El autoguardado no termina");
                    var reread = new Store(temp); reread.Load(reread.Pathname);
                    Check(reread.Entries.First(e => e.Id == target.Id).Get("prompt") == editor.Text, "El editor pierde cambios al guardar");
                    Call(window, "UndoChange", false); Pump(60);
                    Check(store.Entries.First(e => e.Id == target.Id).Get("prompt") == original, "Deshacer edición del prompt");
                    Call(window, "UndoChange", true); Pump(60);
                    Check(store.Entries.First(e => e.Id == target.Id).Get("prompt").EndsWith("guardado."), "Rehacer edición del prompt");

                    target = store.Entries.First(e => e.Id == target.Id);
                    library.SelectedItem = target; Pump(40);
                    Field<TextBox>(window, "entryTitle").Text = "Retrato actualizado";
                    editor.Focus(); Pump(100);
                    Check(target.Get("name") == "Retrato actualizado", "Edición directa del nombre");
                    Field<GlassButton>(window, "tableModeButton").PerformClick(); Pump(60);
                    Check(Field<bool>(window, "tableMode") && Field<DataGridView>(window, "grid").Rows.Count == 8, "Vista de tabla incompleta");
                    Field<GlassButton>(window, "listModeButton").PerformClick(); Pump(60);
                    Check(!Field<bool>(window, "tableMode") && Field<Entry>(window, "selected").Id == target.Id, "Cambiar vista pierde la selección");
                    Call(window, "Copy", target); Pump(40);
                    Check(Clipboard.GetText() == target.Get("prompt"), "Copiar cambia el contenido");
                    Check(target.LastUsed.Length > 0, "Copiar no registra el uso reciente");

                    // Creating from an empty filtered view must reveal the new
                    // prompt immediately instead of leaving it hidden by filters.
                    view.SelectedItem = "Favoritos";
                    search.Text = "búsqueda sin resultados para crear";
                    Wait(() => library.Items.Count == 0, "Preparación del filtro para crear");
                    int previousCount = store.Entries.Count;
                    Call(window, "AddEntry"); Pump(70);
                    var created = Field<Entry>(window, "selected");
                    Check(created != null && store.Entries.Count == previousCount + 1, "Nuevo prompt no agrega una entrada");
                    Check(library.SelectedItem == created && library.Items.Contains(created), "El prompt nuevo queda oculto o sin seleccionar");
                    Check(search.Text.Length == 0 && view.Text == "Todos", "Crear no quita los filtros incompatibles");
                    Check(editor.Enabled && !Field<Control>(window, "copyButton").Enabled, "El prompt vacío no queda listo para editar");
                    Field<TextBox>(window, "entryTitle").Text = "Creado desde favoritos";
                    editor.Text = "Contenido nuevo para comprobar la creación directa.";
                    Wait(() => !Field<bool>(window, "dirty"), "El prompt nuevo no se guarda");

                    // Existing behavior duplicates 404 material into Personales.
                    // Both the untouched source and the visible copy must survive.
                    collection.SelectedItem = "404";
                    Wait(() => library.Items.Count == 1, "Filtro 404 antes de duplicar");
                    var source404 = (Entry)library.Items[0]; library.SelectedItem = source404;
                    string sourceSnapshot = source404.Xml().ToString();
                    previousCount = store.Entries.Count;
                    Call(window, "Duplicate"); Pump(70);
                    var duplicate = Field<Entry>(window, "selected");
                    Check(store.Entries.Count == previousCount + 1 && duplicate.Id != source404.Id, "Duplicar reutiliza la identidad original");
                    Check(duplicate.Get("prompt") == source404.Get("prompt") && duplicate.Get("custom_ui") == source404.Get("custom_ui"), "Duplicar pierde contenido o campos personalizados");
                    Check(source404.Xml().ToString() == sourceSnapshot, "Duplicar modifica el registro de origen");
                    Check(duplicate.Get("collection") == "Personales" && collection.Text == "Personales" && library.SelectedItem == duplicate,
                        "La copia desde 404 no se muestra en su colección de destino");

                    Choose(Field<ComboBox>(window, "entryCategory"), "Fotografía");
                    Check(duplicate.Get("category") == "Fotografía" && Field<Entry>(window, "selected").Id == duplicate.Id,
                        "Cambiar categoría directamente pierde la entrada o la selección");
                    Choose(Field<ComboBox>(window, "entryModel"), "Kling");
                    Check(duplicate.Get("model") == "Kling" && Field<ComboBox>(window, "entryModel").Text == "Kling",
                        "Cambiar modelo directamente no actualiza el prompt");

                    Call(window, "SwitchMode", true); Pump(70);
                    var table = Field<DataGridView>(window, "grid");
                    Capture(window, outputFolder, "tabla-notebook");
                    Check(table.Height > table.ColumnHeadersHeight + table.RowTemplate.Height + SystemInformation.HorizontalScrollBarHeight, "La tabla no deja espacio para una fila completa");
                    Check(table.Columns.Contains("custom_ui") && table.Columns["custom_ui"].Visible, "La tabla pierde el campo personalizado visible");
                    var duplicateRow = table.Rows.Cast<DataGridViewRow>().First(row => ((Entry)row.Tag).Id == duplicate.Id);
                    Check(Convert.ToString(duplicateRow.Cells["custom_ui"].Value) == duplicate.Get("custom_ui"), "La tabla cambia un valor personalizado");
                    table.CurrentCell = duplicateRow.Cells["custom_ui"];
                    Check(table.BeginEdit(true), "No se puede editar un campo personalizado desde la tabla");
                    var customEditor = table.EditingControl as TextBox;
                    Check(customEditor != null, "No se abrió el editor del campo personalizado");
                    customEditor.Text = "Referencia editada desde la tabla · Ω";
                    table.EndEdit(); Pump(90);
                    Field<GlassButton>(window, "listModeButton").PerformClick(); Pump(60);
                    Check(Field<Entry>(window, "selected").Id == duplicate.Id && duplicate.Get("custom_ui") == "Referencia editada desde la tabla · Ω",
                        "Alternar tabla y lista pierde la edición personalizada o la selección");
                    Wait(() => !Field<bool>(window, "dirty"), "Los metadatos y campos personalizados no se guardan");
                    reread.Load(reread.Pathname);
                    var savedDuplicate = reread.Entries.First(entry => entry.Id == duplicate.Id);
                    Check(savedDuplicate.Get("category") == "Fotografía" && savedDuplicate.Get("model") == "Kling"
                        && savedDuplicate.Get("custom_ui") == "Referencia editada desde la tabla · Ω", "La edición de metadatos no persiste al reabrir el catálogo");
                    Check(reread.Fields.Any(field => field.Id == "custom_ui") && reread.ColumnOrder.Contains("custom_ui"), "Alternar vistas pierde el esquema u orden personalizado");

                    // A cloud response arriving during typing must preserve those
                    // later edits and mark them pending for the next exchange.
                    Call(window, "ResetFilters");
                    library.SelectedItem = duplicate; Pump(40);
                    string sentSnapshot = CloudCatalog.Serialize(store);
                    var server = new Store(Path.Combine(temp, "server-simulation"));
                    server.ReadDocument(CloudCatalog.ParseDocument(sentSnapshot));
                    var remoteEntry = new Entry(); remoteEntry.Set("name", "Agregado desde la otra PC");
                    remoteEntry.Set("collection", "Personales"); remoteEntry.Set("prompt", "Texto remoto íntegro.");
                    server.Entries.Add(remoteEntry);
                    string serverSnapshot = CloudCatalog.Serialize(server);
                    editor.Text = "Edición local mientras la sincronización estaba en curso.\r\nSegunda línea.";
                    string lateText = editor.Text;
                    Call(window, "ApplyCloudExchange", sentSnapshot, new CloudExchange {
                        ServerXml = serverSnapshot, Revision = 2,
                        Conflicts = new System.Collections.Generic.List<string>(),
                        LocalConflictIds = new System.Collections.Generic.Dictionary<string, string>() });
                    Check(store.Entries.Any(entry => entry.Id == remoteEntry.Id), "La respuesta de nube no incorpora el prompt remoto");
                    Check(store.Entries.First(entry => entry.Id == duplicate.Id).Get("prompt") == lateText, "La respuesta tardía pisa lo escrito durante la sincronización");
                    Check(Field<Entry>(window, "selected").Id == duplicate.Id && editor.Text == lateText, "La sincronización pierde la selección o el editor local");
                    Check(Field<bool>(window, "cloudPending"), "Las ediciones tardías quedan marcadas como ya sincronizadas");
                    Check(Field<CloudSyncState>(window, "cloudState").BaselineXml == serverSnapshot, "La base remota incluye cambios locales aún no enviados");
                    reread.Load(reread.Pathname);
                    Check(reread.Entries.First(entry => entry.Id == duplicate.Id).Get("prompt") == lateText, "La integración de nube no se guarda de forma durable");
                    Check(!Field<UndoHistory>(window, "history").CanUndo, "Deshacer antiguo podría borrar prompts llegados de otro equipo");

                    editor.Text = lateText + "\r\nCambio posterior al merge.";
                    string pushedSnapshot = CloudCatalog.Serialize(store);
                    var localHistory = Field<UndoHistory>(window, "history");
                    Check(localHistory.CanUndo, "No queda disponible deshacer para los cambios nuevos");
                    Call(window, "ApplyCloudExchange", pushedSnapshot, new CloudExchange {
                        ServerXml = pushedSnapshot, Revision = 3,
                        Conflicts = new System.Collections.Generic.List<string>(),
                        LocalConflictIds = new System.Collections.Generic.Dictionary<string, string>() });
                    Check(Object.ReferenceEquals(localHistory, Field<UndoHistory>(window, "history")) && localHistory.CanUndo,
                        "Subir cambios locales sin recibir cambios remotos borra el historial de deshacer");
                    Check(!Field<bool>(window, "cloudPending"), "Una subida confirmada queda marcada como pendiente");
                    CloudHotkeyNotices(window);
                    window.Close();
                }

                var empty = new Store(Path.Combine(temp, "empty"));
                empty.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
                using (var window = new MainWindow(empty, true))
                {
                    window.Show(); window.Size = new Size(1100, 700); Pump(100);
                    Check(Field<ListBox>(window, "library").Items.Count == 0, "La instalación vacía contiene prompts ajenos");
                    Check(!Field<Control>(window, "copyButton").Enabled, "Copiar habilitado en catálogo vacío");
                    Capture(window, outputFolder, "biblioteca-vacia");
                    window.Close();
                }
                File.WriteAllText(Path.Combine(outputFolder, "pruebas-ui-ok.txt"),
                    "PASS: " + checks + " comprobaciones de interfaz, filtros, guardado, deshacer/rehacer, vista de tabla y portapapeles.");
            }
            finally { if (Directory.Exists(temp)) Directory.Delete(temp, true); }
        }
    }
}
