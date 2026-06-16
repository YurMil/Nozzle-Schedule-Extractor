using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NozzleScheduleExtractor
{
    internal sealed class ExtractionResult
    {
        public FileInfo Pdf;
        public string XlsxPath;
        public string TsvPath;
        public List<NozzleRow> Rows;
    }

    internal sealed class ExtractionService
    {
        private readonly IReportFinder _reportFinder;
        private readonly IReportTextExtractor _textExtractor;
        private readonly INozzleScheduleParser _parser;
        private readonly List<INozzleScheduleWriter> _writers;

        public ExtractionService(
            IReportFinder reportFinder,
            IReportTextExtractor textExtractor,
            INozzleScheduleParser parser,
            IEnumerable<INozzleScheduleWriter> writers)
        {
            if (reportFinder == null) throw new ArgumentNullException("reportFinder");
            if (textExtractor == null) throw new ArgumentNullException("textExtractor");
            if (parser == null) throw new ArgumentNullException("parser");
            if (writers == null) throw new ArgumentNullException("writers");

            _reportFinder = reportFinder;
            _textExtractor = textExtractor;
            _parser = parser;
            _writers = writers.ToList();
            if (_writers.Count == 0)
                throw new ArgumentException("At least one schedule writer is required.", "writers");
        }

        public static ExtractionResult RunFolder(string folder, string prefix, string python, Action<string> log)
        {
            return CreateDefault(python).ExecuteFolder(folder, prefix, log);
        }

        public static ExtractionResult RunPdf(string pdfPath, string python, Action<string> log)
        {
            return CreateDefault(python).ExecutePdf(pdfPath, log);
        }

        public static ExtractionService CreateDefault(string python)
        {
            return new ExtractionService(
                new WPatternReportFinder(),
                new FallbackReportTextExtractor(
                    new PdfPlumberReportTextExtractor(python),
                    new PypdfReportTextExtractor(python)),
                new VvdNozzleParser(),
                new INozzleScheduleWriter[]
                {
                    new TsvScheduleWriter(),
                    new XlsxScheduleWriter()
                });
        }

        public ExtractionResult ExecuteFolder(string folder, string prefix, Action<string> log)
        {
            Log(log, "Searching report in folder: " + folder);
            FileInfo pdf = _reportFinder.FindLatest(folder, prefix);
            return ExecutePdf(pdf.FullName, log);
        }

        public ExtractionResult ExecutePdf(string pdfPath, Action<string> log)
        {
            if (!File.Exists(pdfPath))
                throw new FileNotFoundException(pdfPath);

            FileInfo pdf = new FileInfo(pdfPath);
            Log(log, "PDF: " + pdf.FullName);
            Log(log, "PDF date: " + pdf.LastWriteTime);

            Log(log, "Extracting PDF text...");
            string text = _textExtractor.ExtractText(pdf.FullName);

            Log(log, "Parsing VVD/PVElite-style tables...");
            List<NozzleRow> rows = _parser.Parse(text);
            int loadedRows = rows.Count(r => new[] { "Fx", "Fy", "Fz", "Mx", "My", "Mz" }.All(k => r.Loads.ContainsKey(k)));
            if (loadedRows == 0)
                Log(log, "No nozzle load table found. Load columns will stay '-'.");
            else
                Log(log, "Nozzle load rows found: " + loadedRows + " of " + rows.Count);

            string baseName = Path.GetFileNameWithoutExtension(pdf.Name);
            string xlsxPath = "";
            string tsvPath = "";

            foreach (INozzleScheduleWriter writer in _writers)
            {
                string outputPath = Path.Combine(pdf.DirectoryName, baseName + writer.FileSuffix);
                Log(log, "Writing " + writer.DisplayName + ": " + outputPath);
                writer.Write(rows, outputPath);

                if (String.Equals(writer.FileSuffix, "_nozzle_schedule.xlsx", StringComparison.OrdinalIgnoreCase))
                    xlsxPath = outputPath;
                if (String.Equals(writer.FileSuffix, "_nozzle_schedule.tsv", StringComparison.OrdinalIgnoreCase))
                    tsvPath = outputPath;
            }

            Log(log, "Done. Rows: " + rows.Count);
            return new ExtractionResult
            {
                Pdf = pdf,
                XlsxPath = xlsxPath,
                TsvPath = tsvPath,
                Rows = rows
            };
        }

        private static void Log(Action<string> log, string message)
        {
            if (log != null)
                log(message);
        }
    }
}
