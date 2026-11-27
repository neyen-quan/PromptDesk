using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace PromptDesk
{
    internal static class VariableEditor
    {
        internal static Form Create(Entry entry)
        {
            var form = UI.Dialog("Variables · " + entry.Get("name"), 820, 460);
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(new Label {
                AutoSize = true, Dock = DockStyle.Fill, Padding = new Padding(0, 2, 0, 9),
                Text = "Escribí el marcador tal como aparece en el prompt (por ejemplo, X o {{ropa}})."
            }, 0, 0);
            var grid = new DataGridView {
                Name = "VariableGrid", Dock = DockStyle.Fill, MultiSelect = false,
                AllowUserToAddRows = true, AllowUserToDeleteRows = true,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders
            };
            // Apply the padded theme before creating rows, including the new-record row.
            UI.GridTheme(grid);
            grid.RowTemplate.MinimumHeight = grid.DefaultCellStyle.Font.Height + grid.DefaultCellStyle.Padding.Vertical + 4;
            grid.Columns.Add("token", "Marcador exacto");
            grid.Columns.Add("label", "Nombre del campo");
            grid.Columns.Add("value", "Valor predeterminado");
            grid.Columns["value"].ToolTipText = "Al copiar, PromptDesk recuerda el último valor que elegiste.";
            grid.Columns.Add("options", "Opciones");
            grid.Columns["options"].ReadOnly = true;
            grid.Columns["options"].FillWeight = 48;
            foreach (DataGridViewColumn column in grid.Columns)
            {
                column.SortMode = DataGridViewColumnSortMode.NotSortable;
                column.HeaderCell.Style.WrapMode = DataGridViewTriState.True;
            }
            foreach (var variable in entry.Variables)
            {
                int i = grid.Rows.Add(variable.Token, variable.Label, variable.Value, variable.Options.Count);
                grid.Rows[i].Tag = new List<VariableOption>(variable.Options);
            }
            form.Shown += delegate {
                grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
                grid.AutoResizeRows(DataGridViewAutoSizeRowsMode.AllCellsExceptHeaders);
            };
            layout.Controls.Add(grid, 0, 1);
            var buttons = ButtonRow();
            var optionsButton = UI.Button("Editar opciones…", delegate {
                grid.EndEdit();
                var row = grid.CurrentRow;
                if (row == null || row.IsNewRow) return;
                var options = row.Tag as List<VariableOption> ?? new List<VariableOption>();
                string label = Convert.ToString(row.Cells["label"].Value);
                if (String.IsNullOrWhiteSpace(label)) label = Convert.ToString(row.Cells["token"].Value);
                using (var editor = CreateOptions(label, options))
                {
                    if (editor.ShowDialog(form) != DialogResult.OK) return;
                    row.Tag = options;
                    row.Cells["options"].Value = options.Count;
                }
            });
            optionsButton.Name = "editVariableOptions";
            Action refresh = delegate { optionsButton.Enabled = grid.CurrentRow != null && !grid.CurrentRow.IsNewRow; };
            grid.CurrentCellChanged += delegate { refresh(); };
            grid.UserAddedRow += delegate { refresh(); };
            refresh();
            var save = UI.Button("Guardar variables", delegate {
                grid.EndEdit();
                var result = new List<Variable>();
                foreach (DataGridViewRow row in grid.Rows)
                {
                    if (row.IsNewRow) continue;
                    string token = Convert.ToString(row.Cells["token"].Value);
                    if (token.Length == 0)
                    {
                        if (row.Tag != null || !String.IsNullOrEmpty(Convert.ToString(row.Cells["label"].Value))
                            || !String.IsNullOrEmpty(Convert.ToString(row.Cells["value"].Value)))
                            throw new Exception("Completá el marcador exacto de la fila " + (row.Index + 1) + ".");
                        continue;
                    }
                    if (result.Any(v => v.Token == token)) throw new Exception("Marcador repetido: " + token);
                    if (!Regex.IsMatch(entry.Get("prompt"), Templates.Pattern(token))) throw new Exception("No existe el marcador: " + token);
                    result.Add(new Variable {
                        Token = token, Label = Convert.ToString(row.Cells["label"].Value),
                        Value = Convert.ToString(row.Cells["value"].Value),
                        Options = row.Tag as List<VariableOption> ?? new List<VariableOption>()
                    });
                }
                entry.Variables = result;
                form.DialogResult = DialogResult.OK;
            });
            save.Name = "saveVariables";
            save.Primary = true;
            buttons.Controls.Add(optionsButton);
            buttons.Controls.Add(save);
            AddCancel(form, buttons);
            layout.Controls.Add(buttons, 0, 2);
            form.Controls.Add(layout);
            return form;
        }

        internal static Form CreateOptions(string field, List<VariableOption> options)
        {
            var form = UI.Dialog("Opciones · " + field, 740, 450);
            var drafts = options.Select(o => new VariableOption { Label = o.Label, Value = o.Value }).ToList();
            var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 3, Padding = new Padding(12) };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 32));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 68));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            layout.Controls.Add(Caption("Opciones al copiar"), 0, 0);
            layout.Controls.Add(Caption("Nombre de la opción"), 1, 0);
            var list = new ListBox { Name = "OptionList", Dock = DockStyle.Fill, IntegralHeight = false };
            UI.Style(list);
            layout.Controls.Add(list, 0, 1);
            var detail = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Margin = Padding.Empty };
            detail.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            detail.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            var name = new TextBox { Name = "OptionLabel", AccessibleName = "Nombre de la opción", Dock = DockStyle.Top };
            var value = new TextBox {
                Name = "OptionValue", AccessibleName = "Texto de reemplazo", Dock = DockStyle.Fill,
                Multiline = true, AcceptsReturn = true, ScrollBars = ScrollBars.Vertical
            };
            UI.Style(name);
            UI.Style(value);
            detail.Controls.Add(name, 0, 0);
            detail.Controls.Add(Caption("Texto de reemplazo"), 0, 1);
            detail.Controls.Add(value, 0, 2);
            layout.Controls.Add(detail, 1, 1);

            var actions = ButtonRow();
            Action add = delegate {
                drafts.Add(new VariableOption());
                list.SelectedIndex = list.Items.Add("(Sin nombre)");
                name.Focus();
            };
            var addButton = UI.Button("Agregar", add);
            addButton.Name = "addVariableOption";
            actions.Controls.Add(addButton);
            var remove = UI.Button("Eliminar", delegate {
                int i = list.SelectedIndex;
                if (i < 0) return;
                drafts.RemoveAt(i);
                list.Items.RemoveAt(i);
                list.SelectedIndex = Math.Min(i, list.Items.Count - 1);
            });
            remove.Name = "removeVariableOption";
            actions.Controls.Add(remove);
            bool updating = false;
            Action select = delegate {
                if (updating) return;
                int i = list.SelectedIndex;
                updating = true;
                name.Enabled = value.Enabled = remove.Enabled = i >= 0;
                name.Text = i < 0 ? "" : drafts[i].Label;
                value.Text = i < 0 ? "" : drafts[i].Value;
                updating = false;
            };
            list.SelectedIndexChanged += delegate { select(); };
            name.TextChanged += delegate {
                int i = list.SelectedIndex;
                if (updating || i < 0) return;
                updating = true;
                drafts[i].Label = name.Text;
                list.Items[i] = String.IsNullOrWhiteSpace(name.Text) ? "(Sin nombre)" : name.Text;
                updating = false;
            };
            value.TextChanged += delegate {
                if (!updating && list.SelectedIndex >= 0) drafts[list.SelectedIndex].Value = value.Text;
            };
            foreach (var draft in drafts) list.Items.Add(draft.Label);
            if (drafts.Count > 0) list.SelectedIndex = 0;
            else select();
            layout.Controls.Add(actions, 0, 2);
            var buttons = ButtonRow();
            var accept = UI.Button("Aceptar", delegate {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < drafts.Count; i++)
                {
                    var option = drafts[i];
                    option.Label = option.Label.Trim();
                    if (option.Label.Length > 0 && !String.IsNullOrWhiteSpace(option.Value) && names.Add(option.Label)) continue;
                    list.SelectedIndex = i;
                    throw new Exception("Cada opción necesita un nombre único y un texto de reemplazo.");
                }
                options.Clear();
                options.AddRange(drafts);
                form.DialogResult = DialogResult.OK;
            });
            accept.Name = "acceptVariableOptions";
            accept.Primary = true;
            buttons.Controls.Add(accept);
            AddCancel(form, buttons);
            layout.Controls.Add(buttons, 1, 2);
            form.Controls.Add(layout);
            return form;
        }

        static Label Caption(string title)
        {
            return new Label { Text = title, ForeColor = UI.Muted, AutoSize = true, Margin = new Padding(3, 5, 3, 4) };
        }

        static FlowLayoutPanel ButtonRow()
        {
            return new FlowLayoutPanel { AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Dock = DockStyle.Fill };
        }

        static void AddCancel(Form form, FlowLayoutPanel buttons)
        {
            var cancel = UI.Button("Cancelar", delegate { form.DialogResult = DialogResult.Cancel; });
            form.CancelButton = cancel;
            buttons.Controls.Add(cancel);
        }
    }
}
