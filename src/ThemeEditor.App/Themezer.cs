// --------------------------------------------------
// Umami 3DS Theme Editor - Themezer.cs
// --------------------------------------------------

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
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

    /// <summary>An error Themezer reported (wrong password, a field it rejected...) or a connection problem.</summary>
    public sealed class ThemezerException : Exception
    {
        public string Code { get; }

        public ThemezerException(string message, string code = null, Exception inner = null) : base(message, inner)
        {
            Code = code;
        }
    }

    /// <summary>
    ///     Talks to Themezer's public GraphQL API (https://api.themezer.net/graphql): log in with email and password, upload
    ///     a 3DS theme, log out. The login is a session cookie that lives as long as this client, never written to disk;
    ///     the password is only sent once and not kept.
    /// </summary>
    public sealed class ThemezerClient : IDisposable
    {
        public const string ApiUrl = "https://api.themezer.net/graphql";
        public const string SiteUrl = "https://themezer.net";

        private static ThemezerClient _shared;

        /// <summary>The client the editor uses, so the login holds until the editor is closed.</summary>
        public static ThemezerClient Shared => _shared ?? (_shared = new ThemezerClient());

        private readonly HttpClient _http;
        private readonly CookieContainer _cookies;
        private static readonly Uri ApiUri = new Uri(ApiUrl);
        private const string SessionCookie = "connect.sid";

        /// <summary>Who is logged in, or null.</summary>
        public string Username { get; private set; }

        /// <param name="cookies">The cookies <paramref name="handler" /> keeps, when it is given (for tests).</param>
        public ThemezerClient(HttpMessageHandler handler = null, CookieContainer cookies = null)
        {
            _cookies = cookies;
            if (handler == null)
            {
                _cookies = new CookieContainer();
                handler = new HttpClientHandler { CookieContainer = _cookies, UseCookies = true };
            }
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromMinutes(3) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("UmamiThemeEditor/" + Update.AppVersion);
            // Apollo's CSRF protection lets file uploads through only with this header
            _http.DefaultRequestHeaders.Add("apollo-require-preflight", "true");
        }

        public async Task<string> LogIn(string email, string password)
        {
            var data = await Send(Json(
                "mutation($d:LoginUserInput!){ auth { login(data:$d) { username } } }",
                new JObject { ["d"] = new JObject { ["email"] = email, ["password"] = password } }));
            Username = (string) data.SelectToken("auth.login.username");
            return Username;
        }

        /// <summary>The login session, to be kept in the password vault and resumed on the next start (null when none).</summary>
        public string Session => _cookies?.GetCookies(ApiUri)[SessionCookie]?.Value;

        /// <summary>
        ///     Resumes a saved session; returns who is logged in, or null when Themezer no longer knows the session (it
        ///     expired or was logged out elsewhere).
        /// </summary>
        public async Task<string> Resume(string session)
        {
            if (string.IsNullOrEmpty(session))
                return null;
            _cookies?.Add(ApiUri, new Cookie(SessionCookie, session, "/"));
            var data = await Send(Json("{ auth { me { username } } }", null));
            Username = (string) data.SelectToken("auth.me.username");
            return Username;
        }

        public async Task LogOut()
        {
            try
            {
                if (Username != null)
                    await Send(Json("mutation { auth { logout } }", null));
            }
            finally
            {
                Username = null;
            }
        }

        public async Task<ThemezerTheme> UploadTheme(ThemezerUpload upload)
        {
            if (Username == null)
                throw new ThemezerException("Not logged in", "UNAUTHENTICATED");

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
                using (var response = await _http.PostAsync(ApiUrl, content))
                {
                    status = response.StatusCode;
                    text = await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex) when (ex is HttpRequestException || ex is TaskCanceledException)
            {
                throw new ThemezerException(ex.Message, "NETWORK", ex);
            }

            JObject json;
            try
            {
                json = JObject.Parse(text);
            }
            catch (JsonException)
            {
                throw new ThemezerException("Themezer answered HTTP " + (int) status, "HTTP_" + (int) status);
            }
            var error = (json["errors"] as JArray)?.FirstOrDefault();
            if (error != null)
            {
                var code = (string) error.SelectToken("extensions.code");
                if (code == "UNAUTHENTICATED" || code == "FORBIDDEN")
                    Username = null;
                throw new ThemezerException((string) error["message"] ?? "Themezer reported an error", code);
            }
            return json["data"] ?? new JObject();
        }

        public void Dispose()
        {
            _http.Dispose();
        }
    }
}
