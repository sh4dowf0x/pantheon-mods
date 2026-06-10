using PantheonAddonFramework.Events;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Events;

public class CombatEvents : ICombatEvents
{
    public AddonEvent<CombatResultApplied> CombatResultApplied { get; } = new();
}
