using PantheonAddonFramework.Events;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Events;

public class EntityEvents : IEntityEvents
{
    public AddonEvent<EntitySnapshot> EntitySeen { get; } = new();
    public AddonEvent<EntitySnapshot> EntityUpdated { get; } = new();
    public AddonEvent<EntitySnapshot> EntityRemoved { get; } = new();
}
