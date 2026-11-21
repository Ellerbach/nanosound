using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using nanoFramework.Runtime.Native;
using nanoFramework.WebServer;

namespace nanoSound
{
    public sealed class SoundWebServer
    {
        private const string PagePath = "I:\\Web\\index.html";
        private const string ScriptPath = "I:\\Web\\app.js";
        private const string ConfigScriptPath = "I:\\Web\\config.js";

        private readonly SoundCatalog _catalog;
        private readonly WebServer _server;

        public SoundWebServer(SoundCatalog catalog)
        {
            _catalog = catalog;
            _server = new WebServer(80, HttpProtocol.Http, IPAddress.Parse(AccessPointService.Address));
            _server.CommandReceived += HandleRequest;
        }

        public void Start()
        {
            if (!File.Exists(PagePath))
            {
                Debug.WriteLine("The web page is not deployed yet; API endpoints remain available.");
            }

            _server.Start();
            Debug.WriteLine("Web server is ready at http://" + AccessPointService.Address + "/");
        }

        private void HandleRequest(object source, WebServerEventArgs args)
        {
            try
            {
                string url = args.Context.Request.RawUrl;
                if (string.IsNullOrEmpty(url))
                {
                    WebServer.OutputHttpCode(args.Context.Response, HttpStatusCode.BadRequest);
                }
                else if (args.Context.Request.HttpMethod == "POST" && url == "/api/network")
                {
                    UpdateNetwork(args);
                }
                else if (args.Context.Request.HttpMethod == "POST" && url == "/api/config")
                {
                    ImportConfiguration(args);
                }
                else if (args.Context.Request.HttpMethod != "GET")
                {
                    WebServer.OutputHttpCode(args.Context.Response, HttpStatusCode.MethodNotAllowed);
                }
                else if (IsPageRequest(url))
                {
                    ServePage(args);
                }
                else if (url == "/app.js")
                {
                    ServeScript(args, ScriptPath);
                }
                else if (url == "/config.js")
                {
                    ServeScript(args, ConfigScriptPath);
                }
                else if (url.IndexOf("/api/state") == 0)
                {
                    OutputState(args);
                }
                else if (url == "/api/config")
                {
                    OutputConfiguration(args);
                }
                else if (url.IndexOf("/api/volume?") == 0)
                {
                    UpdateVolume(args);
                }
                else if (url.IndexOf("/api/group?") == 0)
                {
                    UpdateGroup(args);
                }
                else if (url.IndexOf("/api/track?") == 0)
                {
                    UpdateTrack(args);
                }
                else if (url.IndexOf("/api/play?") == 0)
                {
                    QueueTrack(args);
                }
                else if (url.IndexOf("/api/time?") == 0)
                {
                    SynchronizeTime(args);
                }
                else if (url.IndexOf("/api/reset") == 0)
                {
                    _catalog.Reset();
                    OutputOk(args);
                }
                else
                {
                    WebServer.OutputHttpCode(args.Context.Response, HttpStatusCode.NotFound);
                }
            }
            catch (Exception exception)
            {
                Debug.WriteLine("HTTP request failed: " + exception.Message);
                try
                {
                    args.Context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    WebServer.OutputAsStream(args.Context.Response, "Request failed.");
                }
                catch
                {
                }
            }
        }

        private static void ServePage(WebServerEventArgs args)
        {
            if (!File.Exists(PagePath))
            {
                args.Context.Response.StatusCode = (int)HttpStatusCode.ServiceUnavailable;
                WebServer.OutputAsStream(args.Context.Response, "The nanoSound web page has not been deployed yet.");
                return;
            }

            args.Context.Response.ContentType = "text/html; charset=utf-8";
            args.Context.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
            args.Context.Response.KeepAlive = false;
            Debug.WriteLine("Serving " + PagePath + " (" + new FileInfo(PagePath).Length.ToString() + " bytes).");
            WebServer.SendFileOverHTTP(args.Context.Response, PagePath, "text/html; charset=utf-8");
        }

        private static void ServeScript(WebServerEventArgs args, string path)
        {
            if (!File.Exists(path))
            {
                WebServer.OutputHttpCode(args.Context.Response, HttpStatusCode.NotFound);
                return;
            }

            args.Context.Response.Headers.Add("Cache-Control", "no-store, no-cache, must-revalidate");
            args.Context.Response.KeepAlive = false;
            WebServer.SendFileOverHTTP(args.Context.Response, path, "text/javascript; charset=utf-8");
        }

