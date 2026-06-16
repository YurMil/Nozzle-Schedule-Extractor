using System.Collections.Generic;

namespace NozzleScheduleExtractor
{
    internal sealed class TsvScheduleWriter : INozzleScheduleWriter
    {
        public string DisplayName { get { return "TSV"; } }
        public string FileSuffix { get { return "_nozzle_schedule.tsv"; } }

        public void Write(List<NozzleRow> rows, string path)
        {
            TsvWriter.Write(rows, path);
        }
    }
}
