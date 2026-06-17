using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    internal sealed class NozzleRow
    {
        public string Key;
        public string ComponentKind = "";
        public string Description = "";
        public string NozzleType = "";
        public string Size = "";
        public string PressureClass = "";
        public string PipeDimension = "";
        public string Material = "";
        public string Standard = "";
        public readonly Dictionary<string, string> Loads = new Dictionary<string, string>();

        // Validation findings and per-field provenance, populated after parsing.
        public Confidence Confidence = Confidence.High;
        public readonly List<Diagnostic> Diagnostics = new List<Diagnostic>();
        public readonly Dictionary<string, List<Observation>> Observations = new Dictionary<string, List<Observation>>();

        public string DisplayId { get { return Key.Replace(".", "").Replace("*", ""); } }
        public bool IsReinforcement { get { return ComponentKind.ToLowerInvariant().Contains("reinforcement"); } }

        /// <summary>Records that <paramref name="source"/> produced <paramref name="value"/>
        /// for <paramref name="field"/>. Blank values are ignored. Used for conflict detection.</summary>
        public void Observe(string field, string value, string source)
        {
            if (TextUtil.IsBlank(value)) return;
            List<Observation> list;
            if (!Observations.TryGetValue(field, out list))
            {
                list = new List<Observation>();
                Observations[field] = list;
            }
            list.Add(new Observation(source, value.Trim()));
        }

        public void MergeFrom(NozzleRow source)
        {
            if (TextUtil.IsBlank(NozzleType)) NozzleType = source.NozzleType;
            if (TextUtil.IsBlank(Size)) Size = source.Size;
            if (TextUtil.IsBlank(PressureClass)) PressureClass = source.PressureClass;
            if (TextUtil.IsBlank(PipeDimension)) PipeDimension = source.PipeDimension;
            if (TextUtil.IsBlank(Material)) Material = source.Material;
            if (TextUtil.IsBlank(Standard)) Standard = source.Standard;
            foreach (var kv in source.Loads) Loads[kv.Key] = kv.Value;
        }
    }

    internal static class NozzleColumns
    {
        public static readonly string[] Headers =
        {
            "NOZZLE",
            "DESCRIPTION",
            "NOZZLE TYPE",
            "SIZE",
            "PRESSURE CLASS",
            "PIPE DIMENSION, D x t1 (t2)",
            "MATERIAL",
            "STANDARD",
            "Fx (kN)",
            "Fy (kN)",
            "Fz (kN)",
            "Mx (kNm)",
            "My (kNm)",
            "Mz (kNm)"
        };

        // Field keys aligned 1:1 with Headers. Diagnostics and observations use these keys;
        // index in this array is the column index (used for XLSX cell highlighting).
        public static readonly string[] FieldKeys =
        {
            "",
            "Description",
            "NozzleType",
            "Size",
            "PressureClass",
            "PipeDimension",
            "Material",
            "Standard",
            "Fx", "Fy", "Fz", "Mx", "My", "Mz"
        };

        public static int ColumnOf(string fieldKey)
        {
            for (int i = 0; i < FieldKeys.Length; i++)
                if (FieldKeys[i] == fieldKey)
                    return i;
            return -1;
        }
    }
}
