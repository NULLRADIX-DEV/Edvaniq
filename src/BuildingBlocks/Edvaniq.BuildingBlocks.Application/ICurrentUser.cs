namespace Edvaniq.BuildingBlocks.Application;

// The user of the current request. The id comes only from the validated token, never from query, body or header, so
// a client cannot act as someone else by sending another id.
public interface ICurrentUser
{
    string Id { get; }
}
