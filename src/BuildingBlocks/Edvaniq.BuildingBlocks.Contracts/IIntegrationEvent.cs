namespace Edvaniq.BuildingBlocks.Contracts;

// Marks a message that one service sends to the others. An integration event lives in the .Contracts of the service that
// sends it (Edvaniq.Services.<Sender>.Contracts), and that namespace also tells the broker where it goes.
public interface IIntegrationEvent;