        private static bool IsPageRequest(string url)
        {
            return url == "/" ||
                url.IndexOf("/index.html") == 0 ||
                url.IndexOf("/generate_204") == 0 ||
                url.IndexOf("/hotspot-detect.html") == 0 ||
                url.IndexOf("/connecttest.txt") == 0 ||
                url.IndexOf("/ncsi.txt") == 0;
        }

        private void OutputState(WebServerEventArgs args)
        {
            SoundGroup[] groups = _catalog.GetGroups();
            StringBuilder json = new StringBuilder();
            json.Append("{\"volume\":").Append(_catalog.Volume.ToString())
                .Append(",\"ssid\":\"").Append(EscapeJson(_catalog.AccessPointName)).Append("\"")
                .Append(",\"timeZoneOffset\":").Append(_catalog.TimeZoneOffsetMinutes.ToString())
                .Append(",\"utc\":\"").Append(EscapeJson(DateTime.UtcNow.ToString("o"))).Append("\",\"groups\":[");

            for (int groupIndex = 0; groupIndex < groups.Length; groupIndex++)
            {
                SoundGroup group = groups[groupIndex];
                if (groupIndex > 0) json.Append(',');
                json.Append("{\"folder\":").Append(group.Number.ToString())
                    .Append(",\"name\":\"").Append(EscapeJson(group.Name)).Append("\"")
                    .Append(",\"random\":").Append(group.RandomEnabled ? "true" : "false")
                    .Append(",\"min\":").Append(group.MinimumDelaySeconds.ToString())
                    .Append(",\"max\":").Append(group.MaximumDelaySeconds.ToString())
                    .Append(",\"tracks\":[");
                for (int trackIndex = 0; trackIndex < group.Tracks.Length; trackIndex++)
                {
                    SoundTrack track = group.Tracks[trackIndex];
                    if (trackIndex > 0) json.Append(',');
                    json.Append("{\"track\":").Append(track.Number.ToString())
                        .Append(",\"name\":\"").Append(EscapeJson(track.Name)).Append("\"")
                        .Append(",\"schedule\":\"").Append(EscapeJson(track.Schedule)).Append("\"}");
                }

                json.Append("]}");
            }

            json.Append("]}");
            OutputJson(args, json.ToString());
        }

        private void OutputConfiguration(WebServerEventArgs args)
        {
            args.Context.Response.ContentType = "text/plain; charset=utf-8";
            args.Context.Response.Headers.Add("Content-Disposition", "attachment; filename=nanoSound-config.txt");
            args.Context.Response.Headers.Add("Cache-Control", "no-store");
            args.Context.Response.KeepAlive = false;
            WebServer.OutputAsStream(args.Context.Response, _catalog.ExportConfiguration());
        }

        private void ImportConfiguration(WebServerEventArgs args)
        {
            if (!TryReadRequestBody(args, out string configuration))
            {
                OutputBadRequest(args, "Invalid request body.");
                return;
            }

            try
            {
                _catalog.ImportConfiguration(configuration);
                OutputOk(args);
            }
            catch (ArgumentException exception)
            {
                OutputBadRequest(args, exception.Message);
            }
        }

        private void UpdateVolume(WebServerEventArgs args)
        {
            UrlParameter[] parameters = WebServer.DecodeParam(args.Context.Request.RawUrl);
            if (!TryGetInt(parameters, "value", out int volume))
            {
                OutputBadRequest(args, "Invalid volume.");
                return;
            }

            _catalog.UpdateVolume(volume);
            OutputOk(args);
        }

        private void UpdateGroup(WebServerEventArgs args)
        {
            UrlParameter[] parameters = WebServer.DecodeParam(args.Context.Request.RawUrl);
            if (!TryGetInt(parameters, "folder", out int folder) ||
                !TryGetString(parameters, "name", out string name) ||
                !TryGetInt(parameters, "random", out int random) ||
                !TryGetInt(parameters, "min", out int minimum) ||
                !TryGetInt(parameters, "max", out int maximum) ||
                (random != 0 && random != 1))
            {
                OutputBadRequest(args, "Invalid group settings.");
                return;
            }

            _catalog.UpdateGroup(folder, name, random == 1, minimum, maximum);
            OutputOk(args);
        }

        private void UpdateTrack(WebServerEventArgs args)
        {
            UrlParameter[] parameters = WebServer.DecodeParam(args.Context.Request.RawUrl);
            if (!TryGetInt(parameters, "folder", out int folder) ||
                !TryGetInt(parameters, "track", out int track) ||
                !TryGetString(parameters, "name", out string name) ||
                !TryGetString(parameters, "schedule", out string schedule))
            {
                OutputBadRequest(args, "Invalid sound settings.");
                return;
            }

            try
            {
                _catalog.UpdateTrack(folder, track, name, schedule);
                OutputOk(args);
            }
            catch (ArgumentException)
            {
                OutputBadRequest(args, "Use HH:mm or *:mm schedules separated by commas.");
            }
        }

