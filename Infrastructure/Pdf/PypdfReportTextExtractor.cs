namespace NozzleScheduleExtractor
{
    internal sealed class PypdfReportTextExtractor : IReportTextExtractor
    {
        private readonly string _python;

        public PypdfReportTextExtractor(string python)
        {
            _python = python;
        }

        public string ExtractText(string reportPath)
        {
            return PdfTextExtractor.Extract(reportPath, _python);
        }
    }
}
