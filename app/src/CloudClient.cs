using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace PromptDesk
{
    public sealed class CloudSession
    {
        public string AccessToken, RefreshToken, UserId, Email;
        public long ExpiresAt;

        internal CloudSession Copy()
        {
            return new CloudSession { AccessToken = AccessToken, RefreshToken = RefreshToken,
                UserId = UserId, Email = Email, ExpiresAt = ExpiresAt };
        }
    }

    public sealed class CloudException : Exception
    {
        public int StatusCode { get; private set; }
        public bool RequiresLogin { get; private set; }
        public bool IsConnectionFailure { get; private set; }

        public CloudException(string message, int statusCode = 0, bool requiresLogin = false, bool isConnectionFailure = false)
            : base(message)
        {
            StatusCode = statusCode; RequiresLogin = requiresLogin; IsConnectionFailure = isConnectionFailure;
        }
    }

    public sealed class CloudExchange
    {
        public string ServerXml;
        public long Revision;
        public List<string> Conflicts = new List<string>();
        public Dictionary<string, string> LocalConflictIds = new Dictionary<string, string>();
    }

    // One client represents one desktop session. Serializing complete operations also
    // serializes refresh-token rotation, so two timers cannot consume the same token.
    public sealed class CloudClient
    {
        internal const string ApiUrl = "https://rkuaogjbzuwxprkaxugd.supabase.co";
        // Public client key. Authorization comes from the user's session and RLS.
        internal const string PublishableKey = "sb_publishable_42wNqPtJIXrLrMaRo9s3RA_fNrkjlpw";
        internal const int MaxJsonBytes = 64 * 1024 * 1024;
        readonly SemaphoreSlim operation = new SemaphoreSlim(1, 1);
        readonly Func<string, IDictionary<string, object>, string, Task<IDictionary<string, object>>> transport;
        volatile CloudSession session;

        public CloudClient(CloudSession savedSession) : this(savedSession, SendAsync) { }

        internal CloudClient(CloudSession savedSession,
            Func<string, IDictionary<string, object>, string, Task<IDictionary<string, object>>> request)
        {
            if (request == null) throw new ArgumentNullException("request");
            if (savedSession != null) ValidateSession(savedSession);
            session = savedSession == null ? null : savedSession.Copy();
            if (session != null) session.UserId = Guid.Parse(session.UserId).ToString("D");
            transport = request;
        }

        public CloudSession Session
        {
            get { var value = session; return value == null ? null : value.Copy(); }
        }

        internal static long Now()
        {
            return (long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds;
        }

        internal static void ValidateSession(CloudSession value)
        {
            Guid id;
            if (value == null || !Guid.TryParse(value.UserId, out id) || id == Guid.Empty ||
                !ValidToken(value.AccessToken) || !ValidToken(value.RefreshToken) || value.RefreshToken.Length > 16384 ||
                String.IsNullOrWhiteSpace(value.Email) || value.Email.Length > 254 ||
                value.Email.IndexOf('@') < 1 || value.Email.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                value.ExpiresAt < 0 || value.ExpiresAt > 253402300799L)
                throw new CloudException("No se recibió una sesión válida. Volvé a iniciar sesión.", 0, true);
        }

        static bool ValidToken(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 32768) return false;
            foreach (char c in value) if (Char.IsControl(c) || Char.IsWhiteSpace(c)) return false;
            return true;
        }

        static IDictionary<string, object> Body(params object[] pairs)
        {
            var result = new Dictionary<string, object>();
            for (int i = 0; i < pairs.Length; i += 2) result.Add((string)pairs[i], pairs[i + 1]);
            return result;
        }

        internal static long Number(IDictionary<string, object> data, string key)
        {
            object value;
            if (!data.TryGetValue(key, out value) || !(value is int || value is long || value is decimal))
                throw InvalidResponse();
            decimal number = Convert.ToDecimal(value, CultureInfo.InvariantCulture);
            if (number < 0 || number > Int64.MaxValue || number != Decimal.Truncate(number)) throw InvalidResponse();
            return (long)number;
        }

        static string Text(IDictionary<string, object> data, string key)
        {
            object value;
            return data.TryGetValue(key, out value) ? value as string : null;
        }

        static CloudException InvalidResponse()
        {
            return new CloudException("El servidor devolvió una respuesta inválida. Tus prompts locales se conservan.");
        }

        static CloudSession ParseSession(IDictionary<string, object> data)
        {
            object userValue;
            var user = data.TryGetValue("user", out userValue) ? userValue as IDictionary<string, object> : null;
            if (user == null) throw InvalidResponse();
            long expires;
            if (data.ContainsKey("expires_at")) expires = Number(data, "expires_at");
            else
            {
                long duration = Number(data, "expires_in");
                if (duration < 1 || duration > 31 * 86400L) throw InvalidResponse();
                expires = Now() + duration;
            }
            var value = new CloudSession { AccessToken = Text(data, "access_token"),
                RefreshToken = Text(data, "refresh_token"), UserId = Text(user, "id"),
                Email = Text(user, "email"), ExpiresAt = expires };
            ValidateSession(value);
            value.UserId = Guid.Parse(value.UserId).ToString("D");
            return value;
        }

        public async Task<CloudSession> SignInAsync(string email, string password)
        {
            email = (email ?? "").Trim();
            if (email.Length > 254 || email.IndexOf('@') < 1 || email.IndexOfAny(new[] { ' ', '\r', '\n', '\0', '\t' }) >= 0)
                throw new CloudException("Ingresá un correo válido.");
            if (String.IsNullOrEmpty(password) || password.Length > 256)
                throw new CloudException("Ingresá tu contraseña.");
            await operation.WaitAsync().ConfigureAwait(false);
            try
            {
                var response = await transport("/auth/v1/token?grant_type=password",
                    Body("email", email, "password", password), null).ConfigureAwait(false);
                var signedIn = ParseSession(response);
                session = signedIn;
                return signedIn.Copy();
            }
            finally { operation.Release(); }
        }

        async Task RefreshAsync()
        {
            var previous = session;
            if (previous == null) throw LoginRequired();
            try
            {
                var response = await transport("/auth/v1/token?grant_type=refresh_token",
                    Body("refresh_token", previous.RefreshToken), null).ConfigureAwait(false);
                var refreshed = ParseSession(response);
                if (refreshed.UserId != previous.UserId)
                {
                    session = null;
                    throw new CloudException("La sesión no corresponde a la cuenta vinculada. Volvé a iniciar sesión.", 401, true);
                }
                session = refreshed;
            }
            catch (CloudException error)
            {
                if (error.StatusCode == 400 || error.StatusCode == 401 || error.StatusCode == 403 || error.RequiresLogin)
                {
                    session = null;
                    throw LoginRequired();
                }
                throw;
            }
        }

        static CloudException LoginRequired()
        {
            return new CloudException("Tu sesión venció. Volvé a iniciar sesión; tus prompts siguen guardados.", 401, true);
        }

        async Task<IDictionary<string, object>> AuthenticatedAsync(string route, IDictionary<string, object> body)
        {
            if (session == null) throw LoginRequired();
            if (session.ExpiresAt <= Now() + 60) await RefreshAsync().ConfigureAwait(false);
            try { return await transport(route, body, session.AccessToken).ConfigureAwait(false); }
            catch (CloudException error)
            {
                if (error.StatusCode != 401) throw;
            }
            await RefreshAsync().ConfigureAwait(false);
            try { return await transport(route, body, session.AccessToken).ConfigureAwait(false); }
            catch (CloudException retry)
            {
                if (retry.StatusCode == 401) { session = null; throw LoginRequired(); }
                throw;
            }
        }

        public async Task SignOutAsync()
        {
            await operation.WaitAsync().ConfigureAwait(false);
            try
            {
                if (session != null)
                    await AuthenticatedAsync("/auth/v1/logout?scope=local", null).ConfigureAwait(false);
            }
            finally { session = null; operation.Release(); }
        }

        sealed class RemoteLibrary
        {
            public long Revision;
            public string Xml;
        }

        async Task<RemoteLibrary> ReadAsync(long minimumRevision)
        {
            var response = await AuthenticatedAsync("/rest/v1/rpc/promptdesk_read", Body()).ConfigureAwait(false);
            var remote = new RemoteLibrary { Revision = Number(response, "revision"), Xml = Text(response, "catalog_xml") };
            if (remote.Revision == 0)
            {
                if (remote.Xml != null) throw InvalidResponse();
            }
            else
            {
                if (remote.Xml == null) throw InvalidResponse();
                CloudCatalog.Validate(remote.Xml);
            }
            if (remote.Revision < minimumRevision)
                throw new CloudException("La biblioteca online tiene una revisión anterior a la conocida. Se conservaron tus prompts; no se reemplazó ningún dato.");
            return remote;
        }

        static CloudExchange Exchange(RemoteLibrary remote, CloudMergeResult merge)
        {
            return new CloudExchange { ServerXml = remote.Xml, Revision = remote.Revision,
                Conflicts = merge.Conflicts, LocalConflictIds = merge.LocalConflictIds };
        }

        public async Task<CloudExchange> SynchronizeAsync(string baselineXml, string localXml, long knownRevision)
        {
            CloudCatalog.Validate(localXml);
            if (baselineXml != null) CloudCatalog.Validate(baselineXml);
            if (knownRevision < 0 || ((baselineXml == null) != (knownRevision == 0)))
                throw new CloudException("El estado de sincronización no es válido. Se conservaron los archivos locales.");
            await operation.WaitAsync().ConfigureAwait(false);
            try
            {
                RemoteLibrary remote = null;
                if (baselineXml != null && knownRevision > 0)
                {
                    var head = await AuthenticatedAsync("/rest/v1/rpc/promptdesk_head", Body()).ConfigureAwait(false);
                    long revision = Number(head, "revision");
                    if (revision == knownRevision) remote = new RemoteLibrary { Revision = revision, Xml = baselineXml };
                    else if (revision < knownRevision)
                        throw new CloudException("La biblioteca online tiene una revisión anterior a la conocida. No se reemplazó ningún dato; revisá la cuenta antes de continuar.");
                }
                if (remote == null) remote = await ReadAsync(knownRevision).ConfigureAwait(false);
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var merged = CloudMerge.Merge(baselineXml, localXml, remote.Xml);
                    if (remote.Xml != null && CloudCatalog.Equivalent(merged.Xml, remote.Xml)) return Exchange(remote, merged);
                    ExceptionDispatchInfo uncertainWrite = null;
                    try
                    {
                        var result = await AuthenticatedAsync("/rest/v1/rpc/promptdesk_write",
                            Body("p_expected_revision", remote.Revision, "p_catalog_xml", merged.Xml)).ConfigureAwait(false);
                        object accepted;
                        if (!result.TryGetValue("accepted", out accepted) || !(accepted is bool)) throw InvalidResponse();
                        long revision = Number(result, "revision");
                        if ((bool)accepted)
                        {
                            if (revision < 1 || revision <= remote.Revision) throw InvalidResponse();
                            return Exchange(new RemoteLibrary { Revision = revision, Xml = merged.Xml }, merged);
                        }
                    }
                    catch (CloudException error)
                    {
                        // A lost response may follow a committed write. Read it back
                        // before retrying; never blindly repeat an overwrite.
                        if (error.RequiresLogin || (error.StatusCode > 0 && error.StatusCode < 500)) throw;
                        uncertainWrite = ExceptionDispatchInfo.Capture(error);
                    }
                    remote = await ReadAsync(remote.Revision).ConfigureAwait(false);
                    if (uncertainWrite != null)
                    {
                        if (remote.Xml != null && CloudCatalog.Equivalent(remote.Xml, merged.Xml)) return Exchange(remote, merged);
                        if (attempt == 2) uncertainWrite.Throw();
                    }
                }
                throw new CloudException("La biblioteca cambió en otro equipo mientras sincronizábamos. Tus cambios están guardados en este equipo; se reintentará automáticamente.", 409);
            }
            finally { operation.Release(); }
        }

        static Task<IDictionary<string, object>> SendAsync(string route, IDictionary<string, object> body, string token)
        {
            return Task.Run(() => Send(route, body, token));
        }

        static IDictionary<string, object> Send(string route, IDictionary<string, object> body, string token)
        {
            if (route != "/auth/v1/token?grant_type=password" && route != "/auth/v1/token?grant_type=refresh_token" &&
                route != "/auth/v1/logout?scope=local" && route != "/rest/v1/rpc/promptdesk_head" &&
                route != "/rest/v1/rpc/promptdesk_read" && route != "/rest/v1/rpc/promptdesk_write")
                throw new CloudException("Operación de sincronización no permitida.");
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
            var request = (HttpWebRequest)WebRequest.Create(ApiUrl + route);
            request.Method = "POST";
            request.ContentType = "application/json; charset=utf-8";
            request.Accept = "application/json";
            request.Headers["apikey"] = PublishableKey;
            if (token != null) request.Headers["Authorization"] = "Bearer " + token;
            request.UserAgent = "PromptDesk-Cloud";
            request.AllowAutoRedirect = false;
            request.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
            request.Timeout = request.ReadWriteTimeout = 20000;
            byte[] payload = body == null ? new byte[0] : Encoding.UTF8.GetBytes(Serializer().Serialize(body));
            if (payload.Length > MaxJsonBytes) throw new CloudException("La biblioteca supera el tamaño permitido para sincronizar.");
            request.ContentLength = payload.Length;
            using (var deadline = new System.Threading.Timer(state => request.Abort(), null, 20000, Timeout.Infinite))
            try
            {
                if (payload.Length > 0) using (var output = request.GetRequestStream()) output.Write(payload, 0, payload.Length);
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    int status = (int)response.StatusCode;
                    if (status < 200 || status >= 300) throw new CloudException("El servidor rechazó la conexión segura.", status);
                    string json = ReadBody(response, MaxJsonBytes);
                    return String.IsNullOrWhiteSpace(json) ? Body() : ParseObject(json);
                }
            }
            catch (WebException error)
            {
                var response = error.Response as HttpWebResponse;
                if (response == null)
                    throw new CloudException("Sin conexión con Programas 404. Tus cambios están guardados en este equipo; se sincronizarán al reconectar.", 0, false, true);
                using (response)
                {
                    IDictionary<string, object> data = Body();
                    try { string json = ReadBody(response, 1024 * 1024); if (json.Length > 0) data = ParseObject(json); }
                    catch { }
                    throw FriendlyError(data, (int)response.StatusCode);
                }
            }
            catch (IOException)
            {
                throw new CloudException("Se interrumpió la conexión. Tus prompts locales se conservan y la sincronización se reintentará.", 0, false, true);
            }
        }

        internal static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = MaxJsonBytes, RecursionLimit = 64 };
        }

        internal static IDictionary<string, object> ParseObject(string json)
        {
            if (json == null || json.Length > MaxJsonBytes) throw InvalidResponse();
            try
            {
                var result = Serializer().DeserializeObject(json) as IDictionary<string, object>;
                if (result == null) throw InvalidResponse();
                return result;
            }
            catch (CloudException) { throw; }
            catch (Exception) { throw InvalidResponse(); }
        }

        static string ReadBody(HttpWebResponse response, int limit)
        {
            if (response.ContentLength > limit) throw InvalidResponse();
            using (var stream = response.GetResponseStream())
            using (var memory = new MemoryStream())
            {
                var buffer = new byte[16384];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (memory.Length + count > limit) throw InvalidResponse();
                    memory.Write(buffer, 0, count);
                }
                try { return new UTF8Encoding(false, true).GetString(memory.ToArray()); }
                catch (DecoderFallbackException) { throw InvalidResponse(); }
            }
        }

        static CloudException FriendlyError(IDictionary<string, object> data, int status)
        {
            string code = Text(data, "error_code") ?? Text(data, "code");
            string message;
            switch (code)
            {
                case "invalid_credentials": message = "Correo o contraseña incorrectos. Usá la misma cuenta que en PhotoBatch."; break;
                case "email_not_confirmed": message = "Primero confirmá el correo de tu cuenta desde PhotoBatch."; break;
                case "refresh_token_not_found": case "refresh_token_already_used": case "session_not_found":
                    return LoginRequired();
                default:
                    message = status == 429 ? "Demasiados intentos. Esperá unos minutos y reintentá." :
                        status == 401 ? "No se pudo validar la sesión. Volvé a iniciar sesión." :
                        status == 403 ? "La cuenta no tiene permiso para esta operación. Tus prompts se conservan." :
                        status == 404 ? "La sincronización no está disponible. Comprobá que tenés la última versión de PromptDesk." :
                        status >= 500 ? "Programas 404 no está disponible por el momento. Se reintentará sin borrar tus cambios." :
                        "No se pudo completar la sincronización. Tus prompts locales se conservan.";
                    break;
            }
            return new CloudException(message, status);
        }
    }
}
