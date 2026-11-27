using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    public partial class MainWindow
    {
        internal Form CreateVariableValuesDialog(Entry entry)
        {
            int height = Math.Min(720, 156 + entry.Variables.Count * 175);
            var form = UI.Dialog("Asignar valores · " + entry.Get("name"), 690, height);
            form.TopMost = true;
            form.ShowInTaskbar = true;

            var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 4) };
            var fields = new TableLayoutPanel {
                Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var inputs = new List<VariableValueInput>();
            foreach (Variable variable in entry.Variables)
            {
                var input = new VariableValueInput(fields, variable, inputs.Count, () => { Changed(); Save(); });
                inputs.Add(input);
            }
            scroll.Controls.Add(fields);

            var buttons = Bar(58);
            buttons.Dock = DockStyle.Bottom;
            var confirm = UI.Button("Copiar con estos valores", () => {
                var missing = inputs.FirstOrDefault(input => String.IsNullOrWhiteSpace(input.Value));
                if (missing != null)
                {
                    MessageBox.Show(form, "Completá todos los valores.", "PromptDesk");
                    missing.FocusInput();
                    return;
                }
                foreach (var input in inputs) input.Confirm();
                form.DialogResult = DialogResult.OK;
            });
            confirm.Name = "confirmVariableValues";
            confirm.Primary = true;
            var cancel = UI.Button("Cancelar", () => form.DialogResult = DialogResult.Cancel);
            cancel.Name = "cancelVariableValues";
            buttons.Controls.Add(confirm);
            buttons.Controls.Add(cancel);
            form.Controls.Add(scroll);
            form.Controls.Add(buttons);
            form.AcceptButton = confirm;
            form.CancelButton = cancel;
            form.Shown += delegate {
                form.Activate();
                if (inputs.Count > 0) inputs[0].FocusInput();
            };
            return form;
        }

        sealed class VariableValueInput
        {
            readonly Variable variable;
            readonly VariableChoiceStrip choices;
            readonly TextBox editor;
            readonly string originalFreeText;
            string draft;

            internal VariableValueInput(TableLayoutPanel fields, Variable variable, int index, Action saveOrder)
            {
                this.variable = variable;
                string label = String.IsNullOrEmpty(variable.Label) ? variable.Token : variable.Label;
                fields.Controls.Add(new Label {
                    Text = label, AutoSize = true, UseMnemonic = false,
                    ForeColor = UI.Ink, Margin = new Padding(0, 4, 0, 6)
                });
                editor = new TextBox {
                    Name = "variableValue" + index, AccessibleName = label, Dock = DockStyle.Top,
                    Multiline = true, ScrollBars = ScrollBars.Vertical, Margin = new Padding(0, 0, 0, 12)
                };
                UI.Style(editor);
                editor.Height = editor.Font.Height * 4 + 8;
                var previous = variable.Options.FirstOrDefault(option => option.Selected);
                originalFreeText = previous == null ? variable.Value : "";
                draft = PromptText.ForWindows(originalFreeText);
                editor.Text = draft;
                choices = new VariableChoiceStrip(variable, ShowChoice, saveOrder) {
                    Name = "variableChoice" + index, AccessibleName = label,
                    Dock = DockStyle.Top, Margin = new Padding(0, 0, 0, 6)
                };
                fields.Controls.Add(choices);
                fields.Controls.Add(editor);
                choices.SelectOption(previous);
            }

            void ShowChoice(VariableOption option)
            {
                if (!editor.ReadOnly) draft = editor.Text;
                editor.ReadOnly = option != null;
                editor.Text = option == null ? draft : PromptText.ForWindows(option.Value);
                editor.Select(0, 0);
                if (option == null && editor.Visible) editor.Focus();
            }

            internal void FocusInput()
            {
                if (editor.ReadOnly)
                    choices.FocusSelected();
                else
                {
                    editor.Focus();
                    editor.SelectAll();
                }
            }

            internal string Value
            {
                get
                {
                    if (choices.SelectedOption != null) return choices.SelectedOption.Value;
                    // Preserve the original line endings when the displayed text was left untouched.
                    return editor.Text == PromptText.ForWindows(originalFreeText) ? originalFreeText : editor.Text;
                }
            }

            internal void Confirm()
            {
                variable.Value = Value;
                foreach (var option in variable.Options) option.Selected = option == choices.SelectedOption;
            }
        }
    }
}
