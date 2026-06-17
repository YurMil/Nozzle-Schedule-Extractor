namespace NozzleScheduleExtractor
{
    /// <summary>
    /// <see cref="IReportTextExtractor"/> backed by the layout-aware
    /// <see cref="PdfPlumberTextExtractor"/>. Intended to be the primary extractor
    /// with the pypdf-based one as a fallback (see <see cref="FallbackReportTextExtractor"/>).
    /// </summary>
    internal sealed class PdfPlumberReportTextExtractor : IReportTextExtractor
    {
        private readonly string _python;

        public PdfPlumberReportTextExtractor(string python)
        {
            _python = python;
        }

        public string ExtractText(string reportPath)
        {
            return PdfPlumberTextExtractor.Extract(reportPath, _python);
        }
    }
}
