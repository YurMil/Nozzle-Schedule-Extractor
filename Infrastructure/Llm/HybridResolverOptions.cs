using System;

namespace NozzleScheduleExtractor
{
    /// <summary>
    /// Configuration for the optional hybrid LLM resolver, read from environment
    /// variables so no secret is ever committed:
    ///   NOZZLE_EXTRACTOR_LLM        "1"/"true" to enable the hybrid fallback
    ///   ANTHROPIC_API_KEY           API key (required when enabled)
    ///   NOZZLE_EXTRACTOR_LLM_MODEL  optional model id override
    ///
    /// This type is wiring-ready but intentionally NOT consumed by the default
    /// pipeline yet. See the Stage 3 hybrid-extraction issue.
    /// </summary>
    internal sealed class HybridResolverOptions
    {
        // A sensible cost/quality default for fallback extraction; override via env.
        public const string DefaultModel = "claude-sonnet-4-6";

        public bool Enabled;
        public string ApiKey = "";
        public string Model = DefaultModel;
        public string Endpoint = "https://api.anthropic.com/v1/messages";
        public string AnthropicVersion = "2023-06-01";
        public int MaxTokens = 1024;
        public int TimeoutMs = 30000;

        public static HybridResolverOptions FromEnvironment()
        {
            var options = new HybridResolverOptions();
            options.ApiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

            string model = Environment.GetEnvironmentVariable("NOZZLE_EXTRACTOR_LLM_MODEL");
            if (!string.IsNullOrEmpty(model))
                options.Model = model;

            string flag = (Environment.GetEnvironmentVariable("NOZZLE_EXTRACTOR_LLM") ?? "").Trim();
            bool requested = flag == "1" || flag.Equals("true", StringComparison.OrdinalIgnoreCase);
            options.Enabled = requested && !string.IsNullOrEmpty(options.ApiKey);
            return options;
        }
    }
}
