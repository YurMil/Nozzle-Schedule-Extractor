using System.Collections.Generic;
using System.IO;

namespace NozzleScheduleExtractor
{
    internal interface IReportFinder
    {
        FileInfo FindLatest(string folder, string prefix);
    }

    internal interface IReportTextExtractor
    {
        string ExtractText(string reportPath);
    }

    internal interface INozzleScheduleParser
    {
        List<NozzleRow> Parse(string text);
    }

    internal interface INozzleScheduleWriter
    {
        string DisplayName { get; }
        string FileSuffix { get; }
        void Write(List<NozzleRow> rows, string path);
    }
}
