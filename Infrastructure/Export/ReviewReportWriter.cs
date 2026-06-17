using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Writes a tab-separated review report listing every validation finding and
    /// source conflict, plus the confidence assigned to each row. Helps a reviewer
    /// jump straight to the cells that need a human check.
    /// </summary>
    internal sealed class ReviewReportWriter : INozzleScheduleWriter
    {
        public string DisplayName { get { return "Review report"; } }
        public string FileSuffix { get { return "_nozzle_review.tsv"; } }

        public void Write(List<NozzleRow> rows, string path)
        {
            using (var writer = new StreamWriter(path, false, new UTF8Encoding(true)))
            {
                writer.WriteLine(string.Join("\t", new[] { "NOZZLE", "CONFIDENCE", "FIELD", "SEVERITY", "MESSAGE" }));

                int findings = 0;
                foreach (NozzleRow row in rows)
                {
                    foreach (Diagnostic d in row.Diagnostics)
                    {
                        findings++;
                        writer.WriteLine(string.Join("\t", new[]
                        {
                            row.DisplayId,
                            row.Confidence.ToString(),
                            Cell(d.Field),
                            d.Severity.ToString(),
                            Cell(d.Message)
                        }));
                    }
                }

                if (findings == 0)
                    writer.WriteLine(string.Join("\t", new[] { "-", Confidence.High.ToString(), "-", "-", "No issues found" }));
            }
        }

        private static string Cell(string value)
        {
            return (value ?? "").Replace("\t", " ").Replace("\r", " ").Replace("\n", " ");
        }
    }
}
