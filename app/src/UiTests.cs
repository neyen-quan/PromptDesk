using System;
using System.Collections.Generic;
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

        static void Capture(Form window, string folder, string name)
        {
            Pump(80);
            using (var bitmap = new Bitmap(window.Width, window.Height))
            {
                window.DrawToBitmap(bitmap, new Rectangle(Point.Empty, window.Size));
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

        static GlassButton OptionButton(VariableChoiceStrip strip, VariableOption option)
        {
            return strip.Controls.OfType<GlassButton>().Single(b => Object.ReferenceEquals(b.Tag, option));
        }

        static void DragChoice(GlassButton source, GlassButton destination, bool after)
        {
            var start = new Point(source.Width / 2, source.Height / 2);
            var end = source.PointToClient(destination.PointToScreen(new Point(
                after ? destination.Width - 5 : 5, destination.Height / 2)));
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            foreach (string method in new[] { "OnMouseDown", "OnMouseMove", "OnMouseUp" })
            {
                var point = method == "OnMouseDown" ? start : end;
                var args = new MouseEventArgs(MouseButtons.Left, 1, point.X, point.Y, 0);
                source.GetType().GetMethod(method, flags).Invoke(source, new object[] { args });
            }
            Pump(30);
        }

        static void VariableDialogs(MainWindow window, Store store, string outputFolder)
        {
            var entry = new Entry();
            entry.Set("name", "Iluminación de estudio");
            const string template = "Escena:\n\n{{luz}}.\rFin.";
            entry.Set("prompt", template);
            var variable = new Variable { Token = "{{luz}}", Label = "Iluminación", Value = "Luz frontal" };
            var soft = new VariableOption { Label = "Suave", Value = "Luz difusa\ncon sombras suaves" };
            var direct = new VariableOption { Label = "Directa", Value = "Luz frontal" };
            var backlight = new VariableOption { Label = "Contraluz con reflector plateado", Value = "Contraluz" };
            variable.Options.AddRange(new[] { soft, direct, backlight });
            entry.Variables.Add(variable);
            store.Entries.Add(entry);
            using (var form = VariableEditor.Create(entry))
            {
                form.Show(window);
                Pump(70);
                var grid = (DataGridView)form.Controls.Find("VariableGrid", true).Single();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    var style = row.Cells[0].InheritedStyle;
                    Check(row.Height >= style.Font.Height + style.Padding.Vertical + 2,
                        "La altura inicial de la fila de variable debe incluir texto y márgenes");
                }
                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
                    bitmap.Save(Path.Combine(outputFolder, "editor-de-variables.png"), ImageFormat.Png);
                }
                form.Close();
            }

            Exception dialogError = null;
            window.BeginInvoke((Action)delegate {
                var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text.StartsWith("Asignar valores"));
                try
                {
                    var choice = (VariableChoiceStrip)dialog.Controls.Find("variableChoice0", true).Single();
                    var value = (TextBox)dialog.Controls.Find("variableValue0", true).Single();
                    Pump(40);
                    var free = OptionButton(choice, null);
                    Check(choice.Controls[0] == free && free.Text == "Libre" && free.Active && !value.ReadOnly,
                        "Una variable anterior empieza en Libre aunque su texto coincida con una opción");
                    Check(value.Focused || dialog.ActiveControl == value, "Libre deja el campo listo para escribir al abrir");
                    value.Text = "Borrador libre";
                    OptionButton(choice, direct).PerformClick();
                    free.PerformClick();
                    Check(value.Text == "Borrador libre", "El borrador libre se conserva al alternar entre botones");
                    OptionButton(choice, soft).PerformClick();
                    Check(choice.SelectedOption == soft && OptionButton(choice, soft).Active &&
                        choice.Controls.OfType<GlassButton>().Count(button => button.Active) == 1,
                        "La botonera deja una sola opción seleccionada");
                    Check(value.ReadOnly && value.Text == "Luz difusa\r\ncon sombras suaves", "Elegir una opción muestra sus dos líneas");
                    using (var bitmap = new Bitmap(dialog.Width, dialog.Height))
                    {
                        dialog.DrawToBitmap(bitmap, new Rectangle(Point.Empty, dialog.Size));
                        bitmap.Save(Path.Combine(outputFolder, "copiar-con-opciones.png"), ImageFormat.Png);
                    }
                    ((GlassButton)dialog.Controls.Find("confirmVariableValues", true).Single()).PerformClick();
                }
                catch (Exception ex) { dialogError = ex; if (dialog != null && dialog != window) dialog.Close(); }
            });
            Call(window, "Copy", entry);
            if (dialogError != null) throw dialogError;
            Check(soft.Selected && variable.Value == soft.Value && Clipboard.GetText() == "Escena:\r\n\r\nLuz difusa\r\ncon sombras suaves.\r\nFin.",
                "Copiar usa el texto de la opción, con sus saltos de línea");
            Check(entry.Get("prompt") == template, "Copiar conserva el marcador y los separadores originales en la plantilla");
            var second = new Variable { Token = "{{fondo}}", Label = "Fondo", Value = "Blanco" };
            second.Options.Add(new VariableOption { Label = "Negro", Value = "Fondo oscuro" });
            entry.Variables.Add(second);
            using (var form = window.CreateVariableValuesDialog(entry))
            {
                form.Width = 400;
                form.Show(window); Pump(50);
                var choice = (VariableChoiceStrip)form.Controls.Find("variableChoice0", true).Single();
                var value = (TextBox)form.Controls.Find("variableValue0", true).Single();
                var other = (VariableChoiceStrip)form.Controls.Find("variableChoice1", true).Single();
                Check(choice.SelectedOption == soft, "Volver a copiar recuerda el botón confirmado");
                var free = OptionButton(choice, null);
                var last = OptionButton(choice, backlight);
                Check(last.Top > free.Top && last.Bottom <= choice.ClientSize.Height,
                    "Al estrechar la ventana los botones pasan a otra fila y siguen visibles");
                DragChoice(free, last, true);
                DragChoice(OptionButton(choice, direct), OptionButton(other, second.Options[0]), false);
                Check(choice.Controls[0] == free && variable.Options.SequenceEqual(new[] { soft, direct, backlight })
                    && second.Options.Count == 1, "Libre queda fijo y las opciones se arrastran solo dentro de su marcador");
                DragChoice(OptionButton(choice, soft), last, true);
                Check(variable.Options.SequenceEqual(new[] { direct, backlight, soft }) && choice.SelectedOption == soft
                    && choice.Controls[0] == free && choice.Controls[3].Tag == soft,
                    "Arrastrar entre filas cambia el orden manteniendo Libre primero y la opción seleccionada");
                free.PerformClick();
                Check(!value.ReadOnly, "Texto libre permite escribir un valor personalizado");
                value.Text = "Luz lateral";
                ((GlassButton)form.Controls.Find("cancelVariableValues", true).Single()).PerformClick();
                Check(soft.Selected && variable.Value == soft.Value, "Cancelar conserva la última selección confirmada");
            }
            var reread = new Store(store.Folder);
            reread.Load(reread.Pathname);
            var saved = reread.Entries.Single(e => e.Id == entry.Id).Variables[0];
            Check(saved.Options.Select(o => o.Label).SequenceEqual(variable.Options.Select(o => o.Label))
                && saved.Options.Single(o => o.Selected).Label == soft.Label,
                "El orden arrastrado y la elección confirmada se conservan en disco tras cerrar");
            using (var form = window.CreateVariableValuesDialog(entry))
            {
                form.Show(window); Pump(40);
                var choice = (VariableChoiceStrip)form.Controls.Find("variableChoice0", true).Single();
                OptionButton(choice, null).PerformClick();
                ((TextBox)form.Controls.Find("variableValue0", true).Single()).Text = direct.Value;
                ((GlassButton)form.Controls.Find("confirmVariableValues", true).Single()).PerformClick();
                Check(variable.Value == direct.Value && !variable.Options.Any(option => option.Selected),
                    "Confirmar Libre recuerda ese modo aunque se escriba el mismo texto que una opción");
            }
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

        static Color CardFill(LibraryList list, Entry item, bool selected)
        {
            using (var bitmap = new Bitmap(list.ClientSize.Width, list.ItemHeight))
            using (var g = Graphics.FromImage(bitmap))
            {
                var args = new DrawItemEventArgs(g, list.Font, new Rectangle(Point.Empty, bitmap.Size),
                    list.Items.IndexOf(item), selected ? DrawItemState.Selected : DrawItemState.None);
                typeof(LibraryList).GetMethod("OnDrawItem", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(list, new object[] { args });
                return bitmap.GetPixel(bitmap.Width - 32, bitmap.Height / 2);
            }
        }

        static void EditModelColor(MainWindow window, string model, string control, string outputFolder = null)
        {
            Exception error = null;
            window.BeginInvoke((Action)delegate {
                var dialog = Application.OpenForms.Cast<Form>().Single(f => f.Text == "Gestionar modelos");
                try
                {
                    var choices = (CheckedListBox)dialog.Controls.Find("modelChoices", true).Single();
                    choices.SelectedItem = model;
                    var button = (IButtonControl)dialog.Controls.Find(control, true).Single();
                    button.PerformClick();
                    if (outputFolder != null) Capture(dialog, outputFolder, "elegir-color-del-modelo");
                }
                catch (Exception ex) { error = ex; }
                finally { dialog.Close(); }
            });
            Call(window, "ManageChoices", "model");
            if (error != null) throw error;
        }

        static void ModelColorCards(string folder, string outputFolder)
        {
            var store = new Store(folder);
            store.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
            string[] models = { "Banana Pro 2.1", "Para GPT Personal", "Wan 3.0" };
            string[] names = { "Cambiar el fondo", "Desarrollar una idea", "Movimiento de cámara" };
            string[] prompts = { "Iluminá el objeto desde la izquierda.", "Proponé tres variantes de esta idea.", "Hacé un recorrido de cámara suave." };
            for (int i = 0; i < models.Length; i++)
            {
                var entry = new Entry();
                entry.Set("collection", "Personales"); entry.Set("category", "Estudio");
                entry.Set("name", names[i]); entry.Set("model", models[i]); entry.Set("prompt", prompts[i]);
                store.Entries.Add(entry);
            }
            store.Save();
            using (var window = new MainWindow(store, true))
            {
                window.Show(); window.Size = new Size(1200, 760); Pump(70);
                var list = Field<LibraryList>(window, "library");
                var photo = store.Entries[0]; var chat = store.Entries[1]; var video = store.Entries[2];
                list.SelectedItem = photo;
                var gold = CardFill(list, photo, false); var blue = CardFill(list, chat, false);
                var purple = CardFill(list, video, false);
                Check(gold != blue && blue != purple && gold != purple, "La biblioteca distingue los tres modelos por el fondo de su tarjeta");
                Check(CardFill(list, photo, true) != gold && CardFill(list, photo, true) != CardFill(list, chat, true),
                    "Seleccionar una tarjeta conserva el color del modelo y distingue la selección");
                Capture(window, outputFolder, "colores-por-modelo");
                Field<GlassButton>(window, "tableModeButton").PerformClick(); Pump(60);
                var grid = Field<DataGridView>(window, "grid");
                int column = grid.Columns["model"].Index;
                grid.FirstDisplayedScrollingColumnIndex = column; Pump(60);
                using (var bitmap = new Bitmap(grid.Width, grid.Height))
                {
                    grid.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
                    foreach (DataGridViewRow row in grid.Rows)
                    {
                        var cell = grid.GetCellDisplayRectangle(column, row.Index, false);
                        var entry = (Entry)row.Tag;
                        var tint = ModelPalette.Surface(ModelPalette.For(store, entry.Get("model")), row.Selected);
                        Check(cell.Width > 32 && bitmap.GetPixel(cell.Right - 26, cell.Top + 6).ToArgb() == tint.ToArgb(),
                            "La celda Modelo de la tabla usa el color asignado a «" + entry.Get("model") + "»");
                    }
                }
                Field<GlassButton>(window, "listModeButton").PerformClick(); Pump(60);
                Check(ModelPalette.Automatic("Seedream 5.0 Pro") == ModelPalette.Automatic("Seedance") &&
                    ModelPalette.Automatic("Seedance") == ModelPalette.Automatic(models[0]) &&
                    ModelPalette.Automatic("One 3.0") == ModelPalette.Automatic(models[2]) &&
                    ModelPalette.Automatic("").IsEmpty, "Las variantes de nombre mantienen los colores previstos");
                Choose(Field<ComboBox>(window, "entryModel"), models[2]);
                Check(CardFill(list, photo, false) == purple, "Cambiar el modelo de un prompt actualiza su tarjeta");
                EditModelColor(window, models[2], "modelColorAzul", outputFolder);
                Check(CardFill(list, video, false) == CardFill(list, photo, false) &&
                    CardFill(list, video, false) != purple && store.ModelColors.ContainsKey(models[2]),
                    "Asignar un color desde el gestor cambia todas las tarjetas del modelo");
                EditModelColor(window, models[2], "modelColorAuto");
                Check(!store.ModelColors.ContainsKey(models[2]) && CardFill(list, video, false) == purple,
                    "Automático recupera el color original del modelo");
                const string renamed = "Mi modelo de movimiento";
                Call(window, "RenameChoice", "model", "", models[2], renamed);
                Check(video.Get("model") == renamed && CardFill(list, video, false) == purple,
                    "Renombrar un modelo conserva su color en la biblioteca");
                Call(window, "RemoveChoice", "model", renamed, null);
                Check(!store.ModelColors.ContainsKey(renamed) && video.Get("model") == "",
                    "Eliminar un modelo quita su asignación de color");
                Call(window, "UndoChange", false);
                var recovered = store.Entries.Single(e => e.Id == video.Id);
                Check(recovered.Get("model") == renamed && CardFill(list, recovered, false) == purple,
                    "Deshacer la eliminación recupera el modelo y su color");
                window.Close();
            }
        }

        static void FolderNavigation(string folder, string outputFolder)
        {
            var store = new Store(folder);
            store.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
            store.Categories.Add("Personales::Reels");
            store.Categories.Add("404::Proyectos");
            var landscape = CategoryFolders.Create(store, "404", "Proyectos", "Exteriores");
            var night = CategoryFolders.Create(store, "404", landscape, "Noche");
            var parentEntry = new Entry();
            parentEntry.Set("name", "Plano general"); parentEntry.Set("collection", "Personales");
            parentEntry.Set("category", "Reels"); parentEntry.Set("prompt", "Plano de estudio.");
            var otherEntry = new Entry();
            otherEntry.Set("name", "Estudio nocturno"); otherEntry.Set("collection", "404");
            otherEntry.Set("category", night); otherEntry.Set("prompt", "Escena exterior.");
            store.Entries.Add(parentEntry); store.Entries.Add(otherEntry);
            store.Save();
            using (var window = new MainWindow(store, true))
            {
                window.Show(); window.Size = new Size(1100, 700); Pump(80);
                var collection = Field<ComboBox>(window, "collection");
                var categories = Field<ListBox>(window, "categories");
                var library = Field<ListBox>(window, "library");
                collection.SelectedItem = "Personales";
                categories.SelectedItem = "Reels";
                Check(library.Items.Count == 1 && library.SelectedItem == parentEntry, "La carpeta madre muestra su prompt");

                Exception error = null;
                window.BeginInvoke((Action)delegate {
                    var dialog = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f != window && f.Text.Contains("subcarpeta"));
                    try
                    {
                        if (dialog == null) throw new Exception("No se abrió Nueva subcarpeta");
                        var input = (TextBox)dialog.Controls.Find("folderName", true).Single();
                        input.Text = "Accesorios";
                        dialog.AcceptButton.PerformClick();
                    }
                    catch (Exception ex) { error = ex; if (dialog != null) dialog.Close(); }
                });
                Call(window, "AddSubfolder");
                if (error != null) throw error;
                string child = "Reels" + CategoryFolders.Separator + "Accesorios";
                Check(categories.Text == child && library.Items.Count == 0, "Crear una subcarpeta vacía la abre sin mover los prompts de su madre");
                Call(window, "SetCategoryExpanded", "Reels", false);
                Check(!categories.Items.Contains(child), "Al plegar una carpeta se ocultan sus subcarpetas");
                Call(window, "SetCategoryExpanded", "Reels", true);
                categories.SelectedItem = child;
                Call(window, "AddEntry"); Pump(60);
                var created = Field<Entry>(window, "selected");
                Check(created.Get("category") == child && created.Get("collection") == "Personales",
                    "Nuevo prompt hereda la subcarpeta y la colección seleccionadas");
                categories.SelectedItem = "Reels";
                library.SelectedItem = parentEntry; Pump(50);
                Choose(Field<ComboBox>(window, "entryCategory"), child);
                categories.SelectedItem = child;
                Check(parentEntry.Get("category") == child && library.Items.Count == 2, "La ficha mueve un prompt a la subcarpeta");
                Capture(window, outputFolder, "subcarpetas");
                Field<GlassButton>(window, "tableModeButton").PerformClick(); Pump(50);
                Check(Field<DataGridView>(window, "grid").Rows.Count == 2, "La tabla muestra los prompts de la subcarpeta");
                Field<GlassButton>(window, "listModeButton").PerformClick(); Pump(50);
                collection.SelectedItem = "404"; categories.SelectedItem = night;
                library.SelectedItem = otherEntry; Call(window, "Duplicate"); Pump(50);
                var copy = Field<Entry>(window, "selected");
                Check(copy.Id != otherEntry.Id && copy.Get("collection") == "Personales" && copy.Get("category") == night &&
                    CategoryFolders.Parent(store, "Personales", night) == landscape &&
                    CategoryFolders.Parent(store, "Personales", landscape) == "Proyectos", "Duplicar entre colecciones conserva los niveles de carpeta");
                window.Close();
            }
            var reopened = new Store(folder);
            reopened.Load(reopened.Pathname);
            Check(CategoryFolders.Parent(reopened, "Personales", parentEntry.Get("category")) == "Reels" &&
                reopened.Entries.Single(e => e.Id == parentEntry.Id).Get("category") == parentEntry.Get("category"),
                "La subcarpeta y sus asignaciones persisten al reabrir");
        }

        static void ArchiveNavigation(string folder, string outputFolder)
        {
            var store = new Store(folder);
            store.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
            var target = new Entry { Favorite = true, LastUsed = DateTime.UtcNow.ToString("o"), Hotkey = "Alt+NumPad1" };
            target.Set("name", "Estudio de luz");
            target.Set("collection", "Personales");
            target.Set("category", "Fotografía");
            target.Set("prompt", "Detalle {{luz}}");
            target.Variables.Add(new Variable { Token = "{{luz}}", Value = "natural" });
            var archived404 = new Entry { Archived = true };
            archived404.Set("name", "Movimiento");
            archived404.Set("collection", "404");
            archived404.Set("category", "Video");
            archived404.Set("prompt", "Una toma continua con movimiento lateral.");
            var active = new Entry();
            active.Set("name", "Otro diseño");
            active.Set("collection", "Personales");
            active.Set("category", "Fotografía");
            active.Set("prompt", "Iluminación natural desde la izquierda.");
            store.Entries.AddRange(new[] { target, archived404, active });
            store.Save();
            string original = target.Xml().ToString();

            using (var window = new MainWindow(store, true))
            {
                window.Show(); window.Size = new Size(1100, 700); Pump(80);
                var nav = Field<Dictionary<string, GlassButton>>(window, "navigation");
                var menu = Field<ToolStripItem>(window, "archiveMenuItem");
                var library = Field<ListBox>(window, "library");
                var grid = Field<DataGridView>(window, "grid");
                var search = Field<TextBox>(window, "search");
                int normalItemHeight = library.ItemHeight;
                library.SelectedItem = target;
                Check(menu.Text == "Archivar prompt" && library.Items.Count == 2, "La biblioteca activa ofrece archivar el prompt seleccionado");
                menu.PerformClick();
                Wait(() => !library.Items.Contains(target) && library.Items.Count == 1, "Archivar oculta el prompt en la lista activa");
                var reopened = new Store(folder);
                reopened.Load(reopened.Pathname);
                Check(reopened.Entries.Count == 3 && reopened.Entries.Single(e => e.Id == target.Id).Archived, "El archivo persiste al reabrir el catálogo");
                nav["Favoritos"].PerformClick();
                Check(library.Items.Count == 0, "Un favorito archivado sale del acceso a favoritos");
                nav["Recientes"].PerformClick();
                Check(library.Items.Count == 0, "Un reciente archivado sale del acceso a recientes");
                nav["Todos"].PerformClick();
                Field<GlassButton>(window, "tableModeButton").PerformClick();
                Check(grid.Rows.Count == 1 && ((Entry)grid.Rows[0].Tag).Id == active.Id, "La tabla activa excluye los archivados");

                Field<ComboBox>(window, "collection").SelectedItem = "Personales";
                Field<ListBox>(window, "categories").SelectedItem = "Fotografía";
                var categoryArchive = Field<GlassButton>(window, "categoryArchiveButton");
                Visible(window, "categoryArchiveButton");
                var archiveBounds = categoryArchive.RectangleToScreen(categoryArchive.ClientRectangle);
                foreach (string mode in new[] { "listModeButton", "tableModeButton" })
                {
                    var button = Field<GlassButton>(window, mode);
                    Check(!archiveBounds.IntersectsWith(button.RectangleToScreen(button.ClientRectangle)),
                        "El acceso a los archivados de la categoría deja libre el selector de vista " + mode);
                }
                Capture(window, outputFolder, "categoria-con-archivados");
                search.Text = "Otro diseño";
                categoryArchive.PerformClick();
                Wait(() => grid.Rows.Count == 1 && ((Entry)grid.Rows[0].Tag).Id == target.Id,
                    "El acceso de la categoría recupera sus archivados incluso con una búsqueda previa");
                nav["Archivados"].PerformClick();
                Wait(() => grid.Rows.Count == 2, "El acceso a Archivados reúne ambas colecciones aunque hubiera filtros");
                Check(grid.Rows.Cast<DataGridViewRow>().Select(row => ((Entry)row.Tag).Id).OrderBy(id => id)
                    .SequenceEqual(new[] { target.Id, archived404.Id }.OrderBy(id => id)), "La carpeta reúne únicamente los prompts archivados");
                Field<GlassButton>(window, "listModeButton").PerformClick();
                Check(library.Items.Contains(target) && library.Items.Contains(archived404), "El archivo muestra ambos prompts también en Lista");
                Check(library.ItemHeight < normalItemHeight, "Las filas de Archivados usan menos espacio que las de la biblioteca activa");
                library.SelectedItem = target;
                Capture(window, outputFolder, "carpeta-archivados");
                Field<GlassButton>(window, "tableModeButton").PerformClick();
                Check(menu.Text == "Restaurar prompt" && Field<Entry>(window, "selected").Id == target.Id, "La tabla de archivados ofrece restaurar el prompt seleccionado");
                menu.PerformClick();
                Check(target.Xml().ToString() == original, "Restaurar conserva el registro original, su ubicación, variables, favorito y atajo");
                Call(window, "UndoChange", false);
                Check(store.Entries.Single(e => e.Id == target.Id).Archived, "Deshacer la restauración devuelve el prompt al archivo");
                Call(window, "UndoChange", true);
                nav["Favoritos"].PerformClick();
                Check(grid.Rows.Count == 1 && ((Entry)grid.Rows[0].Tag).Id == target.Id, "Rehacer la restauración recupera el favorito en su vista habitual");
                window.Close();
            }
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
                const string mixedEndings = "Título\n\nPrimer párrafo\rSegundo párrafo\r\nFin\\n";
                const string windowsEndings = "Título\r\n\r\nPrimer párrafo\r\nSegundo párrafo\r\nFin\\n";
                store.Entries[6].Set("prompt", mixedEndings);
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
                    var mixedEntry = store.Entries[6];
                    library.SelectedItem = mixedEntry; Pump(50);
                    Check(editor.Text == windowsEndings && editor.GetLineFromCharIndex(editor.Text.IndexOf("Primer")) == 2,
                        "El editor muestra separados los párrafos importados y conserva la línea en blanco");
                    Call(window, "Copy", mixedEntry);
                    using (var destination = new TextBox { Multiline = true })
                    {
                        destination.Paste();
                        Check(destination.Text == windowsEndings, "Pegar en otro control conserva las líneas, los blancos y la secuencia literal de barra y n");
                    }
                    Check(mixedEntry.Get("prompt") == mixedEndings, "Ver y copiar no reescribe el texto original");
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
                    Wait(() => library.Items.Count == 2, "Filtro de recientes");
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
                    VariableDialogs(window, store, outputFolder);
                    VariableEditorTests.Run(window);
                    window.Close();
                }

                ArchiveNavigation(Path.Combine(temp, "archivados"), outputFolder);
                FolderNavigation(Path.Combine(temp, "carpetas"), outputFolder);
                ModelColorCards(Path.Combine(temp, "colores"), outputFolder);
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
