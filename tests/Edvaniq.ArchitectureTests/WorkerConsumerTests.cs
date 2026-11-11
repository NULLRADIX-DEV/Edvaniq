namespace Edvaniq.ArchitectureTests;

// #235: no worker handles events without a health check, or a failing consumer there would never raise an alarm.
public sealed class WorkerConsumerTests
{
    private const string Disabled = "\n  beta-worker:\n    image: x\n    healthcheck:\n      disable: true\n";

    private const string Checked = "\n  beta-worker:\n    image: x\n    healthcheck:\n      test: [\"CMD\", \"true\"]\n";

    public static TheoryData<string> Workers => new(WorkerDirectories());

    private static string[] WorkerDirectories() =>
        [..
        Directory.EnumerateDirectories(Path.Combine(Repository.Root, "src", "Services"), "*.Worker", SearchOption.AllDirectories)];

    [Theory]
    [MemberData(nameof(Workers))]
    public void Worker_HandlesNoEvents_OrHasAHealthCheck(string workerDirectory)
    {
        var name = Path.GetFileName(workerDirectory);
        var service = name.Split('.')[^2].ToLowerInvariant() + "-worker";
        var compose = File.ReadAllText(Path.Combine(Repository.Root, "deploy", "compose.yml")).ReplaceLineEndings("\n");

        Assert.Null(WorkerConsumerRule.Violation(name, WorkerConsumerRule.SourceOf(workerDirectory), WorkerConsumerRule.ComposeBlockOf(compose, service)));
    }

    [Fact]
    public void Workers_AreFound() =>
        Assert.Contains(WorkerDirectories(), directory => directory.EndsWith("Edvaniq.Services.Content.Worker", StringComparison.Ordinal));

    // The Content worker sends events and takes the AddWorker method of its Infrastructure along, without the Api's.
    [Fact]
    public void ContentWorker_SourceTakesItsInfrastructureMethodOnly()
    {
        var source = WorkerConsumerRule.SourceOf(Path.Combine(Repository.Root, "src", "Services", "Content", "Edvaniq.Services.Content.Worker"));

        Assert.Contains("AddWorkerInfrastructure", source, StringComparison.Ordinal);
        Assert.Contains("AddHostedService<DocumentAnalyzer>", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UserDeletedHandler", source, StringComparison.Ordinal);
    }

    [Fact]
    public void HandlerInAWorkerWithoutHealthCheck_IsAViolation() =>
        Assert.NotNull(WorkerConsumerRule.Violation("Beta.Worker", "services.AddIntegrationEventHandler<A, B>();", Disabled));

    [Fact]
    public void HandlerInAWorkerWithHealthCheck_IsFine() =>
        Assert.Null(WorkerConsumerRule.Violation("Beta.Worker", "services.AddIntegrationEventHandler<A, B>();", Checked));

    [Fact]
    public void WorkerWithoutHandler_IsFine() =>
        Assert.Null(WorkerConsumerRule.Violation("Beta.Worker", "services.AddMessaging<Db>(\"beta\");", Disabled));

    [Fact]
    public void ComposeBlock_EndsAtTheNextService()
    {
        var block = WorkerConsumerRule.ComposeBlockOf(Disabled + "  gamma-api:\n    healthcheck:\n      test: x\n", "beta-worker");

        Assert.Contains("disable: true", block, StringComparison.Ordinal);
        Assert.DoesNotContain("gamma-api", block, StringComparison.Ordinal);
    }
}