        private void QueueTrack(WebServerEventArgs args)
        {
            UrlParameter[] parameters = WebServer.DecodeParam(args.Context.Request.RawUrl);
            if (!TryGetInt(parameters, "folder", out int folder) || !TryGetInt(parameters, "track", out int track))
            {
                OutputBadRequest(args, "Unknown folder or sound.");
                return;
            }

            _catalog.QueueTrack(folder, track);
            OutputOk(args);
        }

        private void SynchronizeTime(WebServerEventArgs args)
        {
            UrlParameter[] parameters = WebServer.DecodeParam(args.Context.Request.RawUrl);
            if (!TryGetLong(parameters, "epoch", out long epochSeconds) ||
                !TryGetInt(parameters, "offset", out int offsetMinutes) ||
                epochSeconds < 1577836800L || epochSeconds > 4102444800L)
            {
                OutputBadRequest(args, "Invalid date or time zone.");
                return;
            }

            DateTime utc = new DateTime(1970, 1, 1).AddSeconds(epochSeconds);
            if (!Rtc.SetSystemTime(utc))
            {
                throw new InvalidOperationException("The device clock could not be updated.");
            }

            _catalog.SetTimeZoneOffsetMinutes(offsetMinutes);
            OutputOk(args);
        }

        private void UpdateNetwork(WebServerEventArgs args)
        {
            if (!TryReadRequestBody(args, out string body))
            {
                OutputBadRequest(args, "Invalid request body.");
                return;
            }

            UrlParameter[] parameters = WebServer.DecodeParam("/api/network?" + body);
            if (!TryGetString(parameters, "ssid", out string ssid) ||
                !TryGetString(parameters, "password", out string password))
            {
                OutputBadRequest(args, "Invalid access point settings.");
                return;
            }

            if (string.IsNullOrEmpty(password))
            {
                password = _catalog.AccessPointPassword;
            }

            try
            {
                _catalog.UpdateAccessPoint(ssid, password);
                OutputOk(args);
            }
            catch (ArgumentOutOfRangeException)
            {
                OutputBadRequest(args, "The network name must be 1-32 characters and the password 8-63 characters.");
            }
        }

        private static bool TryReadRequestBody(WebServerEventArgs args, out string body)
        {
            body = null;
            long contentLength = args.Context.Request.ContentLength64;
            if (contentLength <= 0 || contentLength > 65535)
            {
                return false;
            }

            byte[] data = new byte[(int)contentLength];
            int offset = 0;
            while (offset < data.Length)
            {
                int bytesRead = args.Context.Request.InputStream.Read(data, offset, data.Length - offset);
                if (bytesRead <= 0)
                {
                    return false;
                }

                offset += bytesRead;
            }

            body = Encoding.UTF8.GetString(data, 0, data.Length);
            return true;
        }

        private static bool TryGetInt(UrlParameter[] parameters, string name, out int value)
        {
            value = 0;
            return TryGetString(parameters, name, out string text) && int.TryParse(text, out value);
        }

        private static bool TryGetLong(UrlParameter[] parameters, string name, out long value)
        {
            value = 0;
            return TryGetString(parameters, name, out string text) && long.TryParse(text, out value);
        }

        private static bool TryGetString(UrlParameter[] parameters, string name, out string value)
        {
            value = null;
            if (parameters == null) return false;
            foreach (UrlParameter parameter in parameters)
            {
                if (parameter.Name == name)
                {
                    value = parameter.Value ?? string.Empty;
                    return true;
                }
            }

            return false;
        }

        private static string EscapeJson(string value)
        {
            if (value == null) return string.Empty;
            StringBuilder escaped = new StringBuilder();
            foreach (char character in value)
            {
                if (character == '\\') escaped.Append("\\\\");
                else if (character == '"') escaped.Append("\\\"");
                else if (character == '\r') escaped.Append("\\r");
                else if (character == '\n') escaped.Append("\\n");
                else escaped.Append(character);
            }

            return escaped.ToString();
        }

        private static void OutputOk(WebServerEventArgs args)
        {
            OutputJson(args, "{\"ok\":true}");
        }

        private static void OutputJson(WebServerEventArgs args, string json)
        {
            args.Context.Response.ContentType = "application/json";
            WebServer.OutputAsStream(args.Context.Response, json);
        }

        private static void OutputBadRequest(WebServerEventArgs args, string message)
        {
            args.Context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
            WebServer.OutputAsStream(args.Context.Response, message);
        }
    }
}