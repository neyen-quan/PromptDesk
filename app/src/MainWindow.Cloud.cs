using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PromptDesk
{
    public partial class MainWindow
    {
        CloudVault cloudVault;
        CloudClient cloudClient;
        CloudSyncState cloudState;
        Task<bool> cloudTask;
        System.Windows.Forms.Timer cloudTimer;
        GlassButton accountButton;
        Label cloudStatus;
        bool cloudPending, cloudSyncBusy, cloudAccountBusy, cloudAccountOpen;
        bool cloudApplying, cloudClosing, cloudAllowClose, cloudUpdatesOpen;
        bool cloudNeedsLogin, cloudOffline;
        int cloudGeneration;
        DateTime cloudDue = DateTime.MaxValue, cloudLastSync = DateTime.MinValue;
        string cloudProblem = "";
        readonly List<string> cloudNotices = new List<string>();
        readonly HashSet<string> cloudHotkeyWarnings = new HashSet<string>(StringComparer.Ordinal);

        bool CloudSignedIn { get { return cloudClient != null && cloudClient.Session != null; } }

        void InitializeCloud()
        {
            if (testing) { UpdateCloudStatus(); return; }
            try
            {
                cloudVault = new CloudVault(db.Folder);
                cloudClient = new CloudClient(cloudVault.LoadSession());
                cloudState = cloudVault.LoadState();
                cloudPending = cloudState.BaselineXml == null || !CloudCatalog.Equivalent(CloudCatalog.Serialize(db), cloudState.BaselineXml);
                if (CloudSignedIn) cloudDue = DateTime.UtcNow;
            }
            catch (Exception error) { cloudProblem = error.Message; }
            cloudTimer = new System.Windows.Forms.Timer(uiComponents) { Interval = 1000 };
            cloudTimer.Tick += async (s, e) =>
            {
                if (!CloudSignedIn || cloudSyncBusy || cloudAccountBusy || cloudClosing || cloudUpdatesOpen || !Enabled || dirty || grid.IsCurrentCellInEditMode || DateTime.UtcNow < cloudDue) return;
                await SynchronizeCloudAsync();
            };
            Shown += (s, e) => cloudTimer.Start();
            Activated += (s, e) =>
            {
                if (CloudSignedIn && DateTime.UtcNow - cloudLastSync > TimeSpan.FromSeconds(15)) cloudDue = DateTime.UtcNow.AddSeconds(1);
            };
            UpdateCloudStatus();
        }

        void CloudLocalChanged()
        {
            if (testing || cloudApplying) return;
            cloudPending = true;
            cloudDue = DateTime.UtcNow.AddSeconds(3);
            UpdateCloudStatus();
        }

        void CloudLocalSaved()
        {
            if (testing || cloudApplying) return;
            // Undo and restore also save the catalogue without calling Changed.
            cloudPending = cloudState == null || cloudState.BaselineXml == null ||
                !CloudCatalog.Equivalent(CloudCatalog.Serialize(db), cloudState.BaselineXml);
            cloudDue = DateTime.UtcNow.AddSeconds(cloudPending ? 3 : 25);
            UpdateCloudStatus();
        }

        void UpdateCloudStatus()
        {
            if (accountButton == null || cloudStatus == null) return;
            accountButton.Text = CloudSignedIn ? "Mi cuenta" : "Iniciar sesión";
            accountButton.Active = CloudSignedIn;
            accountButton.Enabled = !cloudClosing && !cloudAccountBusy;
            accountButton.Invalidate();
            Color tone = UI.Muted;
            string text;
            if (cloudSyncBusy) { text = "Sincronizando…"; tone = UI.Accent; }
            else if (cloudNeedsLogin) { text = "Volvé a iniciar sesión · cambios locales"; tone = Color.FromArgb(248, 174, 127); }
            else if (cloudProblem.Length > 0) { text = cloudOffline ? "Sin conexión · guardado en este equipo" : CloudSignedIn ? "Pendiente · revisar sincronización" : "Cuenta · requiere atención"; tone = Color.FromArgb(248, 174, 127); }
            else if (!CloudSignedIn) text = cloudVault != null && !String.IsNullOrEmpty(cloudVault.BoundUserId) ? "Sesión cerrada · biblioteca local" : "Solo en este equipo · iniciar sesión";
            else if (cloudPending) { text = "Cambios locales · pendientes de subir"; tone = UI.Accent; }
            else if (cloudNotices.Count > 0) { text = "Sincronizado · revisar cambios"; tone = Color.FromArgb(248, 174, 127); }
            else text = "Sincronizado" + (cloudLastSync == DateTime.MinValue ? "" : " · " + cloudLastSync.ToLocalTime().ToString("HH:mm"));
            cloudStatus.Text = "●  " + text;
            cloudStatus.ForeColor = tone;
            string email = CloudSignedIn ? cloudClient.Session.Email : cloudVault == null ? "" : cloudVault.BoundEmail;
            string detail = email + (email.Length > 0 ? "\n" : "") + (cloudProblem.Length > 0 ? cloudProblem : text);
            if (cloudNotices.Count > 0) detail += "\nHay avisos de sincronización. Abrí Mi cuenta para revisarlos.";
            tips.SetToolTip(cloudStatus, detail);
            tips.SetToolTip(accountButton, "Cuenta Programas 404\n" + detail);
        }

        void OpenCloudAccount()
        {
            if (testing || cloudAccountOpen || cloudClosing) return;
            if (cloudVault == null)
            {
                MessageBox.Show(this, cloudProblem.Length > 0 ? cloudProblem : "No se pudo abrir la configuración de cuenta.", "Cuenta Programas 404", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            cloudAccountOpen = true;
            try
            {
                ShowCloudNotices();
                string email = CloudSignedIn ? cloudClient.Session.Email : cloudVault.BoundEmail;
                using (var dialog = new CloudAccountDialog(email, CloudSignedIn,
                    cloudProblem.Length > 0 ? cloudProblem : cloudStatus.Text,
                    SignInCloudAsync,
                    async () => { if (!await SynchronizeCloudAsync()) throw new Exception(cloudProblem.Length > 0 ? cloudProblem : "Iniciá sesión para sincronizar."); },
                    SignOutCloudAsync)) dialog.ShowDialog(this);
                ShowCloudNotices();
            }
            finally { cloudAccountOpen = false; UpdateCloudStatus(); }
        }

        void ShowCloudNotices()
        {
            if (cloudNotices.Count == 0) return;
            string text = "Tu biblioteca se conserva. Revisá estos avisos de sincronización:\n\n" +
                String.Join("\n", cloudNotices.Take(15).Select(item => "• " + item).ToArray());
            if (cloudNotices.Count > 15) text += "\n\nHay " + (cloudNotices.Count - 15) + " cambios adicionales para revisar.";
            MessageBox.Show(this, text, "Revisión de sincronización", MessageBoxButtons.OK, MessageBoxIcon.Information);
            cloudNotices.Clear();
        }

        void ReportCloudHotkeyErrors(IEnumerable<string> errors)
        {
            var current = new HashSet<string>(errors, StringComparer.Ordinal);
            // A read warning stays acknowledged across later background syncs.
            // It can appear again only after that shortcut worked in between.
            cloudHotkeyWarnings.IntersectWith(current);
            foreach (string error in current)
            {
                if (!cloudHotkeyWarnings.Add(error)) continue;
                string notice = "Atajo no disponible en este equipo (inválido u ocupado): " + error +
                    ". El prompt sigue disponible para copiar desde la biblioteca.";
                if (!cloudNotices.Contains(notice)) cloudNotices.Add(notice);
            }
        }

        async Task SignInCloudAsync(string email, string password)
        {
            if (cloudAccountBusy) throw new Exception("Ya hay una operación de cuenta en curso.");
            cloudAccountBusy = true;
            UpdateCloudStatus();
            CloudClient candidate = null;
            bool accepted = false;
            try
            {
                ExceptionDispatchInfo loginFailure = null;
                try
                {
                    if (cloudTask != null && !cloudTask.IsCompleted) await cloudTask;
                    candidate = new CloudClient(null);
                    CloudSession session = await candidate.SignInAsync(email, password);
                    cloudVault.BindUser(session);
                    cloudVault.SaveSession(session);
                    cloudGeneration++;
                    cloudClient = candidate;
                    accepted = true;
                    cloudState = cloudVault.LoadState();
                    cloudProblem = "";
                    cloudNeedsLogin = cloudOffline = false;
                    cloudPending = cloudState.BaselineXml == null || !CloudCatalog.Equivalent(CloudCatalog.Serialize(db), cloudState.BaselineXml);
                    cloudDue = DateTime.UtcNow;
                }
                catch (Exception error) { loginFailure = ExceptionDispatchInfo.Capture(error); }
                // C# 5 cannot await inside catch/finally. Keep cleanup awaited
                // before releasing the account lock and rethrow the original error.
                if (!accepted && candidate != null && candidate.Session != null)
                    try { await candidate.SignOutAsync(); } catch { }
                if (loginFailure != null) loginFailure.Throw();
            }
            finally
            {
                cloudAccountBusy = false;
                UpdateCloudStatus();
            }
            // A valid login stays active even if the first network sync is interrupted.
            await SynchronizeCloudAsync();
        }

        async Task SignOutCloudAsync()
        {
            if (cloudAccountBusy) throw new Exception("Ya hay una operación de cuenta en curso.");
            cloudAccountBusy = true;
            UpdateCloudStatus();
            try
            {
                if (cloudTask != null && !cloudTask.IsCompleted) await cloudTask;
                if (CloudSignedIn && cloudPending) await SynchronizeCloudAsync();
                if (cloudPending && !UI.Confirm(this, "Quedan cambios guardados solo en este equipo. Se subirán cuando vuelvas a iniciar sesión con esta misma cuenta.\n\n¿Cerrar esta sesión igualmente?"))
                    throw new Exception("La sesión sigue abierta. Podés reintentar la sincronización.");
                CloudClient leaving = cloudClient;
                // If deleting the remembered session fails, keep the account
                // visibly connected instead of claiming a logout that won't last.
                cloudVault.ClearSession();
                cloudGeneration++;
                cloudClient = null;
                cloudDue = DateTime.MaxValue;
                // Clear locally first: an offline logout must never restore tokens later.
                try { if (leaving != null) await leaving.SignOutAsync(); }
                catch { /* Local sign-out is complete; it never closes other devices. */ }
                cloudProblem = "";
                cloudNeedsLogin = cloudOffline = false;
            }
            finally { cloudAccountBusy = false; UpdateCloudStatus(); }
        }

        Task<bool> SynchronizeCloudAsync()
        {
            if (cloudTask != null && !cloudTask.IsCompleted) return cloudTask;
            cloudTask = RunCloudSyncAsync();
            return cloudTask;
        }

        async Task<bool> RunCloudSyncAsync()
        {
            if (testing || !CloudSignedIn || cloudVault == null) return false;
            var client = cloudClient;
            int generation = cloudGeneration;
            cloudSyncBusy = true;
            cloudProblem = "";
            cloudNeedsLogin = cloudOffline = false;
            UpdateCloudStatus();
            try
            {
                if (!Save()) throw new Exception("No se pudieron guardar los cambios locales. La sincronización se detuvo.");
                var state = cloudState ?? cloudVault.LoadState();
                string snapshot = CloudCatalog.Serialize(db);
                CloudExchange exchange = await client.SynchronizeAsync(state.BaselineXml, snapshot, state.Revision);
                if (IsDisposed || generation != cloudGeneration || !Object.ReferenceEquals(client, cloudClient)) return false;
                if (client.Session != null) cloudVault.SaveSession(client.Session);
                // Editors in modal windows retain Entry references until they
                // close. Apply after that point, rebasing their saved changes.
                while (!IsDisposed && generation == cloudGeneration && Object.ReferenceEquals(client, cloudClient) &&
                    !Enabled && !cloudClosing && !cloudAccountOpen)
                    await Task.Delay(120);
                if (IsDisposed || generation != cloudGeneration || !Object.ReferenceEquals(client, cloudClient)) return false;
                ApplyCloudExchange(snapshot, exchange);
                cloudLastSync = DateTime.UtcNow;
                cloudProblem = "";
                return true;
            }
            catch (Exception error)
            {
                if (!IsDisposed && generation == cloudGeneration && Object.ReferenceEquals(client, cloudClient))
                {
                    cloudProblem = error.Message;
                    var failure = error as CloudException;
                    cloudNeedsLogin = failure != null && failure.RequiresLogin;
                    cloudOffline = failure != null && failure.IsConnectionFailure;
                    try
                    {
                        if (failure != null && failure.RequiresLogin) cloudVault.ClearSession();
                        else if (client.Session != null) cloudVault.SaveSession(client.Session);
                    }
                    catch { /* Keep the original error; the account panel remains actionable. */ }
                }
                return false;
            }
            finally
            {
                if (!IsDisposed && generation == cloudGeneration && Object.ReferenceEquals(client, cloudClient))
                {
                    cloudSyncBusy = false;
                    cloudDue = DateTime.UtcNow.AddSeconds(cloudProblem.Length > 0 ? 30 : cloudPending ? 3 : 25);
                    UpdateCloudStatus();
                }
            }
        }

        // Integrate a response against what the user typed while HTTP was in flight.
        // The baseline always represents the server acknowledgement, never later edits.
        void ApplyCloudExchange(string sentSnapshot, CloudExchange exchange)
        {
            if (exchange == null || exchange.Revision < 1) throw new Exception("No se recibió una versión válida de la biblioteca.");
            if (!grid.EndEdit()) throw new Exception("Terminá de editar la celda antes de sincronizar.");
            if (orderPending) { CaptureColumnOrder(); Changed(); orderPending = false; }
            string before = CloudCatalog.Serialize(db);
            var rebased = CloudMerge.Merge(sentSnapshot, before, exchange.ServerXml);
            bool changed = !CloudCatalog.Equivalent(before, rebased.Xml);
            string selectionId = selected == null ? null : selected.Id;
            string mapped;
            if (selectionId != null)
            {
                if (rebased.LocalConflictIds.TryGetValue(selectionId, out mapped)) selectionId = mapped;
                else if (exchange.LocalConflictIds != null && exchange.LocalConflictIds.TryGetValue(selectionId, out mapped)) selectionId = mapped;
            }
            bool editorFocused = editor.Focused, titleFocused = entryTitle.Focused;
            int caret = editor.SelectionStart, length = editor.SelectionLength;
            int titleCaret = entryTitle.SelectionStart, titleLength = entryTitle.SelectionLength;
            cloudApplying = true;
            try
            {
                if (changed)
                {
                    // Publish to disk before replacing live objects. A failed write
                    // leaves the editor bound to its original in-memory entries.
                    var candidate = new Store(db.Folder);
                    candidate.ReadDocument(CloudCatalog.ParseDocument(rebased.Xml));
                    candidate.Save();
                    db.Fields = candidate.Fields; db.Entries = candidate.Entries;
                    db.Categories = candidate.Categories; db.Models = candidate.Models;
                    db.ColumnOrder = candidate.ColumnOrder; db.CategoryOrder = candidate.CategoryOrder;
                    db.DisabledChoices = candidate.DisabledChoices;
                }
                else if (dirty) db.Save();
                dirty = false;
                saveTimer.Stop();
                if (changed)
                {
                    // Undo snapshots from before a remote merge must not delete remote additions.
                    history = new UndoHistory(db);
                    selected = selectionId == null ? null : db.Entries.FirstOrDefault(entry => entry.Id == selectionId);
                    RefreshAll();
                    RegisterKeys(false);
                    if (editorFocused) { editor.Focus(); editor.Select(Math.Min(caret, editor.TextLength), Math.Min(length, Math.Max(0, editor.TextLength - caret))); }
                    if (titleFocused) { entryTitle.Focus(); entryTitle.Select(Math.Min(titleCaret, entryTitle.TextLength), Math.Min(titleLength, Math.Max(0, entryTitle.TextLength - titleCaret))); }
                }
                UpdateEditorSummary();
                // Save the catalogue first. A failed baseline write can be retried safely.
                if (!testing) cloudVault.SaveState(exchange.Revision, exchange.ServerXml);
                cloudState = new CloudSyncState { Revision = exchange.Revision, BaselineXml = exchange.ServerXml };
                cloudPending = !CloudCatalog.Equivalent(CloudCatalog.Serialize(db), exchange.ServerXml);
                foreach (string notice in (exchange.Conflicts ?? new List<string>()).Concat(rebased.Conflicts))
                    if (!cloudNotices.Contains(notice)) cloudNotices.Add(notice);
                status.Text = "Todo guardado en este equipo · " + db.Entries.Count + " prompts";
            }
            finally { cloudApplying = false; }
        }

        bool PermitCloudClose()
        {
            if (testing || cloudAllowClose || cloudVault == null) return true;
            if (cloudClosing) return false;
            if (!CloudSignedIn)
            {
                if (!cloudPending || String.IsNullOrEmpty(cloudVault.BoundUserId)) return true;
                return UI.Confirm(this, "Quedan cambios guardados solo en este equipo. Volvé a iniciar sesión para que aparezcan en tus otros dispositivos.\n\n¿Cerrar igualmente?");
            }
            if (!cloudPending && !cloudSyncBusy) return true;
            FinishCloudCloseAsync();
            return false;
        }

        async void FinishCloudCloseAsync()
        {
            cloudClosing = true;
            Enabled = false;
            bool synced = false;
            try
            {
                synced = await SynchronizeCloudAsync();
                if (synced && cloudPending) synced = await SynchronizeCloudAsync();
            }
            catch (Exception error) { cloudProblem = error.Message; }
            finally { if (!IsDisposed) Enabled = true; }
            if (IsDisposed) return;
            bool close = synced && !cloudPending;
            if (!close) close = UI.Confirm(this, "No se pudo completar la sincronización. Tus cambios están guardados en este equipo y se reintentará al abrirlo.\n\n¿Cerrar igualmente?");
            cloudClosing = false;
            if (close) { cloudAllowClose = true; BeginInvoke((Action)(() => Close())); }
            else UpdateCloudStatus();
        }

        void DisposeCloud()
        {
            cloudGeneration++;
            if (cloudTimer != null) cloudTimer.Stop();
        }

        async void ShowUpdatesWithCloud()
        {
            if (cloudUpdatesOpen || cloudClosing) return;
            cloudUpdatesOpen = true;
            try
            {
                if (cloudTask != null && !cloudTask.IsCompleted) await cloudTask;
                if (CloudSignedIn && cloudPending) await SynchronizeCloudAsync();
                if (IsDisposed) return;
                UpdateCenter.Show(this, () =>
                {
                    if (!Save()) return false;
                    // The updater restarts this same local library and its pending sync state.
                    cloudAllowClose = true;
                    Close();
                    return IsDisposed;
                });
            }
            catch (Exception error) { if (!IsDisposed) MessageBox.Show(this, error.Message, "Actualizar PromptDesk", MessageBoxButtons.OK, MessageBoxIcon.Error); }
            finally { cloudUpdatesOpen = false; }
        }
    }
}
