using System.Globalization;
using System.Text;

using Edvaniq.BuildingBlocks.AI;
using Edvaniq.Services.Content.Application;
using Edvaniq.Services.Content.Contracts;
using Edvaniq.Services.Content.Domain;
using Edvaniq.Services.Content.Infrastructure.Ai;
using Edvaniq.Services.Content.UnitTests;

using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Edvaniq.Services.Content.ReferenceRun;

// The reference set (tests/ReferenceSet) through the topics prompt of the worker against the configured provider (#309):
// split as the worker splits, asked as the worker asks, scored by rules 1 to 5 of the README. Prints a table with tokens
// and cost and writes it to tests/ReferenceSet/results. Nothing goes to a database, and the file holds topic names and
// numbers only, never an answer of the AI. Stops before the cost passes the cap.
public static class ReferenceRun
{
    public const decimal DefaultMaxCost = 0.50m;

    // The minimum quality per document, averaged over the runs (README, "Lauf gegen den echten Anbieter").
    public const double MinHitRate = 0.6;

    public const double MaxInvented = 2;

    public const int Passed = 0;

    public const int BelowMinimum = 1;

    public const int Stopped = 2;

    private static readonly TimeSpan AnswerTimeout = TimeSpan.FromMinutes(2);

    public static async Task<int> RunAsync(
        string[] args, Action<IConfigurationBuilder> configure, TextWriter output, CancellationToken cancellationToken)
    {
        if (Options.Parse(args) is not { } options)
        {
            await output.WriteLineAsync("Usage: --runs <n> --max-cost <dollars> --out <file>   (all optional, default 1 run, cap $0.50)");
            return Stopped;
        }

        var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings());
        configure(builder.Configuration);
        builder.AddContentAi();
        using var host = builder.Build();
        var ai = host.Services.GetRequiredService<IChatClient>();
        var provider = ai.GetService<ChatClientMetadata>()?.ProviderName ?? builder.Configuration["Ai:Provider"] ?? "unknown";
        var simulated = provider.Contains("Simulated", StringComparison.OrdinalIgnoreCase);

        // The cap comes first, before anything is paid.
        await output.WriteLineAsync(Invariant($"Reference run: provider {provider}, prompt {TopicAnalysis.PromptVersion}, {options.Runs} run(s), cost cap ${options.MaxCost:0.00}."));

        var documents = ReferenceSet.Load().Documents.Where(document => document.Analyzable).ToList();
        var results = documents.Select(document => new DocumentResult(document)).ToList();
        decimal spent = 0;
        string? model = null;
        for (var run = 1; run <= options.Runs; run++)
        {
            foreach (var result in results)
            {
                var text = result.Document.Text;
                var sections = SectionSplitter.Split(text);
                IReadOnlyList<Guid> ids = [.. sections.Select(_ => Guid.NewGuid())];
                var answer = await ai.AskAsync(
                    [
                        new ChatMessage(ChatRole.System, TopicAnalysis.System(LearnerProfile.Default)),
                        new ChatMessage(ChatRole.User, TopicAnalysis.User(sections.Select(span => text.Substring(span.Start, span.Length)))),
                    ],
                    answerText => TopicAnalysis.Read(answerText, ids),
                    new StructuredRequest(AnswerTimeout, ContentAi.TopicsFormat),
                    TimeProvider.System,
                    cancellationToken);
                model = answer.ModelId ?? model;

                foreach (var attempt in answer.Tries)
                {
                    if (attempt.Cost is not { } cost && !simulated)
                    {
                        await output.WriteLineAsync($"Stopped: the provider gave no cost for {result.Document.Id}, so the cap can't hold. Set Ai__Prices for the model.");
                        return Stopped;
                    }

                    spent += attempt.Cost ?? 0;
                    result.Add(attempt);
                }

                if (answer.Failure is { } failure)
                {
                    await output.WriteLineAsync($"Stopped: the provider failed on {result.Document.Id} ({failure.GetType().Name}). Spent so far ${Invariant($"{spent:0.0000}")}.");
                    return Stopped;
                }

                if (spent > options.MaxCost)
                {
                    await output.WriteLineAsync(Invariant($"Stopped: ${spent:0.0000} spent, more than the cap of ${options.MaxCost:0.00}. No result file."));
                    return Stopped;
                }

                result.Score(answer.Value, text, sections, ids);
            }
        }

