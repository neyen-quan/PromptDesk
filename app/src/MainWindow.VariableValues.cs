using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk {
public partial class MainWindow {
    internal Form CreateVariableValuesDialog(Entry entry)
    {
        int height = 146 + entry.Variables.Sum(v => v.Options.Count > 0 ? 158 : 118);
        var form = UI.Dialog("Asignar valores · " + entry.Get("name"), 650, Math.Min(height, 720));
        form.TopMost = true;
        form.ShowInTaskbar = true;

        var scroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(16, 12, 16, 4) };
        var fields = new TableLayoutPanel {
            Dock = DockStyle.Top, ColumnCount = 1, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var readValues = new List<Func<string>>();
        for (int i = 0; i < entry.Variables.Count; i++)
            readValues.Add(AddVariableValueField(fields, entry.Variables[i], i));
        scroll.Controls.Add(fields);

        var buttons = Bar(58);
        buttons.Dock = DockStyle.Bottom;
        var confirm = UI.Button("Copiar con estos valores", () => {
            string[] values = readValues.Select(read => read()).ToArray();
            int missing = Array.FindIndex(values, String.IsNullOrWhiteSpace);
            if (missing >= 0) {
                MessageBox.Show(form, "Completá todos los valores.", "PromptDesk");
                fields.Controls["variableValue" + missing].Focus();
                return;
            }
            for (int i = 0; i < values.Length; i++)
                entry.Variables[i].Value = values[i];
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
        form.Shown += (sender, args) => {
            form.Activate();
            Control first = fields.Controls["variableChoice0"] ?? fields.Controls["variableValue0"];
            if (first != null) first.Focus();
        };
        return form;
    }

    Func<string> AddVariableValueField(TableLayoutPanel fields, Variable variable, int index)
    {
        string label = String.IsNullOrEmpty(variable.Label) ? variable.Token : variable.Label;
        fields.Controls.Add(new Label {
            Text = label, AutoSize = true, UseMnemonic = false,
            ForeColor = UI.Ink, Margin = new Padding(0, 4, 0, 5)
        });
        var value = new TextBox {
            Name = "variableValue" + index, AccessibleName = label, Dock = DockStyle.Top,
            Multiline = true, ScrollBars = ScrollBars.Vertical,
            Margin = new Padding(0, 0, 0, 12)
        };
        UI.Style(value);
        value.Height = value.Font.Height * 4 + 8;
        // Windows multiline controls display CRLF. Keep the stored text verbatim on untouched reads.
        string lastValue = variable.Value;
        string displayedLastValue = DisplayVariableValue(lastValue);
        if (variable.Options.Count == 0) {
            value.Text = displayedLastValue;
            fields.Controls.Add(value);
            return () => value.Text == displayedLastValue ? lastValue : value.Text;
        }

        var choices = Combo(250);
        choices.Name = "variableChoice" + index;
        choices.AccessibleName = label;
        choices.Dock = DockStyle.Top;
        choices.Margin = new Padding(0, 0, 0, 5);
        foreach (var option in variable.Options)
            choices.Items.Add(option.Label);
        int customIndex = choices.Items.Add("Texto libre");
        int previous = variable.Options.FindIndex(option => option.Value == lastValue);
        string customText = previous < 0 ? displayedLastValue : "";
        choices.SelectedIndexChanged += (sender, args) => {
            bool custom = choices.SelectedIndex == customIndex;
            if (!value.ReadOnly && !custom) customText = value.Text;
            value.ReadOnly = !custom;
            value.Text = custom ? customText : DisplayVariableValue(variable.Options[choices.SelectedIndex].Value);
            value.Select(0, 0);
        };
        fields.Controls.Add(choices);
        fields.Controls.Add(value);
        value.ReadOnly = true;
        if (previous >= 0) choices.SelectedIndex = previous;
        else choices.SelectedIndex = String.IsNullOrEmpty(lastValue) ? 0 : customIndex;
        return () => {
            if (choices.SelectedIndex != customIndex)
                return variable.Options[choices.SelectedIndex].Value;
            return value.Text == displayedLastValue ? lastValue : value.Text;
        };
    }

    static string DisplayVariableValue(string text)
    {
        return text.Replace("\r\n", "\n").Replace('\r', '\n').Replace("\n", Environment.NewLine);
    }
}
}
