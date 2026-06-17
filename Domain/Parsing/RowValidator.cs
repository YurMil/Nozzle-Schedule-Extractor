using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Post-parse sanity checks and provenance/conflict detection. Populates each
    /// row's <see cref="NozzleRow.Diagnostics"/> and sets <see cref="NozzleRow.Confidence"/>.
    /// Pure and additive: it never changes parsed values.
    /// </summary>
    internal static class RowValidator
    {
        // Nominal outside diameters (mm) for EN/DIN pipe nominal sizes, used to sanity-check
        // SIZE (DNxx) against the parsed pipe outside diameter.
        private static readonly Dictionary<int, double> DnToOd = new Dictionary<int, double>
        {
            { 10, 17.2 }, { 15, 21.3 }, { 20, 26.9 }, { 25, 33.7 }, { 32, 42.4 },
            { 40, 48.3 }, { 50, 60.3 }, { 65, 76.1 }, { 80, 88.9 }, { 100, 114.3 },
            { 125, 139.7 }, { 150, 168.3 }, { 200, 219.1 }, { 250, 273.0 }, { 300, 323.9 },
            { 350, 355.6 }, { 400, 406.4 }, { 450, 457.0 }, { 500, 508.0 }, { 600, 610.0 }
        };

        private static readonly HashSet<int> KnownPn = new HashSet<int>
        {
            6, 10, 16, 25, 40, 63, 100, 160, 250, 320, 400
        };

        private static readonly HashSet<int> KnownClass = new HashSet<int>
        {
            150, 300, 400, 600, 900, 1500, 2500
        };

        // Loads above this magnitude almost always indicate a column/whitespace leak.
        private const double ImplausibleLoad = 100000.0;

        private static readonly string[] LoadKeys = { "Fx", "Fy", "Fz", "Mx", "My", "Mz" };

        public static void Validate(NozzleRow row)
        {
            ValidateGeometry(row);
            ValidateDnVersusOd(row);
            ValidatePressureClass(row);
            ValidateLoads(row);
            ValidateCompleteness(row);
            DetectConflicts(row);
        }

        private static void ValidateGeometry(NozzleRow row)
        {
            double od, wt;
            if (!TryPipe(row.PipeDimension, out od, out wt)) return;

            if (od <= 0 || wt <= 0)
                Warn(row, "PipeDimension", "Non-positive pipe geometry: " + row.PipeDimension);
            else if (wt >= od / 2.0)
                Warn(row, "PipeDimension", "Wall thickness >= radius (likely misparse): " + row.PipeDimension);
        }

        private static void ValidateDnVersusOd(NozzleRow row)
        {
            int dn;
            double od, wt, expectedOd;
            if (!TryDn(row.Size, out dn)) return;
            if (!TryPipe(row.PipeDimension, out od, out wt)) return;
            if (!DnToOd.TryGetValue(dn, out expectedOd)) return;

            if (Math.Abs(od - expectedOd) > Math.Max(2.0, expectedOd * 0.02))
                Warn(row, "Size", string.Format(CultureInfo.InvariantCulture,
                    "SIZE DN{0} (OD~{1}) does not match pipe OD {2}", dn, expectedOd, od));
        }

        private static void ValidatePressureClass(NozzleRow row)
        {
            if (TextUtil.IsBlank(row.PressureClass)) return;

            Match pn = Regex.Match(row.PressureClass, @"PN\s*(\d+)", RegexOptions.IgnoreCase);
            if (pn.Success && !KnownPn.Contains(int.Parse(pn.Groups[1].Value, CultureInfo.InvariantCulture)))
                Warn(row, "PressureClass", "Unknown PN rating: " + row.PressureClass);

            Match cls = Regex.Match(row.PressureClass, @"Class\s*(\d+)", RegexOptions.IgnoreCase);
            if (cls.Success && !KnownClass.Contains(int.Parse(cls.Groups[1].Value, CultureInfo.InvariantCulture)))
                Warn(row, "PressureClass", "Unknown ASME class: " + row.PressureClass);
        }

        private static void ValidateLoads(NozzleRow row)
        {
            foreach (string key in LoadKeys)
            {
                string raw;
                if (!row.Loads.TryGetValue(key, out raw)) continue;
                double v;
                if (TryNum(raw, out v) && Math.Abs(v) > ImplausibleLoad)
                    Warn(row, key, "Implausible load magnitude: " + raw);
            }
        }

        private static void ValidateCompleteness(NozzleRow row)
        {
            if (TextUtil.IsBlank(row.Size))
                Warn(row, "Size", "Missing size");
            if (TextUtil.IsBlank(row.Material))
                Warn(row, "Material", "Missing material");
            if (TextUtil.IsBlank(row.PipeDimension))
                Info(row, "PipeDimension", "Missing pipe dimension");
            if (TextUtil.IsBlank(row.NozzleType))
                Info(row, "NozzleType", "Missing nozzle type");
        }

        private static void DetectConflicts(NozzleRow row)
        {
            foreach (KeyValuePair<string, List<Observation>> kv in row.Observations)
            {
                var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Observation o in kv.Value)
                {
                    string norm = Normalize(o.Value);
                    if (!seen.ContainsKey(norm))
                        seen[norm] = o.Source + "='" + o.Value + "'";
                }
                if (seen.Count > 1)
                    Warn(row, kv.Key, "Source conflict: " + string.Join(" vs ", new List<string>(seen.Values).ToArray()));
            }
        }

        private static void Warn(NozzleRow row, string field, string message)
        {
            row.Diagnostics.Add(new Diagnostic(field, Severity.Warning, message));
            Downgrade(row, IsHardWarning(field) ? Confidence.Low : Confidence.Medium);
        }

        private static void Info(NozzleRow row, string field, string message)
        {
            row.Diagnostics.Add(new Diagnostic(field, Severity.Info, message));
            Downgrade(row, Confidence.Medium);
        }

        // Geometry, size and material problems are treated as low-confidence; softer
        // findings (missing type/pipe, conflicts) keep the row at medium.
        private static bool IsHardWarning(string field)
        {
            return field == "PipeDimension" || field == "Size" || field == "Material";
        }

        private static void Downgrade(NozzleRow row, Confidence level)
        {
            if (level > row.Confidence)
                row.Confidence = level;
        }

        private static bool TryPipe(string pipe, out double od, out double wt)
        {
            od = 0; wt = 0;
            if (TextUtil.IsBlank(pipe)) return false;
            Match m = Regex.Match(pipe, @"D\s*(?<od>[\d.,]+)\s*x\s*(?<wt>[\d.,]+)", RegexOptions.IgnoreCase);
            return m.Success && TryNum(m.Groups["od"].Value, out od) && TryNum(m.Groups["wt"].Value, out wt);
        }

        private static bool TryDn(string size, out int dn)
        {
            dn = 0;
            if (TextUtil.IsBlank(size)) return false;
            Match m = Regex.Match(size, @"DN\s*(\d+)", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out dn);
        }

        private static bool TryNum(string raw, out double value)
        {
            return double.TryParse((raw ?? "").Replace(",", "."), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private static string Normalize(string value)
        {
            return Regex.Replace(value ?? "", @"\s+", "").ToUpperInvariant();
        }
    }
}
