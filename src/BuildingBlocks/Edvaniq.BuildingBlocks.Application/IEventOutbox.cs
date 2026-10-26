using Edvaniq.BuildingBlocks.Contracts;

namespace Edvaniq.BuildingBlocks.Application;

// Where a use case puts the integration events of a change. The event joins the unit of work that is open and is stored
// by the same SaveChanges as the change, so both land or neither does. Nothing is sent from here: a dispatcher in the
// process picks the event up from the database afterwards.
public interface IEventOutbox
{
    void Add(IIntegrationEvent integrationEvent);
}