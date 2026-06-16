using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace NozzleScheduleExtractor
{
    internal static class PdfTextExtractor
    {
        public static string Extract(string pdfPath, string python)
        {
            if (!File.Exists(python))
                throw new FileNotFoundException("Python not found: " + python);

            string script = "import sys\nfrom pypdf import PdfReader\np=sys.argv[1]\nr=PdfReader(p)\nfor i,page in enumerate(r.pages):\n    print('\\n<<<PAGE %d>>>\\n' % (i+1))\n    print(page.extract_text() or '')\n";
            string scriptPath = Path.Combine(Path.GetTempPath(), "vvd_extract_text_" + Guid.NewGuid().ToString("N") + ".py");
            File.WriteAllText(scriptPath, script, new UTF8Encoding(false));
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
                        throw new Exception("PDF text extraction failed: " + error.Trim());
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
