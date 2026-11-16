using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using System.Xml;
using System.Xml.Linq;

namespace PromptDesk
{
    public sealed class CloudSyncState
    {
        public long Revision;
        public string BaselineXml;
    }

    // The binding survives sign-out: an existing library cannot accidentally be
    // uploaded to a different account. Session secrets are protected by Windows.
    public sealed class CloudVault
    {
        const int MaximumCatalogBytes = 10 * 1024 * 1024;
        const int MaximumSessionBytes = 128 * 1024;
        const int MaximumStateBytes = 15 * 1024 * 1024;
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);
        static readonly byte[] Entropy = Encoding.UTF8.GetBytes("PromptDesk.Programas404.Session.v1");
        readonly object gate = new object();
        readonly string bindingPath, sessionPath, statePath;
        string boundUserId = "", boundEmail = "";

        public CloudVault(string folder)
        {
            if (String.IsNullOrWhiteSpace(folder)) throw new ArgumentException("Falta la carpeta de datos.");
            Directory.CreateDirectory(folder);
            bindingPath = Path.Combine(folder, "cloud-account.xml");
            sessionPath = Path.Combine(folder, "account-session.bin");
            statePath = Path.Combine(folder, "cloud-baseline.xml");
            LoadBinding();
        }

        public string BoundUserId { get { lock (gate) return boundUserId; } }
        public string BoundEmail { get { lock (gate) return boundEmail; } }

        static string UserId(string value)
        {
            Guid id;
            if (value == null || value.Length != 36 || !Guid.TryParseExact(value, "D", out id) || id == Guid.Empty)
                throw new InvalidDataException("La cuenta recibida no es válida.");
            return id.ToString("D");
        }

        static string Email(string value)
        {
            if (String.IsNullOrWhiteSpace(value) || value.Length > 254 || value.IndexOf('@') < 1 ||
                value.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0)
                throw new InvalidDataException("El correo recibido no es válido.");
            return value.Trim();
        }

        static void ValidateSession(CloudSession session)
        {
            if (session == null) throw new InvalidDataException("No se recibió una sesión válida.");
            UserId(session.UserId); Email(session.Email);
            if (String.IsNullOrEmpty(session.AccessToken) || session.AccessToken.Length > 32768 ||
                String.IsNullOrEmpty(session.RefreshToken) || session.RefreshToken.Length > 16384 ||
                session.AccessToken.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                session.RefreshToken.IndexOfAny(new[] { '\r', '\n', '\0' }) >= 0 ||
                session.ExpiresAt < 0 || session.ExpiresAt > 253402300799L)
                throw new InvalidDataException("No se recibió una sesión válida.");
        }

