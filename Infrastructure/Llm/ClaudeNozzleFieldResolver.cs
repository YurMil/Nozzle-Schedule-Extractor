using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Anthropic-backed implementation of <see cref="INozzleFieldResolver"/>. Asks Claude
    /// to read a raw report section and return JSON values for the requested fields.
    ///
    /// Dependency-free on purpose (HttpWebRequest + hand-built JSON) so it compiles under
    /// the in-box .NET Framework toolchain. It is defensive: any failure yields an empty
    /// suggestion list so enabling the hybrid mode can never break extraction.
    ///
    /// NOT wired into the default pipeline yet. See the Stage 3 hybrid-extraction issue
    /// for the work needed to enable it (key provisioning, parser integration, hardened
    /// JSON parsing, caching, tests).
    /// </summary>
    internal sealed class ClaudeNozzleFieldResolver : INozzleFieldResolver
    {
        private const string SystemPrompt =
            "You extract pressure-vessel nozzle data from VVD/PVElite report text. " +
            "Return ONLY a compact JSON object mapping each requested field name to a string value. " +
            "Use an empty string when the value is not present. Do not add commentary.";

        private readonly HybridResolverOptions _options;

        public ClaudeNozzleFieldResolver(HybridResolverOptions options)
        {
            if (options == null) throw new ArgumentNullException("options");
            _options = options;
        }

        public List<FieldSuggestion> Resolve(string sectionText, IEnumerable<string> fields)
        {
            var fieldList = new List<string>(fields ?? new string[0]);
            var result = new List<FieldSuggestion>();
            if (!_options.Enabled || string.IsNullOrEmpty(_options.ApiKey) || fieldList.Count == 0)
                return result;

            try
            {
                string responseJson = Post(BuildRequestBody(sectionText, fieldList));
                string modelText = ExtractFirstTextBlock(responseJson);
                Dictionary<string, string> values = ExtractFieldValues(modelText, fieldList);
                foreach (string field in fieldList)
                {
                    string value;
                    if (values.TryGetValue(field, out value) && !TextUtil.IsBlank(value))
                        result.Add(new FieldSuggestion(field, value, Confidence.Low));
                }
            }
            catch
            {
                // Advisory only: never propagate resolver failures into extraction.
                return new List<FieldSuggestion>();
            }
            return result;
        }

        private string BuildRequestBody(string sectionText, List<string> fields)
        {
            var sb = new StringBuilder();
            sb.Append("{\"model\":\"").Append(JsonEscape(_options.Model)).Append("\",");
            sb.Append("\"max_tokens\":").Append(_options.MaxTokens).Append(',');
            sb.Append("\"system\":\"").Append(JsonEscape(SystemPrompt)).Append("\",");
            sb.Append("\"messages\":[{\"role\":\"user\",\"content\":\"");

            var prompt = new StringBuilder();
            prompt.Append("Fields: ").Append(string.Join(", ", fields.ToArray())).Append('\n');
            prompt.Append("Report section:\n").Append(sectionText ?? "");
            sb.Append(JsonEscape(prompt.ToString()));
            sb.Append("\"}]}");
            return sb.ToString();
        }

        private string Post(string body)
        {
            try { ServicePointManager.SecurityProtocol = (SecurityProtocolType)3072; } catch { }

            var request = (HttpWebRequest)WebRequest.Create(_options.Endpoint);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Timeout = _options.TimeoutMs;
            request.Headers["x-api-key"] = _options.ApiKey;
            request.Headers["anthropic-version"] = _options.AnthropicVersion;

            byte[] payload = Encoding.UTF8.GetBytes(body);
            request.ContentLength = payload.Length;
            using (Stream stream = request.GetRequestStream())
                stream.Write(payload, 0, payload.Length);

            using (var response = (HttpWebResponse)request.GetResponse())
            using (var reader = new StreamReader(response.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        // Pulls the first content[].text value out of the Messages API response.
        private static string ExtractFirstTextBlock(string responseJson)
        {
            Match m = Regex.Match(responseJson ?? "", "\"type\"\\s*:\\s*\"text\"\\s*,\\s*\"text\"\\s*:\\s*\"(?<t>(?:\\\\.|[^\"\\\\])*)\"");
            if (!m.Success)
                m = Regex.Match(responseJson ?? "", "\"text\"\\s*:\\s*\"(?<t>(?:\\\\.|[^\"\\\\])*)\"");
            return m.Success ? JsonUnescape(m.Groups["t"].Value) : "";
        }

        // Reads "Field":"value" pairs for the requested fields out of the model's JSON.
        private static Dictionary<string, string> ExtractFieldValues(string modelText, List<string> fields)
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (string field in fields)
            {
                Match m = Regex.Match(modelText ?? "",
                    "\"" + Regex.Escape(field) + "\"\\s*:\\s*\"(?<v>(?:\\\\.|[^\"\\\\])*)\"");
                if (m.Success)
                    values[field] = JsonUnescape(m.Groups["v"].Value).Trim();
            }
            return values;
        }

        private static string JsonEscape(string value)
        {
            var sb = new StringBuilder();
            foreach (char c in value ?? "")
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static string JsonUnescape(string value)
        {
            return (value ?? "")
                .Replace("\\n", "\n").Replace("\\r", "\r").Replace("\\t", "\t")
                .Replace("\\\"", "\"").Replace("\\/", "/").Replace("\\\\", "\\");
        }
    }
}
