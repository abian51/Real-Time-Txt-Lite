using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace LCET
{
    public sealed class LoopbackServer : IDisposable
    {
        private readonly TcpListener listener;
        private readonly string webRoot;
        private readonly string token;
        private readonly string cookieName;
        private readonly object gate = new object();
        private readonly Queue<string> events = new Queue<string>();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private readonly SemaphoreSlim capacity = new SemaphoreSlim(24);
        private readonly HashSet<TcpClient> clients = new HashSet<TcpClient>();
        private volatile bool stopped;
        private TcpClient eventClient;
        public event Action<string, Dictionary<string, object>> Message;
        public event Action Connected;
        public event Action ModelDownloadRequested;
        public int Port { get; private set; }
        public string Origin { get { return "http://127.0.0.1:" + Port; } }
        public string Url { get { return Origin + "/"; } }

        public LoopbackServer(string root)
        {
            webRoot = Path.GetFullPath(root);
            var bytes = new byte[32];
            using (var random = RandomNumberGenerator.Create()) random.GetBytes(bytes);
            token = Convert.ToBase64String(bytes);
            listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(24);
            Port = ((IPEndPoint)listener.LocalEndpoint).Port;
            cookieName = "LCET" + Port;
            Task.Run((Action)Accept);
        }

        private void Accept()
        {
            while (!stopped)
            {
                try
                {
                    TcpClient client = listener.AcceptTcpClient();
                    if (!capacity.Wait(0)) { client.Close(); continue; }
                    lock (gate) clients.Add(client);
                    Task.Run(() =>
                    {
                        try { Handle(client); }
                        catch (Exception exception) when (exception is IOException || exception is SocketException || exception is ObjectDisposedException || exception is ArgumentException || exception is InvalidOperationException) { }
                        finally
                        {
                            lock (gate) clients.Remove(client);
                            client.Close();
                            capacity.Release();
                        }
                    });
                }
                catch (SocketException) { if (stopped) return; }
                catch (ObjectDisposedException) { return; }
            }
        }

        private static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = 65536, RecursionLimit = 12 };
        }

        public void Send(string kind, object data)
        {
            string value = "event: " + kind + "\ndata: " + Serializer().Serialize(data) + "\n\n";
            lock (gate)
            {
                while (events.Count >= 48) events.Dequeue();
                events.Enqueue(value);
            }
            changed.Set();
        }

        private void Reply(NetworkStream stream, int status, string type, byte[] body, bool cookie = false)
        {
            string headers = "HTTP/1.1 " + status + (status == 200 ? " OK" : " Rejected") + "\r\n" +
                "Content-Type: " + type + "\r\nContent-Length: " + body.Length + "\r\n" +
                "Connection: close\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\n" +
                "Referrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; connect-src 'self'; script-src 'self'; style-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'\r\n";
            if (cookie) headers += "Set-Cookie: " + cookieName + "=" + token + "; Path=/; HttpOnly; SameSite=Strict\r\n";
            byte[] header = Encoding.ASCII.GetBytes(headers + "\r\n");
            stream.Write(header, 0, header.Length);
            stream.Write(body, 0, body.Length);
        }

        private void Handle(TcpClient client)
        {
            client.ReceiveTimeout = 5000;
            client.SendTimeout = 1500;
            client.NoDelay = true;
            using (NetworkStream stream = client.GetStream())
            {
                var header = new StringBuilder();
                while (header.Length < 8192)
                {
                    int next = stream.ReadByte();
                    if (next < 0) return;
                    header.Append((char)next);
                    if (header.Length >= 4 && header.ToString(header.Length - 4, 4) == "\r\n\r\n") break;
                }
                if (header.Length >= 8192) return;
                string[] lines = header.ToString().Split(new[] { "\r\n" }, StringSplitOptions.None);
                string[] request = lines[0].Split(' ');
                if (request.Length != 3) return;
                var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (int index = 1; index < lines.Length; index++)
                {
                    int colon = lines[index].IndexOf(':');
                    if (colon > 0)
                    {
                        string name = lines[index].Substring(0, colon);
                        if (headers.ContainsKey(name)) return;
                        headers[name] = lines[index].Substring(colon + 1).Trim();
                    }
                }
                string host, origin, site, cookie;
                headers.TryGetValue("Host", out host);
                headers.TryGetValue("Origin", out origin);
                headers.TryGetValue("Sec-Fetch-Site", out site);
                headers.TryGetValue("Cookie", out cookie);
                bool trusted = host == "127.0.0.1:" + Port && (origin == null || origin == Origin) &&
                    (site == null || site == "none" || site == "same-origin");
                bool authenticated = false;
                foreach (string part in (cookie ?? "").Split(';'))
                    authenticated |= part.Trim() == cookieName + "=" + token;
                string path = request[1];
                bool entry = path == "/" && request[0] == "GET";
                if (!trusted || (!entry && !authenticated))
                {
                    Reply(stream, 403, "text/plain", Encoding.UTF8.GetBytes("Forbidden"));
                    return;
                }
                if (request[0] == "GET" && path == "/events")
                {
                    lock (gate)
                    {
                        if (eventClient != null) eventClient.Close();
                        eventClient = client;
                        events.Clear();
                    }
                    byte[] response = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Type: text/event-stream\r\nCache-Control: no-store\r\nConnection: close\r\nX-Content-Type-Options: nosniff\r\n\r\nretry: 2000\n\n");
                    stream.Write(response, 0, response.Length);
                    Connected?.Invoke();
                    while (!stopped)
                    {
                        lock (gate) { if (eventClient != client) return; }
                        string message = null;
                        lock (gate) { if (events.Count > 0) message = events.Dequeue(); }
                        if (message == null)
                        {
                            if (changed.WaitOne(5000)) continue;
                            message = "event: ping\ndata: {}\n\n";
                        }
                        byte[] packet = Encoding.UTF8.GetBytes(message);
                        stream.Write(packet, 0, packet.Length);
                        stream.Flush();
                    }
                    return;
                }
                if (request[0] == "GET")
                {
                    string filename = entry ? "index.html" : path == "/app.js" ? "app.js" : path == "/app.css" ? "app.css" : path == "/languages.json" ? "languages.json" : null;
                    if (filename == null) { Reply(stream, 404, "text/plain", new byte[0]); return; }
                    byte[] content = File.ReadAllBytes(Path.Combine(webRoot, filename));
                    string type = filename.EndsWith(".json") ? "application/json; charset=utf-8" : filename.EndsWith(".js") ? "text/javascript; charset=utf-8" : filename.EndsWith(".css") ? "text/css; charset=utf-8" : "text/html; charset=utf-8";
                    Reply(stream, 200, type, content, entry);
                    return;
                }
                if (request[0] != "POST" || origin != Origin ||
                    (path != "/status" && path != "/result" && path != "/manual-result" && path != "/heartbeat" && path != "/language-resources" && path != "/open-model-download"))
                { Reply(stream, 403, "text/plain", new byte[0]); return; }
                string lengthValue, contentType;
                int length;
                headers.TryGetValue("Content-Length", out lengthValue);
                headers.TryGetValue("Content-Type", out contentType);
                if (headers.ContainsKey("Transfer-Encoding") || !int.TryParse(lengthValue, out length) || length < 2 || length > 65536 ||
                    contentType == null || !contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
                { Reply(stream, 400, "text/plain", new byte[0]); return; }
                var body = new byte[length];
                int offset = 0;
                while (offset < length)
                {
                    int read = stream.Read(body, offset, length - offset);
                    if (read == 0) return;
                    offset += read;
                }
                Dictionary<string, object> data;
                try { data = Serializer().DeserializeObject(Encoding.UTF8.GetString(body)) as Dictionary<string, object>; }
                catch (ArgumentException) { Reply(stream, 400, "text/plain", new byte[0]); return; }
                catch (InvalidOperationException) { Reply(stream, 400, "text/plain", new byte[0]); return; }
                if (data == null) { Reply(stream, 400, "text/plain", new byte[0]); return; }
                if (path == "/open-model-download")
                {
                    var open = ModelDownloadRequested;
                    if (open == null) { Reply(stream, 503, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}")); return; }
                    try
                    {
                        open();
                        Reply(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
                    }
                    catch (Exception exception)
                    {
                        Diagnostics.Write("model_download", exception.GetType().Name);
                        Reply(stream, 503, "application/json", Encoding.UTF8.GetBytes("{\"ok\":false}"));
                    }
                    return;
                }
                if (path == "/language-resources")
                {
                    object source, target;
                    if (!data.TryGetValue("sourceLanguage", out source) || !data.TryGetValue("targetLanguage", out target) ||
                        !LanguageResources.IsLanguageTag(source as string) || !LanguageResources.IsLanguageTag(target as string))
                    { Reply(stream, 400, "text/plain", new byte[0]); return; }
                    Reply(stream, 200, "application/json", Encoding.UTF8.GetBytes(Serializer().Serialize(LanguageResources.Check((string)source, (string)target))));
                    return;
                }
                Message?.Invoke(path, data);
                Reply(stream, 200, "application/json", Encoding.UTF8.GetBytes("{\"ok\":true}"));
            }
        }

        public void Dispose()
        {
            stopped = true;
            listener.Stop();
            lock (gate)
            {
                foreach (TcpClient client in clients) client.Close();
                events.Clear();
            }
            changed.Set();
        }
    }
}