        static XDocument ReadXml(string path, int limit)
        {
            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                if (input.Length > limit) throw new InvalidDataException("El archivo de sincronización supera el límite permitido.");
                using (var reader = XmlReader.Create(input, new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = limit,
                    IgnoreWhitespace = true
                }))
                    return XDocument.Load(reader);
            }
        }

        static byte[] WriteXml(XDocument document)
        {
            using (var output = new MemoryStream())
            {
                using (var writer = XmlWriter.Create(output, new XmlWriterSettings
                {
                    Encoding = Utf8, Indent = false,
                    NewLineHandling = NewLineHandling.Entitize,
                    CloseOutput = false
                })) document.Save(writer);
                return output.ToArray();
            }
        }

        static void AtomicWrite(string destination, byte[] bytes)
        {
            string temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                {
                    output.Write(bytes, 0, bytes.Length);
                    output.Flush(true);
                }
                if (File.Exists(destination)) File.Replace(temporary, destination, null);
                else File.Move(temporary, destination);
            }
            finally
            {
                try { if (File.Exists(temporary)) File.Delete(temporary); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }

        static string Digest(byte[] bytes)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
        }

        void LoadBinding()
        {
            lock (gate)
            {
                if (!File.Exists(bindingPath)) return;
                try
                {
                    XElement root = ReadXml(bindingPath, 4096).Root;
                    if (root == null || root.Name != "promptdesk-account" || (string)root.Attribute("version") != "1")
                        throw new InvalidDataException();
                    string id = UserId((string)root.Attribute("userId"));
                    string email = Email((string)root.Attribute("email"));
                    boundUserId = id; boundEmail = email;
                }
                catch (Exception error)
                {
                    if (error is OutOfMemoryException || error is StackOverflowException) throw;
                    throw new InvalidDataException("No se pudo leer la vinculación de esta biblioteca. Se conservaron sus archivos; no se enviarán a otra cuenta.");
                }
            }
        }

        void RequireAccount(string userId)
        {
            if (boundUserId.Length == 0)
                throw new InvalidOperationException("Primero vinculá esta biblioteca a tu cuenta.");
            if (!String.Equals(boundUserId, UserId(userId), StringComparison.Ordinal))
                throw new InvalidOperationException("Esta biblioteca está vinculada a " + boundEmail + ". Iniciá sesión con esa cuenta para conservar la separación de tus datos.");
        }

        public void BindUser(CloudSession session)
        {
            ValidateSession(session);
            lock (gate)
            {
                string id = UserId(session.UserId), email = Email(session.Email);
                if (boundUserId.Length > 0) RequireAccount(id);
                var document = new XDocument(new XElement("promptdesk-account",
                    new XAttribute("version", "1"), new XAttribute("userId", id), new XAttribute("email", email)));
                AtomicWrite(bindingPath, WriteXml(document));
                boundUserId = id; boundEmail = email;
            }
        }

        public CloudSession LoadSession()
        {
            lock (gate)
            {
                if (!File.Exists(sessionPath)) return null;
                byte[] clear = null;
                try
                {
                    if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                        throw new PlatformNotSupportedException();
                    var file = new FileInfo(sessionPath);
                    if (file.Length == 0 || file.Length > MaximumSessionBytes) throw new InvalidDataException();
                    byte[] protectedBytes = File.ReadAllBytes(sessionPath);
                    clear = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                    if (clear.Length > MaximumSessionBytes) throw new InvalidDataException();
                    var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumSessionBytes, RecursionLimit = 8 };
                    var values = serializer.Deserialize<Dictionary<string, object>>(Utf8.GetString(clear));
                    if (values == null || values.Count != 6 || !values.ContainsKey("version") ||
                        Convert.ToInt64(values["version"], CultureInfo.InvariantCulture) != 1)
                        throw new InvalidDataException();
                    var session = new CloudSession
                    {
                        AccessToken = values["access_token"] as string,
                        RefreshToken = values["refresh_token"] as string,
                        UserId = values["user_id"] as string,
                        Email = values["email"] as string,
                        ExpiresAt = Convert.ToInt64(values["expires_at"], CultureInfo.InvariantCulture)
                    };
                    ValidateSession(session); RequireAccount(session.UserId);
                    session.UserId = UserId(session.UserId);
                    return session;
                }
                catch (Exception error)
                {
                    if (error is OutOfMemoryException || error is StackOverflowException) throw;
                    throw new InvalidDataException("No se pudo desbloquear la sesión guardada. Volvé a iniciar sesión en este equipo; tu biblioteca se conserva.");
                }
                finally { if (clear != null) Array.Clear(clear, 0, clear.Length); }
            }
        }

        public void SaveSession(CloudSession session)
        {
            ValidateSession(session);
            lock (gate)
            {
                RequireAccount(session.UserId);
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                    throw new PlatformNotSupportedException("Este equipo no dispone del almacenamiento seguro de Windows. No se guardó la sesión.");
                byte[] clear = null;
                try
                {
                    var serializer = new JavaScriptSerializer { MaxJsonLength = MaximumSessionBytes, RecursionLimit = 8 };
                    clear = Utf8.GetBytes(serializer.Serialize(new Dictionary<string, object>
                    {
                        { "version", 1 }, { "access_token", session.AccessToken },
                        { "refresh_token", session.RefreshToken }, { "user_id", UserId(session.UserId) },
                        { "email", Email(session.Email) }, { "expires_at", session.ExpiresAt }
                    }));
                    if (clear.Length > MaximumSessionBytes) throw new InvalidDataException("La sesión recibida supera el límite permitido.");
                    byte[] protectedBytes = ProtectedData.Protect(clear, Entropy, DataProtectionScope.CurrentUser);
                    AtomicWrite(sessionPath, protectedBytes);
                }
                finally { if (clear != null) Array.Clear(clear, 0, clear.Length); }
            }
        }

        public void ClearSession()
        {
            lock (gate) { if (File.Exists(sessionPath)) File.Delete(sessionPath); }
        }

        public CloudSyncState LoadState()
        {
            lock (gate)
            {
                if (!File.Exists(statePath)) return new CloudSyncState { Revision = 0, BaselineXml = null };
                try
                {
                    XElement root = ReadXml(statePath, MaximumStateBytes).Root;
                    if (root == null || root.Name != "promptdesk-sync" || (string)root.Attribute("version") != "1")
                        throw new InvalidDataException();
                    RequireAccount((string)root.Attribute("userId"));
                    long revision;
                    if (!Int64.TryParse((string)root.Attribute("revision"), NumberStyles.None,
                        CultureInfo.InvariantCulture, out revision) || revision < 1)
                        throw new InvalidDataException();
                    XElement catalog = root.Element("catalog");
                    if (catalog == null || (string)catalog.Attribute("encoding") != "base64") throw new InvalidDataException();
                    byte[] bytes = Convert.FromBase64String(catalog.Value);
                    if (bytes.Length > MaximumCatalogBytes || !String.Equals(Digest(bytes),
                        (string)root.Attribute("sha256"), StringComparison.Ordinal)) throw new InvalidDataException();
                    string xml = Utf8.GetString(bytes);
                    CloudCatalog.Validate(xml);
                    return new CloudSyncState { Revision = revision, BaselineXml = xml };
                }
                catch (Exception error)
                {
                    if (error is OutOfMemoryException || error is StackOverflowException) throw;
                    throw new InvalidDataException("El estado local de sincronización no se pudo verificar. Se conservaron la biblioteca y el archivo anterior; no se reemplazará la nube.");
                }
            }
        }

        public void SaveState(long revision, string baselineXml)
        {
            if (revision < 1 || baselineXml == null) throw new ArgumentException("Falta una revisión confirmada de la biblioteca.");
            CloudCatalog.Validate(baselineXml);
            byte[] catalog = Utf8.GetBytes(baselineXml);
            if (catalog.Length > MaximumCatalogBytes) throw new InvalidDataException("La biblioteca supera el límite de sincronización de 10 MB.");
            lock (gate)
            {
                RequireAccount(boundUserId);
                var document = new XDocument(new XElement("promptdesk-sync",
                    new XAttribute("version", "1"), new XAttribute("userId", boundUserId),
                    new XAttribute("revision", revision.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("sha256", Digest(catalog)),
                    new XElement("catalog", new XAttribute("encoding", "base64"), Convert.ToBase64String(catalog))));
                AtomicWrite(statePath, WriteXml(document));
            }
        }
    }
}
