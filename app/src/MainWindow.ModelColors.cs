using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    public partial class MainWindow
    {
        static string ModelColorHex(Color color)
        {
            return String.Format("#{0:X2}{1:X2}{2:X2}", color.R, color.G, color.B);
        }

        void SetModelColor(string modelName, Color color)
        {
            if (String.IsNullOrEmpty(modelName)) return;
            if (color.IsEmpty)
            {
                if (!db.ModelColors.Remove(modelName)) return;
            }
            else
            {
                string hex = ModelColorHex(color), previous;
                if (db.ModelColors.TryGetValue(modelName, out previous) && String.Equals(previous, hex, StringComparison.OrdinalIgnoreCase)) return;
                db.ModelColors[modelName] = hex;
                // Imported prompts can be the only place a model is named.
                if (!db.Models.Contains(modelName)) db.Models.Add(modelName);
            }
            Changed();
            Save();
            RefreshRows();
        }

        Control ModelColorPicker(Form owner, CheckedListBox list)
        {
            var panel = new TableLayoutPanel {
                ColumnCount = 1, RowCount = 2, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Dock = DockStyle.Bottom, BackColor = UI.Panel, Padding = new Padding(12, 8, 12, 4)
            };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            var header = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = false, Margin = Padding.Empty };
            var sample = new Panel { Size = new Size(14, 14), Margin = new Padding(2, 4, 6, 3) };
            var label = new Label { Text = "Color de la tarjeta", AutoSize = true, Margin = new Padding(0, 2, 0, 3), ForeColor = UI.Ink };
            header.Controls.Add(sample);
            header.Controls.Add(label);
            panel.Controls.Add(header, 0, 0);

            var colors = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = Padding.Empty };
            panel.Controls.Add(colors, 0, 1);
            var auto = UI.Button("Automático", () => SetModelColor(list.SelectedItem as string, Color.Empty));
            auto.Name = "modelColorAuto";
            var custom = UI.Button("Otro color…", delegate {
                string modelName = list.SelectedItem as string;
                if (modelName == null) return;
                using (var dialog = new ColorDialog { Color = ModelPalette.For(db, modelName), FullOpen = true, SolidColorOnly = true })
                {
                    if (dialog.ShowDialog(owner) == DialogResult.OK) SetModelColor(modelName, dialog.Color);
                }
            });
            custom.Name = "modelColorCustom";
            Action refresh = delegate {
                string modelName = list.SelectedItem as string;
                bool hasModel = modelName != null;
                bool automatic = hasModel && !db.ModelColors.ContainsKey(modelName);
                Color current = hasModel ? ModelPalette.For(db, modelName) : UI.Line;
                colors.Enabled = hasModel;
                sample.BackColor = current;
                auto.Active = automatic;
                auto.Invalidate();
                foreach (var button in colors.Controls.OfType<Button>())
                    button.Text = hasModel && ((Color)button.Tag).ToArgb() == current.ToArgb() ? "✓" : "";
            };
            foreach (var preset in ModelPalette.Presets)
            {
                Color color = preset.Value;
                var button = new Button {
                    Name = "modelColor" + preset.Key, AccessibleName = preset.Key, Tag = color, Size = new Size(31, 30),
                    FlatStyle = FlatStyle.Flat, UseVisualStyleBackColor = false, BackColor = color, ForeColor = UI.Bg,
                    Margin = new Padding(2, 8, 2, 4), Cursor = Cursors.Hand, TabStop = true
                };
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = UI.Line;
                button.FlatAppearance.MouseOverBackColor = color;
                button.FlatAppearance.MouseDownBackColor = color;
                tips.SetToolTip(button, preset.Key);
                button.Click += delegate { SetModelColor(list.SelectedItem as string, color); refresh(); };
                colors.Controls.Add(button);
            }
            colors.Controls.Add(custom);
            colors.Controls.Add(auto);
            auto.Click += delegate { refresh(); };
            custom.Click += delegate { refresh(); };
            list.SelectedIndexChanged += delegate { refresh(); };
            refresh();
            return panel;
        }
    }
}
