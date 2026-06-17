namespace NozzleScheduleExtractor
{
    /// <summary>
    /// A value suggested by an external resolver (e.g. an LLM) for a single schedule
    /// field. Suggestions are advisory only: the deterministic parser stays the source
    /// of truth, so a suggestion must never overwrite a non-blank parsed value.
    /// </summary>
    internal sealed class FieldSuggestion
    {
        public readonly string Field;
        public readonly string Value;
        public readonly Confidence Confidence;

        public FieldSuggestion(string field, string value, Confidence confidence)
        {
            Field = field ?? "";
            Value = value ?? "";
            Confidence = confidence;
        }
    }
}
