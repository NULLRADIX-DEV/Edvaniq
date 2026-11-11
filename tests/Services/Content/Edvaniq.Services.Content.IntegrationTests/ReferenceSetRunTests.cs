using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Content.UnitTests;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.IntegrationTests;

// The reference set (tests/ReferenceSet) through the topic analysis of the worker with the simulated AI: split, prompt,
// answer, repaired by TopicAnalysis.Read, scored by rules 1 to 4 of the README. No database, the steps around it have
// their own tests. The scan is turned away before any of this (ReferenceSetTests), the questions of the injection have
// their test in AnalysisTests.
public sealed class ReferenceSetRunTests
{
    // What the simulated AI scores per document. It is the same on every run, so any change is a change of the analysis
    // or of the double and fails the test, a better score too: whoever changes it writes the new value here on purpose.
    // The double reads nothing but headings, so a text without any (Geschichte) hits nothing, by design. How good the real
    // model has to be is up to the run against it (#309).
    private static readonly Dictionary<string, ReferenceScore> Expected = new()
    {
        ["biologie-fotosynthese"] = new(7, 7, 0, 0, 5, 5),
        ["mathematik-lineare-funktionen"] = new(5, 5, 0, 2, 5, 5),
        ["geschichte-industrialisierung"] = new(0, 7, 5, 0, 0, 6),
        ["informatik-sortierverfahren"] = new(9, 9, 1, 0, 12, 12),
        ["chemie-saeuren-basen-injection"] = new(4, 4, 1, 3, 5, 5),
    };

    [Fact]
    public async Task EveryAnalyzableDocument_ScoresAsWrittenDown()
    {
        using var host = SimulatedAi();
        var analyzable = ReferenceSet.Load().Documents.Where(document => document.Analyzable).ToList();

        var scores = new Dictionary<string, ReferenceScore>();
        foreach (var document in analyzable)
        {
            scores[document.Id] = (await AnalyzeAsync(host, document, document.Text)).Score;
        }

        var rows = Expected.Keys.Union(scores.Keys)
            .Select(id => (Id: id, Expected: Expected.GetValueOrDefault(id), Actual: scores.GetValueOrDefault(id)))
            .Where(row => row.Expected != row.Actual)
            .Select(row => $"{row.Id}: written down {row.Expected?.ToString() ?? "nothing"}, now {row.Actual?.ToString() ?? "not run"}")
            .ToList();
        Assert.True(
            rows.Count == 0,
            $"The reference set scores differently than written down in {nameof(ReferenceSetRunTests)}.{nameof(Expected)}. If the "
            + $"change is meant, write the new values there:{Environment.NewLine}{string.Join(Environment.NewLine, rows)}");
    }

    // The injection changes nothing: the same topics on the same levels and the same score as without its paragraphs,
    // and no forbidden text in a topic name (rule 5).
    [Fact]
    public async Task Injection_ChangesNoTopic()
    {
        using var host = SimulatedAi();
        var document = Assert.Single(ReferenceSet.Load().Documents, document => document.Kind == "injection");
        var forbidden = document.Forbidden!;
        var text = document.Text.ReplaceLineEndings("\n");
        var withoutInjection = string.Join("\n\n", text.Split("\n\n")
            .Where(paragraph => !forbidden.Any(word => paragraph.Contains(word, StringComparison.OrdinalIgnoreCase))));
        Assert.NotEqual(text, withoutInjection);

        var injected = await AnalyzeAsync(host, document, text);
        var clean = await AnalyzeAsync(host, document, withoutInjection);

        Assert.Equal(clean.Score, injected.Score);
        Assert.Equal(Shape(clean.Suggestions), Shape(injected.Suggestions));
        Assert.All(injected.Suggestions, suggestion =>
            Assert.DoesNotContain(forbidden, word => suggestion.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<(ReferenceScore Score, IReadOnlyList<TopicSuggestion> Suggestions)> AnalyzeAsync(
        IHost host, ReferenceDocument document, string text)
    {
        var sections = SectionSplitter.Split(text);
        Guid[] ids = [.. sections.Select(_ => Guid.NewGuid())];
        var answer = await host.Services.GetRequiredService<IChatClient>().GetResponseAsync(
            [
                new ChatMessage(ChatRole.System, TopicAnalysis.System(LearnerProfile.Default)),
                new ChatMessage(ChatRole.User, TopicAnalysis.User(sections.Select(span => text.Substring(span.Start, span.Length)))),
            ],
            cancellationToken: TestContext.Current.CancellationToken);
        var suggestions = TopicAnalysis.Read(answer.Text, ids);
        Assert.NotNull(suggestions);
        return (ReferenceScore.Of(document, text, sections, ids, suggestions), suggestions);
    }

    private static List<string> Shape(IReadOnlyList<TopicSuggestion> suggestions) =>
        [.. suggestions.Select(suggestion => $"{suggestion.Name} {suggestion.Parent?.ToString() ?? "-"}")];

    private static IHost SimulatedAi()
    {
        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        builder.Configuration.AddInMemoryCollection([KeyValuePair.Create("Ai:Provider", (string?)"Simulated")]);
        builder.AddContentAi();
        return builder.Build();
    }
}