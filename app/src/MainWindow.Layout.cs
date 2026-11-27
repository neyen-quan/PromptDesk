using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    // The workspace stays separate from catalogue editing and persistence.
    public partial class MainWindow
    {
        bool testing, tableMode;
        readonly IContainer uiComponents = new Container();
        readonly Dictionary<string, GlassButton> navigation = new Dictionary<string, GlassButton>();
        System.Windows.Forms.Timer searchTimer;
        LibraryList library;
        SplitContainer workspace;
        Panel listEmpty, libraryHost, editorHeader, editorFooter;
        GlassPanel editorCard;
        TableLayoutPanel editorMetadata;
        Label editorPlaceholder, editorEmptyTitle;
        Label emptyTitle, emptyHint, saveBadge, editorSummary;
        Label libraryHeading;
        GlassButton categoryArchiveButton;
        ToolStripItem archiveMenuItem;
        GlassButton emptyAction, copyButton, favoriteButton, listModeButton, tableModeButton, variablesButton, moreButton;
        TextBox entryTitle;
        ComboBox entryCollection, entryCategory, entryModel;
        ContextMenuStrip libraryMenu, organizeMenu, promptMenu;
        ToolTip tips;

        void Build()
        {
            SuspendLayout();
            tips = new ToolTip(uiComponents) { InitialDelay = 400 };
            searchTimer = new System.Windows.Forms.Timer(uiComponents) { Interval = 180 };
            searchTimer.Tick += (s, e) => RefreshRows();
            BuildMenus();
            var shell = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3,
                Padding = new Padding(18, 8, 18, 6), BackColor = UI.Bg
            };
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 194));
            shell.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
            shell.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            shell.RowStyles.Add(new RowStyle(SizeType.Absolute, 29));
            Controls.Add(shell);
            var header = BuildHeader();
            shell.Controls.Add(header, 0, 0); shell.SetColumnSpan(header, 2);
            shell.Controls.Add(BuildSidebar(), 0, 1);
            shell.Controls.Add(BuildWorkspace(), 1, 1);
            status = new Label
            {
                Dock = DockStyle.Fill, Text = "Todo guardado · biblioteca local", ForeColor = UI.Muted,
                Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true
            };
            var footer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 340));
            cloudStatus = new Label { Dock = DockStyle.Fill, Text = "Solo en este equipo · iniciar sesión", ForeColor = UI.Muted,
                Font = new Font("Segoe UI", 8.5f), TextAlign = ContentAlignment.MiddleRight, AutoEllipsis = true, Cursor = Cursors.Hand,
                AccessibleName = "Estado de sincronización" };
            cloudStatus.Click += (s, e) => OpenCloudAccount();
            footer.Controls.Add(status, 0, 0); footer.Controls.Add(cloudStatus, 1, 0);
            shell.Controls.Add(footer, 0, 2); shell.SetColumnSpan(footer, 2);
            WireGridEvents();
            WireLibraryEvents();
            ResumeLayout(true);
        }

        Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty };
            header.Controls.Add(new PictureBox
            {
                Image = Branding.Logo, SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(0, 5), Size = new Size(31, 31)
            });
            header.Controls.Add(new Label
            {
                Text = "PromptDesk", Font = new Font("Segoe UI", 14, FontStyle.Bold), ForeColor = UI.Ink,
                AutoSize = true, Location = new Point(41, 5)
            });
            header.Controls.Add(new Label
            {
                Text = "v" + Branding.Version, Font = new Font("Segoe UI", 8), ForeColor = UI.Muted,
                AutoSize = true, Location = new Point(189, 14)
            });
            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, WrapContents = false,
                FlowDirection = FlowDirection.RightToLeft, BackColor = Color.Transparent
            };
            var tools = UI.Button("Biblioteca", () => libraryMenu.Show(Cursor.Position));
            tools.IconName = "menu"; tools.Quiet = true;
            var create = UI.Button("Nuevo prompt", AddEntry);
            create.IconName = "plus"; create.Active = true;
            tips.SetToolTip(create, "Nuevo prompt · Ctrl+N");
            accountButton = UI.Button("Iniciar sesión", OpenCloudAccount);
            accountButton.Quiet = true; accountButton.AccessibleName = "Cuenta Programas 404";
            actions.Controls.Add(accountButton); actions.Controls.Add(tools); actions.Controls.Add(create);
            header.Controls.Add(actions);
            return header;
        }

        Control BuildSidebar()
        {
            var side = new Panel
            {
                Dock = DockStyle.Fill, Padding = new Padding(0, 14, 13, 0),
                Margin = new Padding(0, 0, 8, 0), BackColor = UI.Bg
            };
            var top = new Panel { Dock = DockStyle.Top, Height = 244 };
            top.Controls.Add(LabelAt("COLECCIÓN", 2, 0, 172, 23, 8, UI.Muted));
            collection = Combo(170); collection.Location = new Point(0, 28);
            collection.Items.AddRange(new object[] { "Todos", "Personales", "404" }); collection.SelectedIndex = 0;
            top.Controls.Add(collection);
            view = Combo(170); view.Items.AddRange(new object[] { "Todos", "Favoritos", "Recientes", "Archivados" }); view.SelectedIndex = 0;
            int y = 73;
            foreach (var item in new[] { "Todos", "Favoritos", "Recientes" })
            {
                string value = item;
                var button = UI.Button(item == "Todos" ? "Todos los prompts" : item, () => view.SelectedItem = value);
                button.IconName = item == "Todos" ? "library" : item == "Favoritos" ? "star" : "clock";
                button.Quiet = true; button.AutoSize = false; button.Size = new Size(172, 40);
                button.Location = new Point(0, y); button.TextAlign = ContentAlignment.MiddleLeft;
                top.Controls.Add(button); navigation[item] = button; y += 43;
            }
            var archive = UI.Button("Archivados", () => OpenArchive(false));
            archive.IconName = "archive"; archive.Quiet = true; archive.AutoSize = false;
            archive.Font = new Font("Segoe UI", 8.5f); archive.TextAlign = ContentAlignment.MiddleLeft;
            archive.SetBounds(8, y, 164, 31);
            tips.SetToolTip(archive, "Ver los archivados de todas las colecciones");
            top.Controls.Add(archive); navigation.Add("Archivados", archive);
            var bottom = new Panel { Dock = DockStyle.Bottom, Height = 113 };
            bottom.Controls.Add(LabelAt("FILTRAR POR MODELO", 2, 3, 172, 22, 8, UI.Muted));
            model = Combo(170); model.Location = new Point(0, 29); model.AccessibleName = "Filtrar por modelo";
            bottom.Controls.Add(model);
            var manage = UI.Button("Organizar", () => organizeMenu.Show(Cursor.Position));
            manage.IconName = "settings"; manage.Quiet = true; manage.AutoSize = false;
            manage.SetBounds(0, 70, 172, 36); manage.TextAlign = ContentAlignment.MiddleLeft;
            bottom.Controls.Add(manage);
            var categoryHeader = new Panel { Dock = DockStyle.Top, Height = 38 };
            categoryHeader.Controls.Add(LabelAt("CATEGORÍAS", 2, 7, 128, 23, 8, UI.Muted));
            var add = new GlassButton { IconName = "plus", Quiet = true, AutoSize = false, AccessibleName = "Crear categoría o subcarpeta" };
            add.SetBounds(140, 1, 32, 32);
            add.Click += (s, e) => categoryMenu.Show(add, new Point(0, add.Height));
            tips.SetToolTip(add, "Crear categoría o subcarpeta"); categoryHeader.Controls.Add(add);
            categories = new ListBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, IntegralHeight = false,
                BackColor = UI.Bg, ForeColor = UI.Ink, Font = new Font("Segoe UI", 9.5f), AccessibleName = "Categorías"
            };
            side.Controls.Add(categories); side.Controls.Add(categoryHeader);
            side.Controls.Add(bottom); side.Controls.Add(top);
            return side;
        }

        Control BuildWorkspace()
        {
            var content = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = UI.Bg };
            var searchRow = new Panel { Dock = DockStyle.Top, Height = 58, Padding = new Padding(0, 2, 0, 12) };
            var searchSurface = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(37, 10, 38, 8) };
            search = new TextBox
            {
                Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 10),
                BackColor = UI.Panel, ForeColor = UI.Ink, AccessibleName = "Buscar prompts"
            };
            var hint = new Label
            {
                Text = "Buscar por nombre o contenido…                         Ctrl+F", Dock = DockStyle.Fill,
                ForeColor = UI.Muted, BackColor = UI.Panel, Font = new Font("Segoe UI", 9.5f), AutoEllipsis = true
            };
            hint.Click += (s, e) => search.Focus();
            search.TextChanged += (s, e) => { hint.Visible = search.Text.Length == 0; if (!busy) { searchTimer.Stop(); searchTimer.Start(); } };
            search.GotFocus += (s, e) => hint.Visible = search.Text.Length == 0;
            search.LostFocus += (s, e) => hint.Visible = search.Text.Length == 0;
            searchSurface.Controls.Add(hint); searchSurface.Controls.Add(search); hint.BringToFront();
            searchSurface.Paint += (s, e) => Glyph.Draw(e.Graphics, "search", new Rectangle(12, 13, 18, 18), UI.Muted);
            var clear = UI.Button("", () => search.Clear()); clear.IconName = "close"; clear.Quiet = true;
            clear.AutoSize = false; clear.Size = new Size(29, 29); clear.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            clear.AccessibleName = "Limpiar búsqueda"; tips.SetToolTip(clear, "Limpiar búsqueda · Esc");
            searchSurface.Controls.Add(clear); searchSurface.Resize += (s, e) => clear.Location = new Point(searchSurface.Width - 35, 7);
            searchRow.Controls.Add(searchSurface);
            workspace = new SplitContainer
            {
                Dock = DockStyle.Fill, Orientation = Orientation.Vertical, SplitterWidth = 12,
                BackColor = UI.Bg, FixedPanel = FixedPanel.Panel1, Size = new Size(1100, 600)
            };
            workspace.SplitterDistance = 332; workspace.Panel1MinSize = 285; workspace.Panel2MinSize = 420;
            workspace.Panel1.Controls.Add(BuildLibrary()); workspace.Panel2.Controls.Add(BuildEditor());
            content.Controls.Add(workspace); content.Controls.Add(searchRow);
            return content;
        }

        Control BuildLibrary()
        {
            var card = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(10, 12, 10, 10) };
            var heading = new Panel { Dock = DockStyle.Top, Height = 73 };
            libraryHeading = LabelAt("Biblioteca", 4, 0, 135, 29, 12, UI.Ink, FontStyle.Bold);
            heading.Controls.Add(libraryHeading);
            var modes = new FlowLayoutPanel
            {
                Dock = DockStyle.Right, Width = 130, Height = 35, WrapContents = false, Margin = Padding.Empty
            };
            listModeButton = UI.Button("Lista", () => SwitchMode(false));
            tableModeButton = UI.Button("Tabla", () => SwitchMode(true));
            foreach (var button in new[] { listModeButton, tableModeButton })
            {
                button.AutoSize = false; button.Size = new Size(60, 32); button.Margin = new Padding(1, 0, 1, 0); button.Quiet = true;
                modes.Controls.Add(button);
            }
            listModeButton.Active = true; tips.SetToolTip(tableModeButton, "Vista de tabla · editar y ordenar columnas personalizadas");
            heading.Controls.Add(modes);
            resultCount = LabelAt("0 prompts", 4, 37, 260, 24, 9, UI.Muted);
            resultCount.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; heading.Controls.Add(resultCount);
            var host = libraryHost = new Panel { Dock = DockStyle.Fill, BackColor = UI.Panel };
            library = new LibraryList { Dock = DockStyle.Fill, AccessibleName = "Biblioteca de prompts" };
            grid = new DataGridView
            {
                Dock = DockStyle.Fill, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = true, RowHeadersVisible = false, MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect, AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
                EditMode = DataGridViewEditMode.EditProgrammatically, Visible = false
            };
            UI.GridTheme(grid);
            listEmpty = new Panel { Dock = DockStyle.Fill, BackColor = UI.Panel };
            var emptyStack = new TableLayoutPanel { Width = 255, Height = 250, ColumnCount = 1, RowCount = 4, BackColor = UI.Panel };
            emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 86)); emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 65)); emptyStack.RowStyles.Add(new RowStyle(SizeType.Absolute, 43));
            var drawing = new Panel { Dock = DockStyle.Fill };
            drawing.Paint += (s, e) => Glyph.EmptyLibrary(e.Graphics, drawing.ClientRectangle);
            emptyTitle = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 12, FontStyle.Bold), ForeColor = UI.Ink, TextAlign = ContentAlignment.MiddleCenter };
            emptyHint = new Label { Dock = DockStyle.Fill, Font = new Font("Segoe UI", 9.5f), ForeColor = UI.Muted, TextAlign = ContentAlignment.TopCenter };
            emptyAction = UI.Button("Crear mi primer prompt", () => {
                if (view.Text == "Archivados") OpenArchive(false);
                else if (ShowingFolderContents() || db.Entries.All(entry => entry.Archived)) AddEntry();
                else ResetFilters();
            });
            emptyAction.Active = true; emptyAction.Dock = DockStyle.Fill;
            emptyStack.Controls.Add(drawing); emptyStack.Controls.Add(emptyTitle); emptyStack.Controls.Add(emptyHint); emptyStack.Controls.Add(emptyAction);
            listEmpty.Controls.Add(emptyStack);
            listEmpty.Resize += (s, e) => emptyStack.Location = new Point(Math.Max(0, (listEmpty.Width - emptyStack.Width) / 2), Math.Max(4, (listEmpty.Height - emptyStack.Height) / 2));
            host.Controls.Add(library); host.Controls.Add(listEmpty); listEmpty.BringToFront();
            categoryArchiveButton = UI.Button("Archivados en esta categoría", () => OpenArchive(true));
            categoryArchiveButton.Dock = DockStyle.Bottom; categoryArchiveButton.AutoSize = false;
            categoryArchiveButton.Height = 32; categoryArchiveButton.IconName = "archive";
            categoryArchiveButton.TextAlign = ContentAlignment.MiddleLeft; categoryArchiveButton.Quiet = true;
            categoryArchiveButton.Font = new Font("Segoe UI", 8.5f); categoryArchiveButton.Visible = false;
            card.Controls.Add(host); card.Controls.Add(categoryArchiveButton); card.Controls.Add(heading);
            return card;
        }

        Control BuildEditor()
        {
            var card = editorCard = new GlassPanel { Dock = DockStyle.Fill, Padding = new Padding(20, 15, 20, 12) };
            var header = editorHeader = new Panel { Dock = DockStyle.Top, Height = 160 };
            details = LabelAt("PROMPT", 0, 0, 260, 20, 8, UI.Muted); header.Controls.Add(details);
            entryTitle = new TextBox
            {
                BorderStyle = BorderStyle.None, Font = new Font("Segoe UI", 16, FontStyle.Bold),
                BackColor = UI.Panel, ForeColor = UI.Ink, Location = new Point(0, 27), Height = 32,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right, AccessibleName = "Nombre del prompt"
            };
            favoriteButton = UI.Button("", ToggleFavorite); favoriteButton.IconName = "star"; favoriteButton.Quiet = true;
            favoriteButton.AutoSize = false; favoriteButton.Size = new Size(36, 36); favoriteButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            favoriteButton.AccessibleName = "Marcar favorito"; tips.SetToolTip(favoriteButton, "Marcar o quitar de favoritos");
            header.Controls.Add(entryTitle); header.Controls.Add(favoriteButton);
            editorEmptyTitle = LabelAt("Tu espacio para crear", 0, 27, 390, 35, 16, UI.Ink, FontStyle.Bold); editorEmptyTitle.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right; header.Controls.Add(editorEmptyTitle);
            header.Resize += (s, e) => { entryTitle.Width = Math.Max(60, header.Width - 44); favoriteButton.Location = new Point(header.Width - 36, tableMode ? 3 : 23); };
            var meta = editorMetadata = new TableLayoutPanel
            {
                Location = new Point(0, 74), Height = 68, Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                ColumnCount = 3, RowCount = 1, Padding = Padding.Empty, Margin = Padding.Empty
            };
            meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28)); meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36)); meta.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 36));
            entryCollection = Combo(110); entryCategory = Combo(130); entryModel = Combo(130);
            var values = new[] { Tuple.Create("COLECCIÓN", entryCollection), Tuple.Create("CATEGORÍA", entryCategory), Tuple.Create("MODELO", entryModel) };
            for (int index = 0; index < values.Length; index++)
            {
                var field = new Panel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, index == 2 ? 0 : 10, 0) };
                var caption = UI.Caption(values[index].Item1); caption.Font = new Font("Segoe UI", 7.5f, FontStyle.Bold); caption.Height = 24;
                var box = values[index].Item2; box.Dock = DockStyle.Top; box.Margin = Padding.Empty; box.Font = new Font("Segoe UI", 9);
                box.AccessibleName = values[index].Item1 + " del prompt";
                field.Controls.Add(box); field.Controls.Add(caption); meta.Controls.Add(field, index, 0);
            }
            header.Controls.Add(meta); header.Resize += (s, e) => meta.Width = header.Width;
            var textSurface = new Panel { Dock = DockStyle.Fill, Padding = new Padding(14, 14, 10, 10), BackColor = UI.Input };
            editor = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, AcceptsReturn = true, AcceptsTab = true,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, Font = new Font("Consolas", 10.5f),
                BackColor = UI.Input, ForeColor = UI.Ink, AccessibleName = "Contenido del prompt"
            };
            textSurface.Controls.Add(editor);
            editorPlaceholder = new Label { Dock = DockStyle.Fill, BackColor = UI.Input, ForeColor = UI.Muted, Font = new Font("Segoe UI", 11), TextAlign = ContentAlignment.MiddleCenter,
                Text = "Seleccioná un prompt de la biblioteca\npara leerlo, editarlo y copiarlo." };
            textSurface.Controls.Add(editorPlaceholder);
            var footer = editorFooter = new Panel { Dock = DockStyle.Bottom, Height = 81, BackColor = UI.Panel };
            editorSummary = LabelAt("Seleccioná o creá un prompt", 0, 5, 300, 25, 8.5f, UI.Muted);
            editorSummary.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            saveBadge = LabelAt("Guardado", 0, 5, 96, 25, 8.5f, UI.Muted); saveBadge.TextAlign = ContentAlignment.MiddleRight;
            footer.Controls.Add(editorSummary); footer.Controls.Add(saveBadge);
            var actions = new TableLayoutPanel { Dock = DockStyle.Bottom, Height = 43, ColumnCount = 3, RowCount = 1, Margin = Padding.Empty, Padding = Padding.Empty };
            actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 128)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100)); actions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 157));
            var variables = variablesButton = UI.Button("Variables", () => Variables(selected)); variables.IconName = "braces"; variables.Quiet = true; variables.Dock = DockStyle.Fill;
            var more = moreButton = UI.Button("Más", () => promptMenu.Show(Cursor.Position)); more.IconName = "menu"; more.Quiet = true; more.Dock = DockStyle.Left; more.Width = 72;
            copyButton = UI.Button("Copiar prompt", () => Copy(selected)); copyButton.IconName = "copy"; copyButton.Dock = DockStyle.Fill;
            tips.SetToolTip(copyButton, "Copiar el prompt completo · Ctrl+Shift+C");
            actions.Controls.Add(variables, 0, 0); actions.Controls.Add(more, 1, 0); actions.Controls.Add(copyButton, 2, 0);
            footer.Controls.Add(actions);
            footer.Resize += (s, e) => { saveBadge.Location = new Point(footer.Width - saveBadge.Width, tableMode ? 0 : 5); editorSummary.Width = Math.Max(40, footer.Width - saveBadge.Width - 10); };
            card.Controls.Add(textSurface); card.Controls.Add(header); card.Controls.Add(footer);
            return card;
        }

        static Label LabelAt(string text, int x, int y, int width, int height, float size, Color color, FontStyle style = FontStyle.Regular)
        {
            return new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), ForeColor = color, Font = new Font("Segoe UI", size, style), AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
        }

        ContextMenuStrip NewMenu()
        {
            return new ContextMenuStrip(uiComponents) { BackColor = UI.Panel, ForeColor = UI.Ink, Font = new Font("Segoe UI", 10), Renderer = new ToolStripProfessionalRenderer(new DarkMenuColors()), ShowImageMargin = false };
        }
        void MenuAction(ContextMenuStrip menu, string name, Action action) { menu.Items.Add(name, null, (s, e) => RunAction(action)); }
        void RunAction(Action action) { try { action(); } catch (Exception ex) { MessageBox.Show(this, ex.Message, "PromptDesk", MessageBoxButtons.OK, MessageBoxIcon.Error); } }
        void BuildMenus()
        {
            libraryMenu = NewMenu();
            MenuAction(libraryMenu, "Importar Excel…", Import); MenuAction(libraryMenu, "Exportar Excel…", Export);
            MenuAction(libraryMenu, "Respaldos e historial…", Backups); libraryMenu.Items.Add(new ToolStripSeparator());
            MenuAction(libraryMenu, "Deshacer                 Ctrl+Z", () => UndoChange(false)); MenuAction(libraryMenu, "Rehacer                    Ctrl+Y", () => UndoChange(true));
            libraryMenu.Items.Add(new ToolStripSeparator()); MenuAction(libraryMenu, "Ayuda y atajos", () => System.Diagnostics.Process.Start(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "LEEME.txt")));
            organizeMenu = NewMenu();
            AddFolderMenuItems(organizeMenu); MenuAction(organizeMenu, "Gestionar categorías…", () => ManageChoices("category"));
            categoryMenu = NewMenu(); categoryMenu.Name = "categoryMenu"; AddFolderMenuItems(categoryMenu);
            organizeMenu.Items.Add(new ToolStripSeparator()); MenuAction(organizeMenu, "Nuevo modelo…", AddModel); MenuAction(organizeMenu, "Gestionar modelos…", () => ManageChoices("model"));
            organizeMenu.Items.Add(new ToolStripSeparator()); MenuAction(organizeMenu, "Campos y columnas…", ManageFields); MenuAction(organizeMenu, "Crear campo personalizado…", AddColumn);
            promptMenu = NewMenu();
            MenuAction(promptMenu, "Editar ficha completa…", () => Edit(selected)); MenuAction(promptMenu, "Configurar variables…", () => Variables(selected));
            MenuAction(promptMenu, "Asignar atajo global…", () => Shortcut(selected)); MenuAction(promptMenu, "Duplicar                  Ctrl+D", Duplicate);
            archiveMenuItem = promptMenu.Items.Add("Archivar prompt", null, (s, e) => RunAction(ToggleArchive));
            promptMenu.Items.Add(new ToolStripSeparator()); MenuAction(promptMenu, "Eliminar prompt…", Delete);
            promptMenu.Opening += (s, e) => { foreach (ToolStripItem item in promptMenu.Items) item.Enabled = selected != null; };
        }

        void WireLibraryEvents()
        {
            collection.SelectedIndexChanged += (s, e) => { if (!busy) { RefreshFilters(); RefreshRows(); } };
            categories.SelectedIndexChanged += (s, e) => { if (!busy) RefreshRows(); };
            model.SelectedIndexChanged += (s, e) => { if (!busy) RefreshRows(); };
            view.SelectedIndexChanged += (s, e) => { if (!busy) RefreshRows(); };
            library.SelectedIndexChanged += (s, e) => { if (!busy) { selected = library.SelectedItem as Entry; ShowSelected(); } };
            library.DoubleClick += (s, e) => { if (selected != null) editor.Focus(); };
            library.ContextMenuStrip = promptMenu;
            library.MouseDown += (s, e) => { if (e.Button == MouseButtons.Right) { int index = library.IndexFromPoint(e.Location); if (index >= 0) library.SelectedIndex = index; } };
            library.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { RunAction(() => Copy(selected)); e.Handled = true; e.SuppressKeyPress = true; } };
            entryTitle.TextChanged += (s, e) => { if (!busy && selected != null) { selected.Set("name", entryTitle.Text); Changed("name:" + selected.Id); library.Invalidate(); } };
            entryCollection.SelectionChangeCommitted += (s, e) => CommitMetadata("collection", entryCollection);
            entryCategory.SelectionChangeCommitted += (s, e) => CommitMetadata("category", entryCategory);
            entryModel.SelectionChangeCommitted += (s, e) => CommitMetadata("model", entryModel);
        }

        void ShowSelected()
        {
            busy = true;
            bool hasEntry = selected != null;
            editor.Text = hasEntry ? PromptText.ForWindows(selected.Get("prompt")) : ""; editor.Enabled = hasEntry;
            entryTitle.Text = hasEntry ? selected.Get("name") : ""; entryTitle.Enabled = hasEntry;
            entryTitle.Visible = editor.Visible = hasEntry;
            editorMetadata.Visible = hasEntry && !tableMode;
            editorEmptyTitle.Visible = editorPlaceholder.Visible = !hasEntry;
            editorCard.Padding = tableMode ? new Padding(18, 8, 18, 8) : new Padding(20, 15, 20, 12);
            editorHeader.Height = tableMode ? 46 : hasEntry ? 160 : 84;
            editorFooter.Height = tableMode ? 68 : 81;
            entryTitle.Top = tableMode ? 6 : 27; editorEmptyTitle.Top = tableMode ? 5 : 27;
            details.Visible = !tableMode;
            favoriteButton.Top = tableMode ? 3 : 23;
            editorSummary.Top = saveBadge.Top = tableMode ? 0 : 5;
            editorSummary.Height = saveBadge.Height = tableMode ? 21 : 25;
            variablesButton.Enabled = moreButton.Enabled = hasEntry;
            archiveMenuItem.Text = hasEntry && selected.Archived ? "Restaurar prompt" : "Archivar prompt";
            details.Text = !hasEntry ? "TU ESPACIO DE TRABAJO" : selected.Archived ? "PROMPT ARCHIVADO" : "EDITANDO PROMPT";
            FillChoiceBox(entryCollection, "collection", "", hasEntry ? selected.Get("collection") : "Personales");
            FillChoiceBox(entryCategory, "category", hasEntry ? selected.Get("collection") : "Personales", hasEntry ? selected.Get("category") : "");
            FillChoiceBox(entryModel, "model", "", hasEntry ? selected.Get("model") : "");
            entryCollection.Enabled = entryCategory.Enabled = entryModel.Enabled = hasEntry;
            favoriteButton.Enabled = hasEntry; favoriteButton.Active = hasEntry && selected.Favorite; favoriteButton.Invalidate();
            busy = false;
            UpdateEditorSummary();
        }

        void UpdateEditorSummary()
        {
            bool hasEntry = selected != null;
            copyButton.Enabled = hasEntry && selected.Get("prompt").Length > 0;
            editorSummary.Text = hasEntry ? selected.Get("prompt").Length.ToString("N0") + " caracteres · " + selected.Variables.Count + " variables" : "Creá un prompt para comenzar";
            saveBadge.Text = dirty ? "Guardando…" : "Guardado";
            saveBadge.ForeColor = dirty ? UI.Accent : UI.Muted;
        }

        void UpdateNavigation()
        {
            foreach (var pair in navigation) { pair.Value.Active = view.Text == pair.Key; pair.Value.Invalidate(); }
            listModeButton.Active = !tableMode; tableModeButton.Active = tableMode; listModeButton.Invalidate(); tableModeButton.Invalidate();
        }

        void ToggleFavorite()
        {
            if (selected == null) return;
            selected.Favorite = !selected.Favorite; Changed(); RefreshRows();
        }

        void ToggleArchive()
        {
            var item = selected;
            if (item == null || !grid.EndEdit()) return;
            int index = library.Items.IndexOf(item);
            var next = index < 0 || library.Items.Count < 2 ? null :
                (Entry)library.Items[index + 1 < library.Items.Count ? index + 1 : index - 1];
            item.Archived = !item.Archived;
            Changed();
            bool saved = Save();
            if (item.Hotkey.Length > 0) RegisterKeys(!item.Archived);
            selected = next;
            RefreshRows();
            if (saved) status.Text = (item.Archived ? "Archivado" : "Restaurado") + " · " + item.Get("name");
        }

        void OpenArchive(bool currentCategory)
        {
            busy = true;
            view.SelectedItem = "Archivados";
            search.Clear();
            if (!currentCategory)
            {
                collection.SelectedIndex = 0;
                if (categories.Items.Count > 0) categories.SelectedIndex = 0;
            }
            if (model.Items.Count > 0) model.SelectedIndex = 0;
            busy = false;
            RefreshFilters(); RefreshRows();
        }

        void UpdateLibraryHeader(int visibleCount)
        {
            bool archived = view.Text == "Archivados";
            int archivedCount = db.Entries.Count(e => e.Archived);
            int total = archived ? archivedCount : db.Entries.Count - archivedCount;
            navigation["Archivados"].Text = archivedCount == 0 ? "Archivados" : "Archivados (" + archivedCount + ")";
            libraryHeading.Text = archived ? "Archivados" : "Biblioteca";
            resultCount.Text = visibleCount + " de " + total + " prompts";
            int categoryCount = !archived && categories.SelectedIndex > 0 ? db.Entries.Count(e => e.Archived &&
                e.Get("category") == categories.Text && (collection.Text == "Todos" || e.Get("collection") == collection.Text)) : 0;
            categoryArchiveButton.Text = "Archivados en esta categoría (" + categoryCount + ")";
            categoryArchiveButton.Visible = categoryCount > 0;
            listEmpty.Visible = visibleCount == 0;
            if (visibleCount > 0) return;
            if (archived && total == 0)
            {
                emptyTitle.Text = "Sin prompts archivados";
                emptyHint.Text = "Para guardar un prompt acá, abrí su menú Más y elegí Archivar prompt.";
                emptyAction.Visible = false;
            }
            else if (ShowingFolderContents())
            {
                emptyTitle.Text = "Esta carpeta está vacía";
                emptyHint.Text = "Creá un prompt acá o elegí esta carpeta en la ficha de un prompt existente.";
                emptyAction.Text = "Nuevo prompt aquí";
                emptyAction.Visible = true;
            }
            else if (total == 0)
            {
                emptyTitle.Text = archivedCount > 0 ? "Sin prompts activos" : "Tu biblioteca empieza acá";
                emptyHint.Text = archivedCount > 0 ? "Podés recuperar un prompt desde Archivados." : "Guardá tus ideas, ordenalas por categoría\ny reutilizalas en un clic.";
                emptyAction.Text = archivedCount > 0 ? "Crear un prompt" : "Crear mi primer prompt";
                emptyAction.Visible = true;
            }
            else
            {
                emptyTitle.Text = "No encontramos ese prompt";
                emptyHint.Text = "Probá otra búsqueda o quitá los filtros.";
                emptyAction.Text = "Limpiar filtros";
                emptyAction.Visible = true;
            }
        }

        void CommitMetadata(string key, ComboBox box)
        {
            if (busy || selected == null) return;
            var item = selected;
            string value = box.Text;
            if (value == NewChoice)
            {
                value = CreateChoice(this, key, item.Get("collection"));
                if (value == null) { ShowSelected(); return; }
            }
            SetEntryChoice(item, key, value);
            if (key != "collection") RegisterChoice(key, item.Get("collection"), value);
            Changed(); RevealEntry(item);
        }

        void RevealEntry(Entry item)
        {
            string path = item.Get("category");
            bool showFolder = categories.SelectedIndex > 0 || CategoryFolders.Parent(db, item.Get("collection"), path) != null;
            busy = true;
            search.Clear(); view.SelectedItem = item.Archived ? "Archivados" : "Todos"; collection.SelectedItem = item.Get("collection");
            categories.SelectedIndex = categories.Items.Count > 0 ? 0 : -1;
            model.SelectedIndex = model.Items.Count > 0 ? 0 : -1;
            selected = item; busy = false;
            if (showFolder) ExpandCategoryAncestors(item.Get("collection"), path);
            RefreshAll();
            if (showFolder && path.Length > 0 && categories.Items.Contains(path)) categories.SelectedItem = path;
        }

        void ResetFilters()
        {
            busy = true; search.Clear(); collection.SelectedIndex = 0; view.SelectedIndex = 0;
            categories.SelectedIndex = categories.Items.Count > 0 ? 0 : -1; model.SelectedIndex = model.Items.Count > 0 ? 0 : -1;
            busy = false; RefreshFilters(); RefreshRows(); search.Focus();
        }

        void SwitchMode(bool useTable)
        {
            if (tableMode == useTable) return;
            grid.EndEdit(); tableMode = useTable;
            workspace.SuspendLayout();
            workspace.Panel1MinSize = 0; workspace.Panel2MinSize = 0;
            workspace.Orientation = useTable ? Orientation.Horizontal : Orientation.Vertical;
            workspace.FixedPanel = FixedPanel.Panel1;
            if (useTable)
            {
                workspace.SplitterDistance = Math.Max(230, workspace.Height - 310);
                workspace.Panel1MinSize = 230; workspace.Panel2MinSize = 210;
            }
            else
            {
                workspace.SplitterDistance = Math.Min(340, Math.Max(285, workspace.Width - 432));
                workspace.Panel1MinSize = 285; workspace.Panel2MinSize = 420;
            }
            if(useTable){libraryHost.Controls.Remove(library);libraryHost.Controls.Add(grid);grid.Visible=true;}
            else{libraryHost.Controls.Remove(grid);libraryHost.Controls.Add(library);library.Visible=true;}
            listEmpty.BringToFront();
            workspace.ResumeLayout(true); RefreshRows();
        }
    }
}
