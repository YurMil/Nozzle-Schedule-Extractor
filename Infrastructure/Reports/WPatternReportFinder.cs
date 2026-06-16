using System.IO;

namespace NozzleScheduleExtractor
{
    internal sealed class WPatternReportFinder : IReportFinder
    {
        public FileInfo FindLatest(string folder, string prefix)
        {
            return ReportFinder.FindLatest(folder, prefix);
        }
    }
}
