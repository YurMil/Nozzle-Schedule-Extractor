using System.Collections.Generic;

namespace NozzleScheduleExtractor
{
    /// <summary>Confidence assigned to a parsed row after validation.</summary>
    internal enum Confidence
    {
        High = 0,
        Medium = 1,
        Low = 2
    }

    internal enum Severity
    {
        Info = 0,
        Warning = 1
    }

    /// <summary>
    /// A single finding about a parsed row: a sanity-check failure, a missing
    /// critical field, or a conflict between two sources for the same field.
    /// <see cref="Field"/> is the schedule column name (or empty for row-level).
    /// </summary>
    internal sealed class Diagnostic
    {
        public readonly string Field;
        public readonly Severity Severity;
        public readonly string Message;

        public Diagnostic(string field, Severity severity, string message)
        {
            Field = field ?? "";
            Severity = severity;
            Message = message ?? "";
        }
    }

    /// <summary>One value seen for a field, tagged with the source that produced it.</summary>
    internal sealed class Observation
    {
        public readonly string Source;
        public readonly string Value;

        public Observation(string source, string value)
        {
            Source = source ?? "";
            Value = value ?? "";
        }
    }

    /// <summary>Names of the parser sources, used for provenance/conflict reporting.</summary>
    internal static class Source
    {
        public const string NozzleList = "Nozzle List";
        public const string Bom = "Bill of Materials";
        public const string Mawp = "MAWP/Flange";
        public const string Detail = "Detail page";
    }
}
