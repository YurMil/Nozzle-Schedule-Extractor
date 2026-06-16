using System.Collections.Generic;

namespace NozzleScheduleExtractor
{
    internal sealed class XlsxScheduleWriter : INozzleScheduleWriter
    {
        public string DisplayName { get { return "Excel"; } }
        public string FileSuffix { get { return "_nozzle_schedule.xlsx"; } }

        public void Write(List<NozzleRow> rows, string path)
        {
            XlsxWriter.Write(rows, path);
        }
    }
}
