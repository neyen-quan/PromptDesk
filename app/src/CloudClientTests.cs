using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace PromptDesk
{
    public static class CloudClientTests
    {
        const string User = "a661f2c0-6421-44cd-a645-9875a44794a2";
        const string OtherUser = "681568d2-6a5c-4e6c-afc5-3563bf855fcb";

        static CloudSession Session(bool expired = false)
        {
            return new CloudSession { UserId = User, Email = "prueba@example.invalid",
                AccessToken = "synthetic-access-token", RefreshToken = "synthetic-refresh-token",
                ExpiresAt = expired ? 0 : CloudClient.Now() + 3600 };
        }

        static IDictionary<string, object> Object(params object[] values)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < values.Length; i += 2) result.Add((string)values[i], values[i + 1]);
            return result;
        }

        static IDictionary<string, object> SessionResponse(string access = "refreshed-access-token")
        {
            return Object("access_token", access, "refresh_token", "refreshed-refresh-token",
                "expires_in", 3600, "user", Object("id", User, "email", "prueba@example.invalid"));
        }

        static Exception Failure(Action action)
        {
            try { action(); return null; }
            catch (Exception error) { return error; }
        }

        sealed class Server
        {
            public string Xml, ConcurrentXml, LastToken, LogoutRoute;
            public long Revision;
            public int Reads, Heads, Writes, Refreshes;
            public bool LoseCommittedReply, AlwaysReject, NetworkDown, RejectRefresh, Protected401, LogoutFails,
                AlwaysUnauthorized, UncertainWriteFailure;

            public async Task<IDictionary<string, object>> Request(string route, IDictionary<string, object> body, string token)
            {
                if (NetworkDown) throw new CloudException("Red sintética no disponible.", 0, false, true);
                if (route.Contains("grant_type=refresh_token"))
                {
                    Refreshes++; await Task.Delay(15).ConfigureAwait(false);
                    if (RejectRefresh) throw new CloudException("Refresh rechazado.", 400);
                    return SessionResponse();
                }
                if (route.Contains("grant_type=password")) return SessionResponse("signed-in-access-token");
                LastToken = token;
                if (Protected401 || AlwaysUnauthorized) { Protected401 = false; throw new CloudException("JWT vencido.", 401); }
                if (route.StartsWith("/auth/v1/logout", StringComparison.Ordinal))
                {
                    LogoutRoute = route;
                    if (LogoutFails) throw new CloudException("Corte durante cierre de sesión.");
                    return Object();
                }
                if (route.EndsWith("promptdesk_head", StringComparison.Ordinal))
                { Heads++; return Object("revision", Revision); }
                if (route.EndsWith("promptdesk_read", StringComparison.Ordinal))
                { Reads++; return Object("revision", Revision, "catalog_xml", Xml); }
                if (!route.EndsWith("promptdesk_write", StringComparison.Ordinal)) throw new Exception("Ruta inesperada en prueba.");
                Writes++;
                if (UncertainWriteFailure) throw new CloudException("Escritura no confirmada.", 503);
                if (ConcurrentXml != null) { Xml = ConcurrentXml; ConcurrentXml = null; Revision++; }
                if (AlwaysReject || Convert.ToInt64(body["p_expected_revision"]) != Revision)
                    return Object("accepted", false, "revision", Revision);
                Xml = (string)body["p_catalog_xml"]; Revision++;
                if (LoseCommittedReply) { LoseCommittedReply = false; throw new CloudException("Respuesta perdida después del commit."); }
                return Object("accepted", true, "revision", Revision);
            }
        }

        static Store Catalog(string folder)
        {
            var store = new Store(folder);
            store.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "seed.xml"));
            return store;
        }

        static Entry Prompt(string name, string value)
        {
            var entry = new Entry();
            entry.Set("name", name); entry.Set("prompt", value);
            entry.Set("collection", "Personales");
            return entry;
        }

        public static void Run(Action<bool, string> check, string folder)
        {
            string root = Path.Combine(folder, "cloud-client");
            var local = Catalog(Path.Combine(root, "local"));
            local.Entries.Add(Prompt("Notebook", "Texto creado de viaje.\r\nConservá ñ 🚀."));
            string notebook = CloudCatalog.Serialize(local);
            var oldPc = Catalog(Path.Combine(root, "pc"));
            oldPc.Entries.Add(Prompt("PC", "Texto anterior de la PC."));
            string pc = CloudCatalog.Serialize(oldPc);

            var server = new Server();
            var client = new CloudClient(Session(), server.Request);
            CloudExchange first = client.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult();
            check(first.Revision == 1 && CloudCatalog.Equivalent(first.ServerXml, notebook), "La primera sincronización no crea la biblioteca online");
            check(server.Reads == 1 && server.Writes == 1, "La primera sincronización realiza solicitudes duplicadas");
            int reads = server.Reads, writes = server.Writes;
            client.SynchronizeAsync(first.ServerXml, notebook, first.Revision).GetAwaiter().GetResult();
            check(server.Heads == 1 && server.Reads == reads && server.Writes == writes,
                "Sin cambios se descarga o publica el catálogo completo");

            var pcClient = new CloudClient(Session(), server.Request);
            CloudExchange united = pcClient.SynchronizeAsync(null, pc, 0).GetAwaiter().GetResult();
            check(CloudCatalog.ParseDocument(united.ServerXml).Descendants("record").Count() == 2,
                "Vincular la PC reemplaza los prompts creados en notebook");
            CloudExchange received = client.SynchronizeAsync(first.ServerXml, notebook, first.Revision).GetAwaiter().GetResult();
            check(CloudCatalog.Equivalent(received.ServerXml, united.ServerXml), "La notebook no recibe los prompts vinculados desde PC");

            var concurrent = Catalog(Path.Combine(root, "concurrent"));
            concurrent.ReadDocument(CloudCatalog.ParseDocument(united.ServerXml));
            concurrent.Entries.Add(Prompt("Otro equipo", "Cambio simultáneo remoto"));
            local.ReadDocument(CloudCatalog.ParseDocument(united.ServerXml));
            local.Entries.Add(Prompt("Local", "Cambio simultáneo local"));
            server.ConcurrentXml = CloudCatalog.Serialize(concurrent);
            int beforeWrites = server.Writes;
            CloudExchange raced = client.SynchronizeAsync(united.ServerXml, CloudCatalog.Serialize(local), united.Revision).GetAwaiter().GetResult();
            check(server.Writes == beforeWrites + 2 && CloudCatalog.ParseDocument(raced.ServerXml).Descendants("record").Count() == 4,
                "Un conflicto CAS pierde cambios de otro equipo o no reintenta");

            local.ReadDocument(CloudCatalog.ParseDocument(raced.ServerXml));
            local.Entries.Add(Prompt("Respuesta perdida", "Una sola copia tras reintentar"));
            server.LoseCommittedReply = true; beforeWrites = server.Writes;
            CloudExchange recovered = client.SynchronizeAsync(raced.ServerXml, CloudCatalog.Serialize(local), raced.Revision).GetAwaiter().GetResult();
            check(server.Writes == beforeWrites + 1 && CloudCatalog.ParseDocument(recovered.ServerXml).Descendants("record").Count() == 5,
                "Una respuesta perdida repite el commit o duplica prompts");

            var remoteEdit = Catalog(Path.Combine(root, "remote-edit"));
            remoteEdit.ReadDocument(CloudCatalog.ParseDocument(notebook));
            remoteEdit.Entries[0].Set("prompt", "Edición remota del mismo prompt");
            var localEdit = Catalog(Path.Combine(root, "local-edit"));
            localEdit.ReadDocument(CloudCatalog.ParseDocument(notebook));
            localEdit.Entries[0].Set("prompt", "Edición local del mismo prompt");
            var conflictServer = new Server { Xml = CloudCatalog.Serialize(remoteEdit), Revision = 2, LoseCommittedReply = true };
            var conflictClient = new CloudClient(Session(), conflictServer.Request);
            CloudExchange conflictRecovered = conflictClient.SynchronizeAsync(notebook, CloudCatalog.Serialize(localEdit), 1).GetAwaiter().GetResult();
            CloudExchange replayed = conflictClient.SynchronizeAsync(notebook, CloudCatalog.Serialize(localEdit), 1).GetAwaiter().GetResult();
            check(conflictServer.Writes == 1 && conflictRecovered.LocalConflictIds.Count == 1 &&
                CloudCatalog.ParseDocument(replayed.ServerXml).Descendants("record").Count() == 2,
                "Un conflicto con respuesta perdida duplica o pierde una de las versiones al reintentar");

            var expiredServer = new Server { Xml = notebook, Revision = 1 };
            var expiredClient = new CloudClient(Session(true), expiredServer.Request);
            Task.WaitAll(expiredClient.SynchronizeAsync(notebook, notebook, 1), expiredClient.SynchronizeAsync(notebook, notebook, 1));
            check(expiredServer.Refreshes == 1 && expiredServer.LastToken == "refreshed-access-token",
                "Dos sincronizaciones consumen el mismo refresh token");
            expiredServer.Protected401 = true;
            expiredClient.SynchronizeAsync(notebook, notebook, 1).GetAwaiter().GetResult();
            check(expiredServer.Refreshes == 2 && expiredClient.Session != null, "Un JWT rechazado no renueva la sesión una sola vez");
            var deniedServer = new Server { AlwaysUnauthorized = true };
            var deniedClient = new CloudClient(Session(), deniedServer.Request);
            var deniedError = Failure(() => deniedClient.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(deniedError != null && deniedError.RequiresLogin && deniedClient.Session == null && deniedServer.Refreshes == 1,
                "Un segundo rechazo de JWT no cierra la sesión o renueva sin límite");

            var rejected = new CloudClient(Session(true), new Server { RejectRefresh = true }.Request);
            var rejection = Failure(() => rejected.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(rejection != null && rejection.RequiresLogin && rejected.Session == null,
                "Un refresh rechazado mantiene una sesión inválida");
            var wrongRefresh = new CloudClient(Session(true), (route, body, token) =>
            {
                var response = SessionResponse();
                response["user"] = Object("id", OtherUser, "email", "otra@example.invalid");
                return Task.FromResult(response);
            });
            var wrongRefreshError = Failure(() => wrongRefresh.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(wrongRefreshError != null && wrongRefreshError.RequiresLogin && wrongRefresh.Session == null,
                "La renovación acepta tokens de una cuenta distinta");
            var disconnected = new CloudClient(Session(true), new Server { NetworkDown = true }.Request);
            var disconnectedError = Failure(() => disconnected.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(disconnectedError != null && disconnectedError.IsConnectionFailure &&
                disconnected.Session != null, "Una falla de red borra la sesión recuperable o se confunde con credenciales inválidas");

            var boundedServer = new Server { AlwaysReject = true };
            var bounded = new CloudClient(Session(), boundedServer.Request);
            var conflict = Failure(() => bounded.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(conflict != null && conflict.StatusCode == 409 && boundedServer.Writes == 3,
                "La sincronización queda en un bucle de conflictos");
            var uncertainServer = new Server { UncertainWriteFailure = true };
            var uncertainClient = new CloudClient(Session(), uncertainServer.Request);
            var uncertainError = Failure(() => uncertainClient.SynchronizeAsync(null, notebook, 0).GetAwaiter().GetResult()) as CloudException;
            check(uncertainError != null && uncertainError.StatusCode == 503 && uncertainServer.Writes == 3 && uncertainServer.Reads == 4,
                "Una escritura sin confirmar pierde el error original, omite verificar el servidor o reintenta sin límite");
            var rollbackServer = new Server { Xml = notebook, Revision = 1 };
            check(Failure(() => new CloudClient(Session(), rollbackServer.Request).SynchronizeAsync(notebook, notebook, 2).GetAwaiter().GetResult()) != null &&
                rollbackServer.Writes == 0, "Una revisión remota atrasada reemplaza el catálogo");
            check(Failure(() => client.SynchronizeAsync(notebook, notebook, 0).GetAwaiter().GetResult()) != null,
                "Se acepta un baseline sin revisión confirmada");

            var logoutServer = new Server { LogoutFails = true };
            var loggingOut = new CloudClient(Session(), logoutServer.Request);
            check(Failure(() => loggingOut.SignOutAsync().GetAwaiter().GetResult()) != null && loggingOut.Session == null &&
                logoutServer.LogoutRoute == "/auth/v1/logout?scope=local", "Cerrar sesión afecta otras sesiones o retiene el token local");

            var signingIn = new CloudClient(null, new Server().Request);
            CloudSession signedIn = signingIn.SignInAsync("prueba@example.invalid", "synthetic-password").GetAwaiter().GetResult();
            check(signedIn.UserId == User && signedIn.AccessToken == "signed-in-access-token", "Ingreso no interpreta la sesión de Supabase");
            signedIn.AccessToken = "changed-outside-client";
            check(signingIn.Session.AccessToken == "signed-in-access-token", "La sesión expone una referencia mutable fuera del cliente");
            check(Failure(() => signingIn.SignInAsync("correo inválido", "password").GetAwaiter().GetResult()) != null,
                "El ingreso acepta un correo inválido");
            check(Failure(() => CloudClient.ParseObject("[]")) != null &&
                Failure(() => CloudClient.Number(Object("revision", 1.5m), "revision")) != null,
                "La respuesta online acepta estructura o revisión inválida");
            check(Failure(() => CloudClient.ParseObject(String.Concat(Enumerable.Repeat("{\"a\":", 80)) + "0" + new string('}', 80))) != null,
                "La respuesta online admite JSON con anidación ilimitada");
            var badSession = Session(); badSession.AccessToken = "invalid\r\nheader";
            check(Failure(() => new CloudClient(badSession)) != null, "Un token contiene caracteres de encabezado");
            badSession = Session(); badSession.RefreshToken = new string('x', 32769);
            check(Failure(() => new CloudClient(badSession)) != null, "Un token excede el límite permitido");

            CheckVault(check, Path.Combine(root, "vault"), notebook);
        }

        static void CheckVault(Action<bool, string> check, string folder, string catalog)
        {
            var vault = new CloudVault(folder);
            check(vault.LoadSession() == null && vault.LoadState().BaselineXml == null && vault.LoadState().Revision == 0,
                "Una instalación nueva tiene estado o sesión ajenos");
            check(Failure(() => vault.SaveState(1, catalog)) != null, "Se guarda baseline sin cuenta vinculada");
            var session = Session(); vault.BindUser(session); vault.SaveState(7, catalog);
            var reloaded = new CloudVault(folder);
            check(reloaded.BoundUserId == User && reloaded.LoadState().Revision == 7 && reloaded.LoadState().BaselineXml == catalog,
                "El estado sincronizado pierde cuenta, revisión o saltos de línea");
            string bindingFile = Path.Combine(folder, "cloud-account.xml");
            byte[] binding = File.ReadAllBytes(bindingFile);
            var wrong = Session(); wrong.UserId = OtherUser;
            check(Failure(() => vault.BindUser(wrong)) != null && File.ReadAllBytes(bindingFile).SequenceEqual(binding),
                "Iniciar sesión con otra cuenta reasigna una biblioteca existente");
            string stateFile = Path.Combine(folder, "cloud-baseline.xml");
            byte[] before = File.ReadAllBytes(stateFile);
            check(Failure(() => vault.SaveState(8, "<malformed>")) != null && File.ReadAllBytes(stateFile).SequenceEqual(before),
                "Un baseline inválido reemplaza el anterior");
            XDocument state = XDocument.Load(stateFile);
            state.Root.SetAttributeValue("sha256", new string('0', 64)); state.Save(stateFile);
            check(Failure(() => vault.LoadState()) != null && File.Exists(stateFile), "Un baseline corrupto se acepta o se elimina automáticamente");
            vault.SaveState(7, catalog);
            state = XDocument.Load(stateFile); state.Root.SetAttributeValue("userId", OtherUser); state.Save(stateFile);
            check(Failure(() => vault.LoadState()) != null, "Se carga el baseline de otra cuenta");
            vault.SaveState(7, catalog);

            if (Environment.OSVersion.Platform == PlatformID.Win32NT)
            {
                session.ExpiresAt = 0;
                vault.SaveSession(session);
                var restored = new CloudVault(folder).LoadSession();
                check(restored.AccessToken == session.AccessToken && restored.RefreshToken == session.RefreshToken && restored.ExpiresAt == 0,
                    "La sesión cifrada no permite recuperar tokens vencidos para renovación");
                string sessionFile = Path.Combine(folder, "account-session.bin");
                byte[] protectedBytes = File.ReadAllBytes(sessionFile);
                check(!Encoding.UTF8.GetString(protectedBytes).Contains(session.RefreshToken), "El refresh token se guarda en texto plano");
                protectedBytes[protectedBytes.Length / 2] ^= 0x7f; File.WriteAllBytes(sessionFile, protectedBytes);
                check(Failure(() => vault.LoadSession()) != null && File.Exists(sessionFile), "Una sesión cifrada alterada se acepta o se borra");
                vault.SaveSession(session); vault.ClearSession();
                check(vault.LoadSession() == null && vault.BoundUserId == User && vault.LoadState().Revision == 7,
                    "Cerrar sesión elimina la vinculación o el baseline");
            }
            else check(Failure(() => vault.SaveSession(session)) != null, "Los tokens se persisten sin protección de Windows");
            check(Directory.GetFiles(folder, "*.tmp").Length == 0, "El guardado deja archivos temporales de sesión");
        }
    }
}
