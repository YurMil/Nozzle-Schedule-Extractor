using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NozzleScheduleExtractor
{
    internal static class TsvWriter
    {
        public static void Write(List<NozzleRow> rows, string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join("\t", NozzleColumns.Headers));
                foreach (NozzleRow row in rows)
                    writer.WriteLine(string.Join("\t", ToCells(row).Select(v => v ?? "")));
            }
        }

        public static string[] ToCells(NozzleRow row)
        {
            string[] loads = HasAllLoads(row)
                ? new[] { row.Loads["Fx"], row.Loads["Fy"], row.Loads["Fz"], row.Loads["Mx"], row.Loads["My"], row.Loads["Mz"] }
                : new[] { "-", "-", "-", "-", "-", "-" };

            return new[]
            {
                row.DisplayId,
                TextUtil.CleanDescription(row.Description),
                TextUtil.OrDash(row.NozzleType),
                TextUtil.OrDash(row.Size),
                TextUtil.OrDash(row.PressureClass),
                TextUtil.OrDash(row.PipeDimension),
                TextUtil.OrDash(row.Material),
                TextUtil.OrDash(row.Standard),
                loads[0],
                loads[1],
                loads[2],
                loads[3],
                loads[4],
                loads[5]
            };
        }

        private static bool HasAllLoads(NozzleRow row)
        {
            return new[] { "Fx", "Fy", "Fz", "Mx", "My", "Mz" }.All(k => row.Loads.ContainsKey(k));
        }
    }
}
