using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PromptDesk
{
    // Account operations are supplied by the window controller. This view never
    // stores credentials, accesses Supabase, or chooses a catalogue to upload.
    internal sealed class CloudAccountDialog : DarkForm
    {
        readonly TextBox emailBox, passwordBox;
        readonly Label message;
        readonly GlassButton primary, signOutButton, closeButton;
        bool working;

        public CloudAccountDialog(string initialEmail, bool signedIn, string status,
            Func<string, string, Task> signIn, Func<Task> synchronize, Func<Task> signOut)
        {
            Text = signedIn ? "Mi cuenta · Programas 404" : "Conectar PromptDesk";
            Icon = Branding.Icon;
            ClientSize = new Size(526, 454);
            MinimumSize = Size;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = UI.Bg; ForeColor = UI.Ink;
            Font = new Font("Segoe UI", 10);

            var layout = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, Padding = new Padding(25, 19, 25, 18),
                ColumnCount = 1, RowCount = 6, BackColor = UI.Bg
            };
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 73));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 139));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            Controls.Add(layout);

            layout.Controls.Add(TextLabel("PROGRAMAS 404", 8.5f, UI.Accent, FontStyle.Bold), 0, 0);
            layout.Controls.Add(TextLabel(signedIn ? "Tu biblioteca está vinculada" : "Tus prompts, en ambos equipos", 17, UI.Ink, FontStyle.Bold), 0, 1);
            layout.Controls.Add(TextLabel(signedIn
                ? "Esta es la misma cuenta de PhotoBatch. La biblioteca se sincroniza entre tu PC y tu notebook."
                : "Usá la misma cuenta de PhotoBatch. Los prompts de este equipo se sumarán a tu biblioteca, sin reemplazar los del otro equipo.",
                9.5f, UI.Muted, FontStyle.Regular), 0, 2);

            var fields = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4,
                Margin = Padding.Empty, BackColor = UI.Bg
            };
            fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 23));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 39));
            fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
            fields.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.Controls.Add(fields, 0, 3);
            fields.Controls.Add(TextLabel("CORREO", 8.5f, UI.Muted, FontStyle.Bold), 0, 0);
            emailBox = Input("accountEmail", "Correo de Programas 404", initialEmail ?? "");
            emailBox.ReadOnly = signedIn;
            emailBox.TabIndex = 0;
            fields.Controls.Add(emailBox, 0, 1);

            passwordBox = Input("accountPassword", "Contraseña", "");
            passwordBox.UseSystemPasswordChar = true;
            passwordBox.MaxLength = 256;
            passwordBox.TabIndex = 1;
            if (signedIn)
            {
                var note = TextLabel("Cerrar sesión acá conserva la sesión de tus otros programas y equipos.",
                    9, UI.Muted, FontStyle.Regular);
                fields.Controls.Add(note, 0, 2); fields.SetRowSpan(note, 2);
                passwordBox.Visible = false;
                // There is no password field in the connected layout. The
                // unused input is disposed with this view without a native handle.
            }
            else
            {
                fields.Controls.Add(TextLabel("CONTRASEÑA", 8.5f, UI.Muted, FontStyle.Bold), 0, 2);
                fields.Controls.Add(passwordBox, 0, 3);
            }

            message = TextLabel(String.IsNullOrWhiteSpace(status)
                ? (signedIn ? "Cuenta conectada." : "La sesión se recuerda de forma protegida en Windows.")
                : status, 9, UI.Muted, FontStyle.Regular);
            message.Name = "accountMessage";
            message.AccessibleRole = AccessibleRole.StaticText;
            layout.Controls.Add(message, 0, 4);

            var buttons = new TableLayoutPanel
            {
                Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1,
                Margin = Padding.Empty, Padding = Padding.Empty
            };
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, signedIn ? 186 : 174));
            layout.Controls.Add(buttons, 0, 5);

            closeButton = ActionButton("Cerrar", "accountClose");
            closeButton.Quiet = true; closeButton.TabIndex = 4;
            closeButton.Click += (s, e) => { if (!working) { DialogResult = DialogResult.Cancel; Close(); } };
            buttons.Controls.Add(closeButton, 0, 0);

            primary = ActionButton(signedIn ? "Sincronizar ahora" : "Iniciar sesión", "accountPrimary");
            primary.Primary = true; primary.TabIndex = 2;
            buttons.Controls.Add(primary, 2, 0);
            if (signedIn)
            {
                signOutButton = ActionButton("Cerrar sesión", "accountSignOut");
                signOutButton.Quiet = true; signOutButton.TabIndex = 3;
                buttons.Controls.Add(signOutButton, 1, 0);
                primary.Click += async (s, e) => await PerformAsync(synchronize, "Sincronizando tu biblioteca…");
                signOutButton.Click += async (s, e) => await PerformAsync(signOut, "Guardando y cerrando esta sesión…");
            }
            else
            {
                primary.Click += async (s, e) =>
                {
                    if (String.IsNullOrWhiteSpace(emailBox.Text))
                    {
                        ShowError("Ingresá el correo de tu cuenta de PhotoBatch."); emailBox.Focus(); return;
                    }
                    if (passwordBox.Text.Length == 0)
                    {
                        ShowError("Ingresá tu contraseña."); passwordBox.Focus(); return;
                    }
                    await PerformAsync(() => signIn(emailBox.Text.Trim(), passwordBox.Text), "Conectando tu cuenta…");
                };
            }
            AcceptButton = primary;
            CancelButton = closeButton;
            Shown += (s, e) => { if (signedIn) primary.Focus(); else if (emailBox.Text.Length == 0) emailBox.Focus(); else passwordBox.Focus(); };
            FormClosing += (s, e) => { if (working) e.Cancel = true; };
            FormClosed += (s, e) => passwordBox.Clear();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && passwordBox != null && passwordBox.Parent == null) passwordBox.Dispose();
            base.Dispose(disposing);
        }

        static Label TextLabel(string text, float size, Color color, FontStyle style)
        {
            return new Label
            {
                Text = text, Dock = DockStyle.Fill, Margin = Padding.Empty,
                Font = new Font("Segoe UI", size, style), ForeColor = color,
                BackColor = UI.Bg, TextAlign = ContentAlignment.MiddleLeft
            };
        }

        static TextBox Input(string name, string accessibleName, string text)
        {
            var box = new TextBox
            {
                Name = name, AccessibleName = accessibleName, Text = text,
                Dock = DockStyle.Top, Margin = new Padding(0, 3, 0, 0), MaxLength = 254
            };
            UI.Style(box);
            return box;
        }

        static GlassButton ActionButton(string text, string name)
        {
            return new GlassButton
            {
                Text = text, Name = name, AccessibleName = text,
                Dock = DockStyle.Fill, AutoSize = false, Margin = new Padding(0, 3, 5, 3)
            };
        }

        void ShowError(string text)
        {
            message.ForeColor = Color.FromArgb(248, 174, 127);
            message.Text = text;
        }

        async Task PerformAsync(Func<Task> action, string progress)
        {
            if (working) return;
            working = true;
            primary.Enabled = closeButton.Enabled = emailBox.Enabled = passwordBox.Enabled = false;
            if (signOutButton != null) signOutButton.Enabled = false;
            message.ForeColor = UI.Accent; message.Text = progress;
            bool success = false;
            try
            {
                if (action == null) throw new InvalidOperationException("Esta acción no está disponible.");
                await action();
                passwordBox.Clear();
                success = true;
            }
            catch (Exception error)
            {
                string text = String.IsNullOrWhiteSpace(error.Message) ? "No se pudo completar la operación. Reintentá." : error.Message;
                // UI callbacks should already provide friendly messages. Do not
                // echo the entered credential if an implementation error does.
                if (passwordBox.Text.Length > 0) text = text.Replace(passwordBox.Text, "••••");
                ShowError(text.Length > 650 ? text.Substring(0, 650) + "…" : text);
            }
            finally
            {
                working = false;
                primary.Enabled = closeButton.Enabled = emailBox.Enabled = passwordBox.Enabled = true;
                if (signOutButton != null) signOutButton.Enabled = true;
            }
            if (success) { DialogResult = DialogResult.OK; Close(); }
            else if (passwordBox.Visible) { passwordBox.Focus(); passwordBox.SelectAll(); }
        }
    }
}
