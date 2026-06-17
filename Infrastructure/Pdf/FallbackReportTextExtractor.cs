using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Tries an ordered list of extractors and returns the first that yields
    /// non-blank text. If an extractor throws (e.g. the required Python package is
    /// missing) the next one is tried. This lets the layout-aware pdfplumber
    /// extractor run as primary while keeping pypdf as a safety net.
    /// </summary>
    internal sealed class FallbackReportTextExtractor : IReportTextExtractor
    {
        private readonly List<IReportTextExtractor> _extractors;
        private readonly Action<string> _log;

        public FallbackReportTextExtractor(params IReportTextExtractor[] extractors)
            : this(null, extractors)
        {
        }

        public FallbackReportTextExtractor(Action<string> log, params IReportTextExtractor[] extractors)
        {
            if (extractors == null || extractors.Length == 0)
                throw new ArgumentException("At least one extractor is required.", "extractors");
            _log = log;
            _extractors = extractors.ToList();
        }

        public string ExtractText(string reportPath)
        {
            string lastResult = "";
            var errors = new List<Exception>();

            foreach (IReportTextExtractor extractor in _extractors)
            {
                string name = extractor.GetType().Name;
                try
                {
                    string text = extractor.ExtractText(reportPath);
                    if (HasContent(text))
                        return text;

                    lastResult = text;
                    Log(name + " produced no usable text; trying next extractor.");
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                    Log(name + " failed (" + ex.Message + "); trying next extractor.");
                }
            }

            if (HasContent(lastResult))
                return lastResult;
            if (errors.Count == 1)
                throw errors[0];
            if (errors.Count > 1)
                throw new AggregateException("All text extractors failed.", errors);
            return lastResult;
        }

        // Page/table markers are emitted even when an extractor reads a valid PDF but finds
        // no words. Strip them before judging whether there is real content, otherwise a
        // marker-only result would suppress the fallback extractor.
        private static bool HasContent(string text)
        {
            string stripped = Regex.Replace(text ?? "", @"<<<PAGE\s+\d+>>>|<<<TABLE>>>|<<<TABLE END>>>", " ");
            return !TextUtil.IsBlank(stripped);
        }

        private void Log(string message)
        {
            if (_log != null)
                _log(message);
        }
    }
}
