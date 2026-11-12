namespace Edvaniq.Services.Content.Application;

// Whether the caller's token marks a team account for tests (claim test_double, docs/tokens.md). Only a new document
// takes it over, the worker then goes by the document.
public interface ITestDoubleCaller
{
    bool UsesTestDouble { get; }
}