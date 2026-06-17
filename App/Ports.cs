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

    /// <summary>
    /// Optional resolver that suggests values for fields the deterministic parser could
    /// not fill (or filled with low confidence). Implementations are advisory: the parser
    /// remains ground truth. NOT wired into the default pipeline yet — see the Stage 3
    /// hybrid-extraction issue.
    /// </summary>
    internal interface INozzleFieldResolver
    {
        List<FieldSuggestion> Resolve(string sectionText, IEnumerable<string> fields);
    }

    internal interface INozzleScheduleWriter
    {
        string DisplayName { get; }
        string FileSuffix { get; }
        void Write(List<NozzleRow> rows, string path);
    }
}
