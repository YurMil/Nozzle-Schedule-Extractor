using System;
using System.IO;

namespace NozzleScheduleExtractor
{
    internal static class Program
    {
        private const string DefaultPython = @"C:\Users\Yurii.Milienin\.cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe";

        public static int Main(string[] args)
        {
            try
            {
                if (args.Length == 0)
                {
                    PrintUsage();
                    ShowUsageDialog();
                    return 2;
                }

                string input = args[0];
                string prefix = args.Length >= 2 ? args[1] : "";
                string python = args.Length >= 3 ? args[2] : DefaultPython;

                ExtractionResult result = IsPdf(input)
                    ? ExtractionService.RunPdf(input, python, Console.WriteLine)
                    : ExtractionService.RunFolder(input, prefix, python, Console.WriteLine);
                Console.WriteLine("Rows: " + result.Rows.Count);
                Console.WriteLine("XLSX: " + result.XlsxPath);
                Console.WriteLine("TSV: " + result.TsvPath);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("ERROR: " + ex.Message);
                return 1;
            }
        }

        private static bool IsPdf(string path)
        {
            return File.Exists(path) && String.Equals(Path.GetExtension(path), ".pdf", StringComparison.OrdinalIgnoreCase);
        }

        private static void PrintUsage()
        {
            Console.WriteLine(UsageText());
        }

        private static string UsageText()
        {
            return "Nozzle Schedule Extractor" + Environment.NewLine +
                   Environment.NewLine +
                   "Usage:" + Environment.NewLine +
                   "  NozzleScheduleExtractor.exe <folder> [prefix] [python]" + Environment.NewLine +
                   "  NozzleScheduleExtractor.exe <report.pdf> [prefix] [python]" + Environment.NewLine +
                   Environment.NewLine +
                   "Examples:" + Environment.NewLine +
                   @"  NozzleScheduleExtractor.exe ""C:\Reports"" W2402601" + Environment.NewLine +
                   @"  NozzleScheduleExtractor.exe ""C:\Reports\W2402601.pdf""" + Environment.NewLine +
                   Environment.NewLine +
                   "Python must have packages from requirements.txt installed: pdfplumber, pypdf.";
        }

        private static void ShowUsageDialog()
        {
            // Never pop a modal in a non-interactive context (CI, service, headless): it would
            // block indefinitely with no one to dismiss it.
            if (!Environment.UserInteractive) return;
            try
            {
                System.Windows.Forms.MessageBox.Show(
                    UsageText(),
                    "Nozzle Schedule Extractor",
                    System.Windows.Forms.MessageBoxButtons.OK,
                    System.Windows.Forms.MessageBoxIcon.Information);
            }
            catch
            {
            }
        }
    }
}
