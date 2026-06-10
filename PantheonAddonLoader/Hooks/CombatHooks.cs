using Il2Cpp;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Hooks;

public class CombatResultApplyHook
{
    private static void Postfix(CombatResult __instance, double time)
    {
        if (!AddonLoader.HasUpdated)
        {
            return;
        }

        try
        {
            AddonLoader.CombatEvents.CombatResultApplied.Raise(new CombatResultApplied(
                time,
                "",
                "",
                __instance.Damage,
                __instance.BeforeMitigationDamage,
                __instance.MitigatedDamage,
                __instance.ThreatToApply,
                __instance.DamageType.ToString(),
                __instance.DamageStyle.ToString(),
                __instance.WeaponType.ToString(),
                __instance.ImpactType.ToString(),
                __instance.CombatResultType.ToString(),
                "",
                ""));
        }
        catch (Exception ex)
        {
            AddonLoader.LoadedAddons.ForEach(addon => addon.Logger.Error($"Combat result hook failed: {ex}"));
        }
    }
}
