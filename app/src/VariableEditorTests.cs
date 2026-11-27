using System;
using System.Linq;
using System.Windows.Forms;

namespace PromptDesk
{
    internal static class VariableEditorTests
    {
        internal static void Run(Form owner)
        {
            var entry = new Entry();
            entry.Set("prompt", "Foto {{color}}");
            var variable = new Variable { Token = "{{color}}", Label = "Color", Value = "Gris claro" };
            variable.Options.Add(new VariableOption { Label = "Gris", Value = "Gris claro", Selected = true });
            entry.Variables.Add(variable);
            const string multiline = "Rosa suave.\r\nFondo de estudio.";

            // Accepting the option editor only changes the current variable draft.
            using (var variables = VariableEditor.Create(entry))
            {
                variables.Show(owner);
                EditOptions(variables, delegate(Form options) {
                    AddOption(options, "Temporal", multiline);
                    Click(options, "acceptVariableOptions");
                });
                Check(OptionCount(variables) == 2, "Aceptar debe actualizar la cantidad en la fila.");
                Find<DataGridView>(variables, "VariableGrid").Rows[0].Cells["value"].Value = "Valor descartado";
                variables.CancelButton.PerformClick();
            }
            Check(variable.Options.Count == 1 && entry.Variables[0] == variable && variable.Options[0].Selected,
                "Cancelar el editor de variables debe conservar la configuración original.");

            using (var variables = VariableEditor.Create(entry))
            {
                variables.Show(owner);
                EditOptions(variables, delegate(Form options) {
                    Find<TextBox>(options, "OptionValue").Text = "Edición descartada";
                    Click(options, "removeVariableOption");
                    options.CancelButton.PerformClick();
                });
                EditOptions(variables, delegate(Form options) {
                    Check(Find<ListBox>(options, "OptionList").Items.Count == 1
                        && Find<TextBox>(options, "OptionValue").Text == "Gris claro",
                        "Cancelar opciones debe descartar tanto las ediciones como las eliminaciones.");
                    Find<TextBox>(options, "OptionLabel").Text = "Gris cálido";
                    Find<TextBox>(options, "OptionValue").Text = "Gris de estudio";
                    AddOption(options, "Sobrante", "Quitar");
                    Click(options, "removeVariableOption");
                    AddOption(options, "Rosa", multiline);
                    Click(options, "acceptVariableOptions");
                });
                Check(OptionCount(variables) == 2, "La fila debe conservar las dos opciones elegidas.");
                Click(variables, "saveVariables");
                Check(variables.DialogResult == DialogResult.OK, "Guardar debe aceptar el editor de variables.");
            }
            var saved = entry.Variables.Single().Options;
            Check(saved.Count == 2 && saved[0].Label == "Gris cálido" && saved[0].Value == "Gris de estudio"
                && saved[0].Selected && entry.Variables[0].Value == saved[0].Value
                && saved[1].Label == "Rosa" && saved[1].Value == multiline,
                "Guardar debe conservar nombres, orden y texto completo de las opciones.");
            using (var variables = VariableEditor.Create(entry))
            {
                variables.Show(owner);
                Find<DataGridView>(variables, "VariableGrid").Rows[0].Cells["value"].Value = "Texto a mano";
                Click(variables, "saveVariables");
            }
            Check(entry.Variables[0].Value == "Texto a mano" && !entry.Variables[0].Options.Any(o => o.Selected),
                "Editar manualmente el valor predeterminado debe seleccionar Libre.");
        }

        static void EditOptions(Form variables, Action<Form> change)
        {
            var grid = Find<DataGridView>(variables, "VariableGrid");
            grid.CurrentCell = grid.Rows[0].Cells["token"];
            Exception failure = null;
            var title = "Opciones · Color";
            variables.BeginInvoke((MethodInvoker)delegate {
                var options = Application.OpenForms.Cast<Form>().FirstOrDefault(f => f.Text == title);
                try
                {
                    Check(options != null, "Debe abrirse el editor de opciones del marcador seleccionado.");
                    change(options);
                }
                catch (Exception ex)
                {
                    failure = ex;
                    if (options != null) options.DialogResult = DialogResult.Cancel;
                }
            });
            Click(variables, "editVariableOptions");
            if (failure != null) throw new Exception("Interfaz del editor de opciones", failure);
        }

        static void AddOption(Form form, string label, string value)
        {
            Click(form, "addVariableOption");
            Find<TextBox>(form, "OptionLabel").Text = label;
            Find<TextBox>(form, "OptionValue").Text = value;
        }

        static int OptionCount(Form form)
        {
            return Convert.ToInt32(Find<DataGridView>(form, "VariableGrid").Rows[0].Cells["options"].Value);
        }

        static T Find<T>(Form form, string name) where T : Control
        {
            return (T)form.Controls.Find(name, true).Single();
        }

        static void Click(Form form, string name)
        {
            var button = Find<GlassButton>(form, name);
            Check(button.Enabled, "El botón " + name + " debe estar habilitado.");
            button.PerformClick();
        }

        static void Check(bool condition, string message)
        {
            if (!condition) throw new Exception(message);
        }
    }
}