        var report = Report(results, provider, model ?? ai.GetService<ChatClientMetadata>()?.DefaultModelId ?? "unknown", options.Runs, spent);
        await output.WriteLineAsync(report);
        var path = options.Out ?? Path.Combine(ReferenceSet.Folder, "results", FileName(model));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        await File.WriteAllTextAsync(path, report, cancellationToken);
        await output.WriteLineAsync($"Written to {path}");
        return results.All(result => result.MeetsMinimum) ? Passed : BelowMinimum;
    }

    private static string Report(IReadOnlyList<DocumentResult> results, string provider, string model, int runs, decimal spent)
    {
        var report = new StringBuilder();
        report.AppendLine(Invariant($"# Referenzset-Lauf {TimeProvider.System.GetUtcNow():yyyy-MM-dd}"));
        report.AppendLine();
        report.AppendLine(Invariant($"Anbieter {provider}, Modell `{model}`, Prompt `{TopicAnalysis.PromptVersion}`, {runs} Lauf/Läufe je Dokument. Mindestqualität je Dokument im Mittel: Trefferquote ab {MinHitRate:0.0}, höchstens {MaxInvented:0} erfundene Themen, jede Antwort verwertbar, keine Injection im Themennamen."));
        report.AppendLine();
        report.AppendLine("| Dokument | Treffer | Trefferquote | Erfunden | Ebenenfehler | Anker | Tokens ein/aus | Kosten ($) | Ergebnis |");
        report.AppendLine("|---|---|---|---|---|---|---|---|---|");
        foreach (var result in results)
        {
            report.AppendLine(Invariant(
                $"| {result.Document.Id} | {result.Hits}/{result.Expected} | {result.HitRate:0.00} | {result.InventedPerRun:0.0} | {result.LevelErrors} | {result.AnchorsMet}/{result.Anchors} | {result.InputTokens}/{result.OutputTokens} | {result.Cost:0.0000} | {(result.MeetsMinimum ? "erreicht" : "verfehlt: " + result.Why)} |"));
        }

        report.AppendLine(Invariant($"| Summe | | | | | | {results.Sum(r => r.InputTokens)}/{results.Sum(r => r.OutputTokens)} | {spent:0.0000} | {(results.All(r => r.MeetsMinimum) ? "erreicht" : "verfehlt")} |"));
        report.AppendLine();
        report.AppendLine("Themen des letzten Laufs je Dokument, ein Unterthema als „Thema › Unterthema“:");
        foreach (var result in results)
        {
            report.AppendLine();
            report.AppendLine($"- {result.Document.Id}: " + string.Join(", ", result.LastTopics));
        }

        return report.ToString();
    }

    private static string FileName(string? model)
    {
        var safe = new string([.. (model ?? "unknown").Select(c => char.IsLetterOrDigit(c) || c is '-' or '.' ? c : '-')]);
        return Invariant($"{TimeProvider.System.GetUtcNow():yyyy-MM-dd}-{safe}-{TopicAnalysis.PromptVersion}.md");
    }

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private sealed record Options(int Runs, decimal MaxCost, string? Out)
    {
        public static Options? Parse(string[] args)
        {
            var runs = 1;
            var maxCost = DefaultMaxCost;
            string? output = null;
            for (var index = 0; index < args.Length; index += 2)
            {
                var value = index + 1 < args.Length ? args[index + 1] : null;
                switch (args[index])
                {
                    case "--runs" when int.TryParse(value, CultureInfo.InvariantCulture, out var parsed) && parsed is >= 1 and <= 10:
                        runs = parsed;
                        break;
                    case "--max-cost" when decimal.TryParse(value, NumberStyles.Number, CultureInfo.InvariantCulture, out var cap) && cap > 0:
                        maxCost = cap;
                        break;
                    case "--out" when !string.IsNullOrWhiteSpace(value):
                        output = value;
                        break;
                    default:
                        return null;
                }
            }

            return new Options(runs, maxCost, output);
        }
    }

    // One document over all runs.
    private sealed class DocumentResult(ReferenceDocument document)
    {
        private int runs;

        public ReferenceDocument Document { get; } = document;

        public int Hits { get; private set; }

        public int Expected { get; private set; }

        public int Invented { get; private set; }

        public int LevelErrors { get; private set; }

        public int AnchorsMet { get; private set; }

        public int Anchors { get; private set; }

        public int Unusable { get; private set; }

        public int InjectionViolations { get; private set; }

        public long InputTokens { get; private set; }

        public long OutputTokens { get; private set; }

        public decimal Cost { get; private set; }

        public IReadOnlyList<string> LastTopics { get; private set; } = [];

        public double HitRate => Expected == 0 ? 1 : (double)Hits / Expected;

        public double InventedPerRun => runs == 0 ? 0 : (double)Invented / runs;

        public bool MeetsMinimum => Why.Length == 0;

        public string Why =>
            string.Join(", ", new[]
            {
                HitRate < MinHitRate ? "Trefferquote" : null,
                InventedPerRun > MaxInvented ? "erfundene Themen" : null,
                Unusable > 0 ? "unverwertbare Antwort" : null,
                InjectionViolations > 0 ? "Injection" : null,
            }.OfType<string>());

        public void Add(AiTry attempt)
        {
            InputTokens += attempt.InputTokens;
            OutputTokens += attempt.OutputTokens;
            Cost += attempt.Cost ?? 0;
        }

        public void Score(IReadOnlyList<TopicSuggestion>? suggestions, string text, IReadOnlyList<TextSpan> sections, IReadOnlyList<Guid> ids)
        {
            runs++;
            if (suggestions is null)
            {
                Unusable++;
                Expected += Document.AllTopics().Count();
                LastTopics = [];
                return;
            }

            var score = ReferenceScore.Of(Document, text, sections, ids, suggestions);
            Hits += score.Hits;
            Expected += score.Expected;
            Invented += score.Invented;
            LevelErrors += score.LevelErrors;
            AnchorsMet += score.AnchorsMet;
            Anchors += score.Anchors;
            InjectionViolations += suggestions.Count(suggestion =>
                (Document.Forbidden ?? []).Any(word => suggestion.Name.Contains(word, StringComparison.OrdinalIgnoreCase)));
            LastTopics = [.. suggestions.Select(suggestion => suggestion.Parent is { } parent ? suggestions[parent].Name + " › " + suggestion.Name : suggestion.Name)];
        }
    }
}