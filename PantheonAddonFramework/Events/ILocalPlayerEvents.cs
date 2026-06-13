using PantheonAddonFramework.Models;

namespace PantheonAddonFramework.Events;

public interface ILocalPlayerEvents
{
    AddonEvent<IPlayer> LocalPlayerEntered { get; }
    AddonEvent<IPlayer> LocalPlayerLeft { get; }
    AddonEvent<PlayerExperience> ExperienceChanged { get; }
    AddonEvent<float> OffensiveTargetChanged { get; }
    AddonEvent<float> DefensiveTargetChanged { get; }
    AddonEvent<TargetHealthSnapshot> OffensiveTargetHealthChanged { get; }
    AddonEvent<TargetHealthSnapshot> DefensiveTargetHealthChanged { get; }
    AddonEvent<IInventoryItem> ItemAdded { get; }
    AddonEvent<IInventoryItem> ItemRemoved { get; }
}
