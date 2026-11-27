using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    public partial class MainWindow
    {
        ContextMenuStrip categoryMenu;
        readonly HashSet<string> collapsedCategories = new HashSet<string>();
        readonly Dictionary<string, int> categoryDepth = new Dictionary<string, int>();
        readonly Dictionary<string, string> categoryParents = new Dictionary<string, string>();
        readonly HashSet<string> categoryBranches = new HashSet<string>();
        readonly Dictionary<string, List<string>> categoryScopes = new Dictionary<string, List<string>>();

        void AddFolderMenuItems(ContextMenuStrip menu)
        {
            MenuAction(menu, "Nueva categoría…", AddCategory);
            MenuAction(menu, "Nueva subcarpeta…", AddSubfolder);
            var createChild = menu.Items[menu.Items.Count - 1];
            menu.Opening += (s, e) => createChild.Enabled = categories.SelectedIndex > 0;
        }

        string CategoryCreationScope()
        {
            if (collection.Text != "Todos") return collection.Text;
            List<string> scopes;
            if (categories.SelectedIndex > 0 && categoryScopes.TryGetValue(categories.Text, out scopes))
            {
                if (selected != null && scopes.Contains(selected.Get("collection"))) return selected.Get("collection");
                return scopes[0];
            }
            return "Personales";
        }

        void AddSubfolder()
        {
            if (categories.SelectedIndex <= 0) return;
            string parent = categories.Text;
            string scope = CategoryCreationScope();
            List<string> scopes = categoryScopes[parent];
            using (var form = UI.Dialog("Nueva subcarpeta en " + parent, 490, 260))
            {
                var fields = new FlowLayoutPanel {
                    Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
                    Padding = new Padding(14, 12, 14, 0), AutoScroll = true
                };
                ComboBox scopeBox = null;
                if (scopes.Count > 1)
                {
                    fields.Controls.Add(new Label { Text = "Colección", AutoSize = true });
                    scopeBox = Combo(220);
                    scopeBox.Items.AddRange(scopes.ToArray());
                    scopeBox.SelectedItem = scope;
                    fields.Controls.Add(scopeBox);
                }
                fields.Controls.Add(new Label { Text = "Nombre de la subcarpeta", AutoSize = true });
                var name = new TextBox { Name = "folderName", Width = 335 };
                UI.Style(name); fields.Controls.Add(name);
                var buttons = Bar(50); buttons.Dock = DockStyle.Bottom;
                var create = UI.Button("Crear", () => {
                    if (scopeBox != null) scope = scopeBox.Text;
                    string path = CategoryFolders.Create(db, scope, parent, name.Text);
                    Changed(); Save();
                    busy = true;
                    collection.SelectedItem = scope; view.SelectedItem = "Todos"; search.Clear();
                    if (model.Items.Count > 0) model.SelectedIndex = 0;
                    busy = false;
                    ExpandCategoryAncestors(scope, path);
                    RefreshAll(); categories.SelectedItem = path;
                    form.DialogResult = DialogResult.OK;
                });
                var cancel = UI.Button("Cancelar", () => form.DialogResult = DialogResult.Cancel);
                buttons.Controls.Add(create); buttons.Controls.Add(cancel);
                form.AcceptButton = create; form.CancelButton = cancel;
                form.Controls.Add(fields); form.Controls.Add(buttons);
                form.Shown += (s, e) => name.Focus();
                form.ShowDialog(this);
            }
        }

        bool ShowingFolderContents()
        {
            return categories.SelectedIndex > 0 && view.Text == "Todos" && search.Text.Length == 0 && model.SelectedIndex == 0;
        }

        void ExpandCategoryAncestors(string scope, string path)
        {
            for (string parent = CategoryFolders.Parent(db, scope, path); parent != null; parent = CategoryFolders.Parent(db, scope, parent))
                collapsedCategories.Remove(scope + "::" + parent);
        }

        void RefreshFilters()
        {
            busy = true;
            string oldCategory = Convert.ToString(categories.SelectedItem);
            string oldModel = Convert.ToString(model.SelectedItem);
            var scopes = collection.Text == "Todos" ? new[] { "Personales", "404" } : new[] { collection.Text };
            var paths = new List<string>();
            categoryParents.Clear(); categoryScopes.Clear(); categoryBranches.Clear(); categoryDepth.Clear();
            foreach (string scope in scopes)
            {
                foreach (string path in CatalogValues("category", scope).Where(p => ChoiceEnabled("category", scope, p)))
                {
                    if (!categoryScopes.ContainsKey(path))
                    {
                        categoryScopes[path] = new List<string>();
                        paths.Add(path);
                    }
                    categoryScopes[path].Add(scope);
                    string parent = CategoryFolders.Parent(db, scope, path);
                    if (parent != null) categoryParents[path] = parent;
                }
            }
            var tree = paths.ToLookup(path => {
                string parent;
                return categoryParents.TryGetValue(path, out parent) && categoryScopes.ContainsKey(parent) ? parent : "";
            });
            foreach (var group in tree) if (group.Key.Length > 0) categoryBranches.Add(group.Key);
            categories.BeginUpdate();
            try
            {
                categories.Items.Clear();
                categories.Items.Add("Todas las categorías");
                AppendCategoryRows(tree, "", 0);
                categories.SelectedItem = oldCategory;
                if (categories.SelectedIndex < 0) categories.SelectedIndex = 0;
            }
            finally { categories.EndUpdate(); }
            model.Items.Clear(); model.Items.Add("Todos los modelos");
            foreach (string m in CatalogValues("model").Where(m => ChoiceEnabled("model", "", m))) model.Items.Add(m);
            model.SelectedItem = oldModel;
            if (model.SelectedIndex < 0) model.SelectedIndex = 0;
            busy = false;
        }

        void AppendCategoryRows(ILookup<string, string> tree, string parent, int depth)
        {
            foreach (string path in tree[parent])
            {
                categoryDepth[path] = depth;
                categories.Items.Add(path);
                if (!collapsedCategories.Contains(collection.Text + "::" + path))
                    AppendCategoryRows(tree, path, depth + 1);
            }
        }

        void SetCategoryExpanded(string path, bool expanded)
        {
            string key = collection.Text + "::" + path;
            if (expanded ? !collapsedCategories.Remove(key) : !collapsedCategories.Add(key)) return;
            RefreshFilters();
            categories.SelectedItem = path;
            RefreshRows();
        }

        int CategoryIndent(string path)
        {
            int depth;
            return categoryDepth.TryGetValue(path, out depth) ? Math.Min(depth, 5) * 13 : 0;
        }

        void WireCategoryNavigation()
        {
            categories.ContextMenuStrip = categoryMenu;
            categories.DrawMode = DrawMode.OwnerDrawFixed;
            categories.ItemHeight = 37;
            categories.DrawItem += DrawCategory;
            categories.MouseDown += (s, e) => {
                int row = categories.IndexFromPoint(e.Location);
                if (row < 0) return;
                categories.SelectedIndex = row;
                string path = categories.Text;
                if (e.Button == MouseButtons.Left && categoryBranches.Contains(path) && e.X <= CategoryIndent(path) + 23)
                    SetCategoryExpanded(path, collapsedCategories.Contains(collection.Text + "::" + path));
            };
            categories.KeyDown += (s, e) => {
                if (categories.SelectedIndex <= 0 || (e.KeyCode != Keys.Left && e.KeyCode != Keys.Right)) return;
                string path = categories.Text;
                bool closed = collapsedCategories.Contains(collection.Text + "::" + path);
                if (categoryBranches.Contains(path) && ((e.KeyCode == Keys.Right) == closed))
                    SetCategoryExpanded(path, closed);
                else if (e.KeyCode == Keys.Left && categoryParents.ContainsKey(path)) categories.SelectedItem = categoryParents[path];
                else if (e.KeyCode == Keys.Right && categoryBranches.Contains(path)) categories.SelectedIndex++;
                e.SuppressKeyPress = true;
            };
            categories.MouseMove += (s, e) => {
                int row = categories.IndexFromPoint(e.Location);
                string path = row <= 0 ? "" : (string)categories.Items[row];
                if (tips.GetToolTip(categories) != path) tips.SetToolTip(categories, path);
            };
        }

        void DrawCategory(object sender, DrawItemEventArgs args)
        {
            if (args.Index < 0 || args.Index >= categories.Items.Count) return;
            bool active = (args.State & DrawItemState.Selected) != 0;
            using (var background = new SolidBrush(UI.Input)) args.Graphics.FillRectangle(background, args.Bounds);
            if (active)
            {
                var highlight = new Rectangle(args.Bounds.X + 2, args.Bounds.Y + 3, args.Bounds.Width - 4, args.Bounds.Height - 6);
                using (var outline = Glass.Round(highlight, 7))
                using (var fill = new SolidBrush(UI.Selected)) args.Graphics.FillPath(fill, outline);
                using (var accent = new SolidBrush(UI.Accent)) args.Graphics.FillRectangle(accent, args.Bounds.X + 2, args.Bounds.Y + 10, 3, args.Bounds.Height - 20);
            }
            string path = (string)categories.Items[args.Index];
            int indent = CategoryIndent(path);
            int x = args.Bounds.Left + 10 + indent;
            bool isBranch = args.Index > 0 && categoryBranches.Contains(path);
            string parent;
            string label = indent > 0 && categoryParents.TryGetValue(path, out parent) ? path.Substring(parent.Length + CategoryFolders.Separator.Length) : path;
            if (isBranch)
            {
                int y = args.Bounds.Top + args.Bounds.Height / 2;
                bool closed = collapsedCategories.Contains(collection.Text + "::" + path);
                var points = closed ? new[] { new Point(x + 2,y - 4), new Point(x + 6,y), new Point(x + 2,y + 4) }
                    : new[] { new Point(x,y - 2), new Point(x + 4,y + 2), new Point(x + 8,y - 2) };
                using (var pen = new Pen(active ? UI.Accent : UI.Muted, 1.5f)) args.Graphics.DrawLines(pen, points);
            }
            x += args.Index > 0 ? 13 : 4;
            TextRenderer.DrawText(args.Graphics, label, categories.Font,
                new Rectangle(x, args.Bounds.Top, Math.Max(1, args.Bounds.Right - x - 3), args.Bounds.Height),
                active ? UI.Accent : UI.Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }
}
