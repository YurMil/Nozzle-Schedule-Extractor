using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    internal static class TextUtil
    {
        public static string Normalize(string value)
        {
            return Regex.Replace(value ?? "", @"\s+", " ").Trim();
        }

        public static IEnumerable<string> Lines(string text)
        {
            return (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        }

        public static string NormId(string id)
        {
            return (id ?? "").Trim().TrimEnd('.');
        }

        public static string DisplayNumberSortKey(string id)
        {
            Match m = Regex.Match(id ?? "", @"N\.(\d+)([A-Z*]*)", RegexOptions.IgnoreCase);
            if (!m.Success) return "9999-" + id;
            return Int32.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture).ToString("0000", CultureInfo.InvariantCulture) + "-" + m.Groups[2].Value.Replace("*", "");
        }

        public static string Fmt(string raw)
        {
            double d;
            raw = (raw ?? "").Trim();
            Match number = Regex.Match(raw, @"-?(?:\d+(?:[\.,]\d+)?|[\.,]\d+)");
            if (number.Success)
                raw = number.Value;
            raw = raw.Trim().TrimEnd('.').Replace(",", ".");
            if (!Double.TryParse(raw.StartsWith(".") ? "0" + raw : raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                return raw;
            if (Math.Abs(d - Math.Round(d)) < 0.0000001)
                return ((int)Math.Round(d)).ToString(CultureInfo.InvariantCulture);
            return d.ToString("0.####", CultureInfo.InvariantCulture).Replace(".", ",");
        }

        public static string OrDash(string value)
        {
            return IsBlank(value) ? "-" : value;
        }

        public static bool IsBlank(string value)
        {
            return String.IsNullOrWhiteSpace(value) || value.Trim() == "-";
        }

        public static string CleanDescription(string value)
        {
            value = Normalize(value).Trim(' ', ',', '-');
            return OrDash(value);
        }

        public static string Xml(string value)
        {
            return (value ?? "").Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
        }

        public static string ColumnName(int index)
        {
            string name = "";
            while (index > 0)
            {
                int rem = (index - 1) % 26;
                name = (char)('A' + rem) + name;
                index = (index - 1) / 26;
            }
            return name;
        }
    }
}
