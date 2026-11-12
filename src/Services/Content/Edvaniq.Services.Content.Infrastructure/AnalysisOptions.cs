using Edvaniq.Services.Content.Application;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Edvaniq.Services.Content.Infrastructure;

// The time limits of the worker, section Content:Analysis (docs/service-internals.md).
public sealed class AnalysisOptions
{
    public const string Section = "Content:Analysis";

    // How long the AI may take for one call. A provider that hangs fails the call instead of the worker.
    public TimeSpan AiTimeout { get; set; } = TimeSpan.FromMinutes(2);

    // How much material goes into one call for the topics. A longer document is asked in parts (TopicBatches).
    public int TopicBatchCharacters { get; set; } = TopicBatches.DefaultBatchCharacters;

    // How long a document waits for Knowledge's topics before it fails.
    public TimeSpan TopicsTimeout { get; set; } = TimeSpan.FromMinutes(10);

    // How long a whole run may take, from the paste, upload or retry on. Longer and StuckAnalysisSweep fails it.
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(60);

    // How often the sweep looks.
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    public static void AddTo(IServiceCollection services) =>
        services.AddOptions<AnalysisOptions>()
            .BindConfiguration(Section)
            .Validate(
                options => options.AiTimeout > TimeSpan.Zero && options.TopicsTimeout > TimeSpan.Zero && options.TopicBatchCharacters > 0
                    && options.SweepInterval > TimeSpan.Zero && options.Timeout > options.TopicsTimeout,
                $"{Section}: every limit must be above zero, and Timeout above TopicsTimeout.")
            .ValidateOnStart();
}