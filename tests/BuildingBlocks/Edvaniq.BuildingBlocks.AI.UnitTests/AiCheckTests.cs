using System.Net;

namespace Edvaniq.BuildingBlocks.AI.UnitTests;

// The check after a deploy says whether the provider answers, with model, time and tokens, but never the answer or the key.
public class AiCheckTests
{
    [Fact]
    public async Task Answer_PrintsModelTimeAndTokens()
    {
        var stub = new AiStub().Answer(HttpStatusCode.OK, AiStub.Completion("Photosynthese", "vendor/model-1-0101"));
        using var host = stub.Host();
        var output = new StringWriter();

        var exitCode = await AiCheck.RunAsync(host.Services, output);

        Assert.Equal(0, exitCode);
        Assert.Matches(@"^AI answered: provider OpenAiCompatible, model vendor/model-1-0101, \d+ ms, 15 tokens\.\r?\n$", output.ToString());
        Assert.DoesNotContain("Photosynthese", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Rejection_PrintsTheStatusWithoutTheKey()
    {
        var stub = new AiStub().Answer(HttpStatusCode.Unauthorized, $$$"""{"error":{"message":"Invalid key {{{AiStub.Key}}}"}}""");
        using var host = stub.Host();
        var output = new StringWriter();

        var exitCode = await AiCheck.RunAsync(host.Services, output);

        Assert.Equal(1, exitCode);
        Assert.Matches(@"^AI check failed: provider OpenAiCompatible, after \d+ ms: AiProviderException: The AI provider answered 401\.\r?\n$", output.ToString());
        Assert.DoesNotContain(AiStub.Key, output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoAnswerInTime_Fails()
    {
        var stub = new AiStub().Hang();
        using var host = stub.Host();
        var output = new StringWriter();

        var exitCode = await AiCheck.RunAsync(host.Services, output, TimeSpan.FromMilliseconds(200));

        Assert.Equal(1, exitCode);
        Assert.Contains("no answer in time", output.ToString(), StringComparison.Ordinal);
    }
}