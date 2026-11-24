// --------------------------------------------------
// Umami 3DS Theme Editor - Themezer.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ThemeEditor.App
{
    /// <summary>What goes into a Themezer upload.</summary>
    public sealed class ThemezerUpload
    {
        public string Name;
        public string Description;
        public IList<string> Tags = new List<string>();
        public string BgmInfo;
        public bool IsPrivate;
        public bool IsNsfw;
        public byte[] Body;
        public byte[] Bgm;
        public byte[] Preview;
        public byte[] Icon;
    }

    /// <summary>A theme Themezer accepted.</summary>
    public sealed class ThemezerTheme
    {
        public string HexId;
        public string Name;
        public string PageUrl;
    }

    /// <summary>An error Themezer reported (a field it rejected, the hourly upload limit...) or a connection problem.</summary>
    public sealed class ThemezerException : Exception
    {
        public string Code { get; }

        /// <summary>The finer cause Themezer gives for some codes (extensions.reason), e.g. UNMARKED_NSFW.</summary>
        public string Reason { get; }

        public ThemezerException(string message, string code = null, Exception inner = null, string reason = null) : base(message, inner)
        {
            Code = code;
            Reason = reason;
        }
    }

    /// <summary>
    ///     Talks to Themezer's public GraphQL API (https://api.themezer.net/graphql) as a registered OAuth app: the user signs
    ///     in on Themezer's own page in their browser (authorization code with PKCE, so Discord and Google accounts work too and
    ///     the editor never sees a password), and every request carries the resulting access token.
    /// </summary>
    public sealed class ThemezerClient : IDisposable
    {
        public const string ApiUrl = "https://api.themezer.net/graphql";
        public const string SiteUrl = "https://themezer.net";
        public const string AuthorizeUrl = "https://themezer.net/oauth/authorize";
        public const string TokenUrl = "https://api.themezer.net/oauth/token";
        public const string RevokeUrl = "https://api.themezer.net/oauth/revoke";
        public const string ClientId = "umami-theme-editor";
        /// <summary>Who is signed in, and uploading; reading other content is not needed.</summary>
        public const string Scopes = "profile:read content:write";

        private static ThemezerClient _shared;

        /// <summary>The client the editor uses, so the sign-in holds until the editor is closed.</summary>
        public static ThemezerClient Shared => _shared ?? (_shared = new ThemezerClient());

        private readonly HttpClient _http;

        /// <summary>Who is signed in, or null.</summary>
        public string Username { get; private set; }

        /// <summary>The access token of the signed-in user (it does not expire), or null.</summary>
        public string Token { get; private set; }

        public ThemezerClient(HttpMessageHandler handler = null)
        {
            _http = handler == null ? new HttpClient() : new HttpClient(handler);
            _http.Timeout = TimeSpan.FromMinutes(3);
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + Update.AppVersion);
            // Apollo's CSRF protection lets file uploads through only with this header
            _http.DefaultRequestHeaders.Add("apollo-require-preflight", "true");
        }

        /// <summary>
        ///     Signs in through the user's browser: listens on a free port of 127.0.0.1, has <paramref name="openBrowser" />
        ///     open Themezer's consent page, waits for its answer and exchanges the code for a token. Returns who signed in.
        /// </summary>
        public async Task<string> SignIn(Action<string> openBrowser, CancellationToken cancel)
        {
            var verifier = RandomString();
            var state = RandomString();
            var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            string code;
            string redirectUri;
            try
            {
                redirectUri = "http://127.0.0.1:" + ((IPEndPoint) listener.LocalEndpoint).Port + "/callback";
                openBrowser(AuthorizeUrl + "?response_type=code&client_id=" + ClientId +
                            "&redirect_uri=" + Uri.EscapeDataString(redirectUri) + "&scope=" + Uri.EscapeDataString(Scopes) +
                            "&state=" + state + "&code_challenge=" + challenge + "&code_challenge_method=S256");
                code = await WaitForCode(listener, state, cancel);
            }
            finally
            {
                listener.Stop();
            }

            string body;
            HttpStatusCode status;
            try
            {
                using (var response = await _http.PostAsync(TokenUrl, new FormUrlEncodedContent(new Dictionary<string, string>
                       {
                           ["grant_type"] = "authorization_code",
                           ["code"] = code,
                           ["client_id"] = ClientId,
                           ["redirect_uri"] = redirectUri,
                           ["code_verifier"] = verifier
                       }), cancel))
                {
                    status = response.StatusCode;
                    body = await response.Content.ReadAsStringAsync();
                }
            }
            catch (HttpRequestException ex)
            {
                throw new ThemezerException(ex.Message, "NETWORK", ex);
            }
            JObject json;
            try
            {
                json = JObject.Parse(body);
            }
            catch (JsonException)
            {
                throw new ThemezerException("Themezer answered HTTP " + (int) status, "HTTP_" + (int) status);
            }
            var token = (string) json["access_token"];
            if (token == null)
                throw new ThemezerException((string) json["error_description"] ?? (string) json["error"] ?? "Themezer refused the sign-in",
                    (string) json["error"]);
            return await Use(token) ?? throw new ThemezerException("Themezer did not accept the new sign-in", "NOT_AUTHENTICATED");
        }

        /// <summary>
        ///     Waits for the browser's answer on <paramref name="listener" />: the request to /callback carrying our
        ///     <paramref name="state" />. Anything else on the port (a favicon, another program) gets a 404.
        /// </summary>
        private static async Task<string> WaitForCode(TcpListener listener, string state, CancellationToken cancel)
        {
            using (cancel.Register(listener.Stop))
                while (true)
                {
                    TcpClient client;
                    try
                    {
                        client = await listener.AcceptTcpClientAsync();
                    }
                    catch (Exception) when (cancel.IsCancellationRequested)
                    {
                        throw new OperationCanceledException(cancel);
                    }
                    using (client)
                    using (var stream = client.GetStream())
                    {
                        var target = await ReadRequestTarget(stream);
                        var query = ParseQuery(target);
                        var isAnswer = target != null && target.Split('?')[0] == "/callback" && query.TryGetValue("state", out var s) && s == state;
                        var text = isAnswer
                            ? query.ContainsKey("code") ? Localization.MainResources.Themezer_BrowserDone : Localization.MainResources.Themezer_BrowserDenied
                            : "Not found";
                        var bytes = Encoding.UTF8.GetBytes(text);
                        var head = Encoding.ASCII.GetBytes((isAnswer ? "HTTP/1.1 200 OK" : "HTTP/1.1 404 Not Found") +
                                                           "\r\nContent-Type: text/plain; charset=utf-8\r\nContent-Length: " + bytes.Length +
                                                           "\r\nConnection: close\r\n\r\n");
                        try
                        {
                            await stream.WriteAsync(head, 0, head.Length);
                            await stream.WriteAsync(bytes, 0, bytes.Length);
                        }
                        catch (IOException)
                        {
                        }
                        if (!isAnswer)
                            continue;
                        if (query.TryGetValue("code", out var code))
                            return code;
                        throw new ThemezerException(Localization.MainResources.Themezer_SignInDenied,
                            query.TryGetValue("error", out var error) ? error : "access_denied");
                    }
                }
        }

        /// <summary>The path and query of an HTTP request ("GET /callback?code=... HTTP/1.1"), or null.</summary>
        private static async Task<string> ReadRequestTarget(NetworkStream stream)
        {
            var buffer = new byte[8192];
            var length = 0;
            using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
                while (length < buffer.Length)
                {
                    int read;
                    try
                    {
                        read = await stream.ReadAsync(buffer, length, buffer.Length - length, timeout.Token);
                    }
                    catch (Exception)
                    {
                        return null;
                    }
                    if (read == 0)
                        break;
                    length += read;
                    var text = Encoding.ASCII.GetString(buffer, 0, length);
                    var end = text.IndexOf("\r\n", StringComparison.Ordinal);
                    if (end >= 0)
                    {
                        var parts = text.Substring(0, end).Split(' ');
                        return parts.Length >= 2 && parts[0] == "GET" ? parts[1] : null;
                    }
                }
            return null;
        }

        private static Dictionary<string, string> ParseQuery(string target)
        {
            var result = new Dictionary<string, string>();
            var q = target?.IndexOf('?') ?? -1;
            if (q < 0)
                return result;
            foreach (var pair in target.Substring(q + 1).Split('&'))
            {
                var eq = pair.IndexOf('=');
                if (eq > 0)
                    result[Uri.UnescapeDataString(pair.Substring(0, eq).Replace('+', ' '))] = Uri.UnescapeDataString(pair.Substring(eq + 1).Replace('+', ' '));
            }
            return result;
        }

        private static string RandomString() => Base64Url(RandomNumberGenerator.GetBytes(32));

        private static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Uses a saved access token; returns who it belongs to, or null when Themezer no longer accepts it.</summary>
        public async Task<string> Use(string token)
        {
            Token = token;
            Username = null;
            try
            {
                var data = await Send(Json("{ auth { me { username } } }", null));
                Username = (string) data.SelectToken("auth.me.username");
            }
            catch (ThemezerException ex) when (ex.Code == "NOT_AUTHENTICATED")
            {
            }
            if (Username == null)
                Token = null;
            return Username;
        }

        /// <summary>Signs out: Themezer revokes the token, and this client forgets it.</summary>
        public async Task SignOut()
        {
            var token = Token;
            Token = null;
            Username = null;
            if (token == null)
                return;
            try
            {
                using (var response = await _http.PostAsync(RevokeUrl, new FormUrlEncodedContent(new Dictionary<string, string> { ["token"] = token })))
                {
                }
            }
            catch (HttpRequestException)
            {
                // Offline: the token is forgotten here, and the user can still remove the app in their Themezer account
            }
        }

        public async Task<ThemezerTheme> UploadTheme(ThemezerUpload upload)
        {
            if (Token == null)
                throw new ThemezerException("Not signed in", "NOT_AUTHENTICATED");

            var d = new JObject
            {
                ["name"] = upload.Name,
                ["description"] = string.IsNullOrWhiteSpace(upload.Description) ? null : upload.Description,
                ["tags"] = new JArray(upload.Tags),
                ["isPrivate"] = upload.IsPrivate,
                ["isNSFW"] = upload.IsNsfw,
                ["externalEditor"] = true,
                ["body"] = null,
                ["preview"] = null,
                ["icon"] = null
            };
            var files = new List<(string Path, string Name, byte[] Data)>
            {
                ("variables.d.body", "body_LZ.bin", upload.Body),
                ("variables.d.preview", "preview.png", upload.Preview),
                ("variables.d.icon", "icon.png", upload.Icon)
            };
            if (upload.Bgm != null && upload.Bgm.Length > 0)
            {
                d["bgm"] = null;
                d["bgmInfo"] = upload.BgmInfo;
                files.Add(("variables.d.bgm", "bgm.bcstm", upload.Bgm));
            }

            var form = new MultipartFormDataContent();
            form.Add(new StringContent(new JObject
            {
                ["query"] = "mutation($d:CreateThreeDsThemeDataInput!){ threeDs { theme { createTheme(data:$d) { hexId name pageUrl } } } }",
                ["variables"] = new JObject { ["d"] = d }
            }.ToString(Formatting.None), Encoding.UTF8), "operations");
            var map = new JObject();
            for (var i = 0; i < files.Count; i++)
                map[i.ToString()] = new JArray(files[i].Path);
            form.Add(new StringContent(map.ToString(Formatting.None), Encoding.UTF8), "map");
            for (var i = 0; i < files.Count; i++)
            {
                var part = new ByteArrayContent(files[i].Data ?? new byte[0]);
                part.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
                form.Add(part, i.ToString(), files[i].Name);
            }

            var theme = (await Send(form)).SelectToken("threeDs.theme.createTheme");
            if (theme == null)
                throw new ThemezerException("Themezer did not return the new theme");
            return new ThemezerTheme
            {
                HexId = (string) theme["hexId"],
                Name = (string) theme["name"],
                PageUrl = (string) theme["pageUrl"]
            };
        }

        /// <summary>Tags as typed: separated by commas, trimmed, without empty ones and duplicates.</summary>
        public static List<string> ParseTags(string text)
        {
            return (text ?? string.Empty).Split(',', ';', '\n')
                .Select(t => t.Trim())
                .Where(t => t.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static HttpContent Json(string query, JObject variables)
        {
            var body = new JObject { ["query"] = query, ["variables"] = variables ?? new JObject() };
            return new StringContent(body.ToString(Formatting.None), Encoding.UTF8, "application/json");
        }

        /// <summary>Posts to the API and returns its "data", or throws the first error it reports.</summary>
        private async Task<JToken> Send(HttpContent content)
        {
            string text;
            HttpStatusCode status;
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl) { Content = content })
                {
                    if (Token != null)
                        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token);
                    using (var response = await _http.SendAsync(request))
                    {
                        status = response.StatusCode;
                        text = await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new ThemezerException(ex.Message, "NETWORK", ex);
            }

            JObject json = null;
            try
            {
                json = JObject.Parse(text);
            }
            catch (JsonException)
            {
            }
            var error = (json?["errors"] as JArray)?.FirstOrDefault();
            var code = (string) error?.SelectToken("extensions.code");
            // The token is wrong, was revoked, or the app was disabled: sign in again
            if (status == HttpStatusCode.Unauthorized || code == "NOT_AUTHENTICATED" || code == "UNAUTHENTICATED")
            {
                Token = null;
                Username = null;
                throw new ThemezerException((string) error?["message"] ?? "Not signed in", "NOT_AUTHENTICATED");
            }
            if (json == null)
                throw new ThemezerException("Themezer answered HTTP " + (int) status, "HTTP_" + (int) status);
            if (error != null)
                throw new ThemezerException((string) error["message"] ?? "Themezer reported an error", code, null,
                    (string) error.SelectToken("extensions.reason"));
            return json["data"] ?? new JObject();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
