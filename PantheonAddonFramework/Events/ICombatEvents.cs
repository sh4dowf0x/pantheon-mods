using PantheonAddonFramework.Models;

namespace PantheonAddonFramework.Events;

public interface ICombatEvents
{
    AddonEvent<CombatResultApplied> CombatResultApplied { get; }
}
