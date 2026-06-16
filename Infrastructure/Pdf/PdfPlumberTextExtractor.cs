using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Layout-aware text extraction via pdfplumber. Unlike pypdf's extract_text(),
    /// this reconstructs each line from word coordinates: words are clustered into
    /// rows by their vertical position and ordered left-to-right by x0. This keeps
    /// table rows (e.g. "Radial Load Fz kN 1 -9 3") on a single line with
    /// deterministic spacing, which is what <see cref="VvdNozzleParser"/> expects.
    ///
    /// Page boundaries are emitted as "&lt;&lt;&lt;PAGE n&gt;&gt;&gt;" markers so the
    /// output is structurally compatible with the pypdf extractor.
    /// </summary>
    internal static class PdfPlumberTextExtractor
    {
        // Vertical tolerance (in PDF points) for grouping words into the same row.
        private const string LineTolerance = "3.0";

        private static readonly string Script = string.Join("\n", new[]
        {
            "import sys",
            "import pdfplumber",
            "path = sys.argv[1]",
            "tol = " + LineTolerance,
            "out = sys.stdout",
            "with pdfplumber.open(path) as pdf:",
            "    for i, page in enumerate(pdf.pages):",
            "        out.write('\\n<<<PAGE %d>>>\\n\\n' % (i + 1))",
            "        words = page.extract_words(use_text_flow=False, keep_blank_chars=False)",
            "        words.sort(key=lambda w: (float(w['top']), float(w['x0'])))",
            "        lines = []",
            "        cur = []",
            "        cur_top = None",
            "        for w in words:",
            "            t = float(w['top'])",
            "            if cur_top is None or abs(t - cur_top) <= tol:",
            "                cur.append(w)",
            "                if cur_top is None:",
            "                    cur_top = t",
            "            else:",
            "                lines.append(cur)",
            "                cur = [w]",
            "                cur_top = t",
            "        if cur:",
            "            lines.append(cur)",
            "        for ln in lines:",
            "            ln.sort(key=lambda w: float(w['x0']))",
            "            out.write(' '.join(w['text'] for w in ln))",
            "            out.write('\\n')",
            ""
        });

        public static string Extract(string pdfPath, string python)
        {
            if (!File.Exists(python))
                throw new FileNotFoundException("Python not found: " + python);

            string scriptPath = Path.Combine(Path.GetTempPath(), "vvd_extract_plumber_" + Guid.NewGuid().ToString("N") + ".py");
            File.WriteAllText(scriptPath, Script, new UTF8Encoding(false));
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = python,
                    Arguments = Quote(scriptPath) + " " + Quote(pdfPath),
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };
                using (var process = Process.Start(psi))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    if (process.ExitCode != 0)
                        throw new Exception("pdfplumber text extraction failed: " + error.Trim());
                    return output;
                }
            }
            finally
            {
                try { File.Delete(scriptPath); } catch { }
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }
    }
}
