using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    internal static class ReportFinder
    {
        public static FileInfo FindLatest(string folder, string prefix)
        {
            if (!Directory.Exists(folder))
                throw new DirectoryNotFoundException(folder);

            string pattern = String.IsNullOrWhiteSpace(prefix) ? "W*.pdf" : prefix + "*.pdf";
            var rx = new Regex(@"^W\d{7}.*\.pdf$", RegexOptions.IgnoreCase);
            var matches = new DirectoryInfo(folder)
                .GetFiles(pattern, SearchOption.TopDirectoryOnly)
                .Where(f => rx.IsMatch(f.Name))
                .OrderByDescending(f => f.LastWriteTime)
                .ThenByDescending(f => f.Length)
                .ToList();

            if (matches.Count == 0)
                throw new FileNotFoundException("No PDF matching W#######*.pdf found in " + folder);

            return matches[0];
        }
    }
}
