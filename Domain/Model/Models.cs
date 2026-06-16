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

        public string DisplayId { get { return Key.Replace(".", "").Replace("*", ""); } }
        public bool IsReinforcement { get { return ComponentKind.ToLowerInvariant().Contains("reinforcement"); } }

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
    }
}
