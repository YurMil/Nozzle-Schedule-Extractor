using System;
using System.Collections.Generic;
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
                string folder = args.Length >= 1 ? args[0] : Directory.GetCurrentDirectory();
                string prefix = args.Length >= 2 ? args[1] : "";
                string python = args.Length >= 3 ? args[2] : DefaultPython;

                ExtractionResult result = ExtractionService.RunFolder(folder, prefix, python, Console.WriteLine);
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
    }
}
