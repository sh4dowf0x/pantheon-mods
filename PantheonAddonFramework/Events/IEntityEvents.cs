using PantheonAddonFramework.Models;

namespace PantheonAddonFramework.Events;

public interface IEntityEvents
{
    AddonEvent<EntitySnapshot> EntitySeen { get; }
    AddonEvent<EntitySnapshot> EntityUpdated { get; }
    AddonEvent<EntitySnapshot> EntityRemoved { get; }
}
