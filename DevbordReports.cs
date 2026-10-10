using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Devbord
{
    /// <summary>
    /// Sends bug reports from a Unity game to devbord, where each report becomes a card.
    /// Use a "Report only" API key: it can only submit reports to one list and reads nothing,
    /// so it is safe to ship inside the game.
    /// </summary>
    public static class DevbordReports
    {
        /// <summary>Base address of devbord, for example https://devbord.com.</summary>
        public static string Url = "https://devbord.com";

        /// <summary>A "Report only" API key of the board for bugs.</summary>
        public static string ApiKey = "";

        /// <summary>How many of the latest log lines are sent along as logs once <see cref="CaptureLogs"/> was called.</summary>
        public static int LogLines = 40;

        private static readonly Queue<string> recentLogs = new Queue<string>();
        private static bool capturing;

        /// <summary>What to report. Only <see cref="Description"/> or <see cref="Title"/> is required.</summary>
        public class Report
        {
            public string Title;
            public string Description;
            /// <summary>Your own reference to the player (account or player ID) and their display name.</summary>
            public string ReporterRef;
            public string ReporterName;
            /// <summary>Reports with the same fingerprint are collected on one open card.</summary>
            public string Fingerprint;
            /// <summary>Output or a stack trace; defaults to the captured log lines.</summary>
            public string Logs;
            /// <summary>low, normal, high or urgent.</summary>
            public string Priority;
            public Dictionary<string, string> Fields = new Dictionary<string, string>();
            public List<string> Labels = new List<string> { "Bug" };
            /// <summary>Attach a screenshot of the current frame as the card cover.</summary>
            public bool Screenshot = true;
        }

        /// <summary>The answer of devbord. <see cref="ok"/> is false when sending failed; see <see cref="error"/> and <see cref="code"/>.</summary>
        [Serializable]
        public class Result
        {
            public bool ok;
            public int number;
            public string url;
            public bool duplicate;
            public int reports;
            /// <summary>Keep this to ask for the status later with <see cref="Check"/>.</summary>
            public string receipt;
            public string error;
            public string code;
        }

        /// <summary>State of a report: open, completed (fixed) or archived.</summary>
        [Serializable]
        public class Status
        {
            public string receipt;
            public int number;
            public string state;
            public string list;
            public int reports;
        }

        /// <summary>Starts keeping the latest log lines, so reports carry what happened just before.</summary>
        public static void CaptureLogs()
        {
            if (capturing) return;
            capturing = true;
            Application.logMessageReceived += (message, stackTrace, type) =>
            {
                lock (recentLogs)
                {
                    recentLogs.Enqueue("[" + type + "] " + message + (type == LogType.Exception || type == LogType.Error ? "\n" + stackTrace : ""));
                    while (recentLogs.Count > LogLines) recentLogs.Dequeue();
                }
            };
        }

        /// <summary>Sends a report. Run it with StartCoroutine; <paramref name="done"/> is always called once.</summary>
        public static IEnumerator Send(Report report, Action<Result> done)
        {
            string screenshot = null;
            if (report.Screenshot)
            {
                yield return new WaitForEndOfFrame();
                Texture2D frame = ScreenCapture.CaptureScreenshotAsTexture();
                screenshot = "data:image/jpeg;base64," + Convert.ToBase64String(frame.EncodeToJPG(60));
                UnityEngine.Object.Destroy(frame);
            }

            var fields = new Dictionary<string, string>
            {
                { "Version", Application.version },
                { "Platform", Application.platform.ToString() },
                { "Scene", SceneManager.GetActiveScene().name },
                { "System", SystemInfo.operatingSystem },
                { "Device", SystemInfo.deviceModel },
                { "Graphics", SystemInfo.graphicsDeviceName },
            };
            foreach (KeyValuePair<string, string> field in report.Fields) fields[field.Key] = field.Value;

            string logs = report.Logs;
            if (logs == null && capturing)
            {
                lock (recentLogs) logs = string.Join("\n", recentLogs.ToArray());
            }

            var json = new StringBuilder("{");
            Append(json, "title", report.Title);
            Append(json, "description", report.Description);
            Append(json, "fingerprint", report.Fingerprint);
            Append(json, "priority", report.Priority);
            Append(json, "logs", logs);
            Append(json, "screenshot", screenshot);
            if (!string.IsNullOrEmpty(report.ReporterRef) || !string.IsNullOrEmpty(report.ReporterName))
            {
                json.Append("\"reporter\":{\"ref\":").Append(Quote(report.ReporterRef ?? "")).Append(",\"name\":").Append(Quote(report.ReporterName ?? "")).Append("},");
            }
            json.Append("\"labels\":[");
            for (int i = 0; i < report.Labels.Count; i++) json.Append(i == 0 ? "" : ",").Append(Quote(report.Labels[i]));
            json.Append("],\"fields\":{");
            bool first = true;
            foreach (KeyValuePair<string, string> field in fields)
            {
                if (string.IsNullOrEmpty(field.Value)) continue;
                json.Append(first ? "" : ",").Append(Quote(field.Key)).Append(':').Append(Quote(field.Value));
                first = false;
            }
            json.Append("}}");

            using (UnityWebRequest request = Request("POST", "/reports", json.ToString()))
            {
                request.SetRequestHeader("Idempotency-Key", Guid.NewGuid().ToString());
                yield return request.SendWebRequest();
                Result result = Parse<Result>(request) ?? new Result();
                result.ok = request.responseCode == 200 || request.responseCode == 201;
                if (!result.ok && string.IsNullOrEmpty(result.error)) result.error = request.error;
                done(result);
            }
        }

        /// <summary>Asks whether a report was fixed. <paramref name="done"/> gets null when the request failed.</summary>
        public static IEnumerator Check(string receipt, Action<Status> done)
        {
            using (UnityWebRequest request = Request("GET", "/reports/" + UnityWebRequest.EscapeURL(receipt), null))
            {
                yield return request.SendWebRequest();
                done(request.responseCode == 200 ? Parse<Status>(request) : null);
            }
        }

        private static UnityWebRequest Request(string method, string path, string body)
        {
            var request = new UnityWebRequest(Url.TrimEnd('/') + "/api/v1" + path, method);
            if (body != null) request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = 30;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + ApiKey);
            return request;
        }

        private static T Parse<T>(UnityWebRequest request) where T : class
        {
            try { return JsonUtility.FromJson<T>(request.downloadHandler.text); }
            catch (Exception) { return null; }
        }

        private static void Append(StringBuilder json, string name, string value)
        {
            if (!string.IsNullOrEmpty(value)) json.Append(Quote(name)).Append(':').Append(Quote(value)).Append(',');
        }

        private static string Quote(string value)
        {
            var quoted = new StringBuilder("\"");
            foreach (char c in value)
            {
                if (c == '"' || c == '\\') quoted.Append('\\').Append(c);
                else if (c == '\n') quoted.Append("\\n");
                else if (c < 0x20) quoted.Append("\\u").Append(((int)c).ToString("x4"));
                else quoted.Append(c);
            }
            return quoted.Append('"').ToString();
        }
    }
}
