using HarmonyLib;
using Il2Cpp;
using Il2CppPantheonPersist;
using MelonLoader;
using PantheonAddonFramework;
using PantheonAddonLoader.AddonManagement;
using PantheonAddonLoader.UI;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Hooks;

[HarmonyPatch(typeof(UIWindowPanel), nameof(UIWindowPanel.Start))]
public class UIPanelHooks
{
    private static void Postfix(UIWindowPanel __instance)
    {
        if (__instance.name == "Panel_XpBar")
        {
            AddonLoader.WindowPanelEvents.ExperienceBarReady.Raise(new XpBarWindow(__instance));
        }
        if (__instance.name == "Panel_OffensiveTarget")
        {
            var healthBarPool = __instance.GetComponentInChildren<UIPoolBar>(true);
            AddonLoader.WindowPanelEvents.OffensiveTargetReady.Raise(new AddonPoolBar(healthBarPool));
        }
        if (__instance.name == "Panel_DefensiveTarget")
        {
            var healthBarPool = __instance.GetComponentInChildren<UIPoolBar>(true);
            AddonLoader.WindowPanelEvents.DefensiveTargetReady.Raise(new AddonPoolBar(healthBarPool));
        }
    }
}

[HarmonyPatch(typeof(UIPoolBar), nameof(UIPoolBar.HandlePoolChanged))]
public class UIPoolBarHandlePoolChangedHook
{
    private static void Postfix(UIPoolBar __instance, float current, float max, float delta, PoolChangeType changeType)
    {
        // The below checks will throw for e.g., the player's Endurance pool bar. For now, lets only get the health pools
        // until we've found a better way to do this
        if (__instance.PoolType != PoolType.Health)
        {
            return;   
        }
        
        var pppName = __instance.transform.parent.parent.parent.name;

        if (pppName == "Panel_OffensiveTarget")
        {
            var snapshot = TargetPoolSnapshots.CreateTargetSnapshot("Offensive", Globals.LocalPlayer?.Targets?.Offensive);
            var percent = snapshot.HasTarget ? snapshot.HealthPercent : CalculatePercent(current, max);
            AddonLoader.WindowPanelEvents.OffTargetPoolbarChange.Raise(percent);
            AddonLoader.WindowPanelEvents.OffTargetHealthChange.Raise(snapshot.HasTarget ? snapshot : new TargetHealthSnapshot("Offensive", current, max, percent, 0, 0, 0));
        }
        if (pppName == "Panel_DefensiveTarget")
        {
            var snapshot = TargetPoolSnapshots.CreateTargetSnapshot("Defensive", Globals.LocalPlayer?.Targets?.Defensive);
            var percent = snapshot.HasTarget ? snapshot.HealthPercent : CalculatePercent(current, max);
            AddonLoader.WindowPanelEvents.DefTargetPoolbarChange.Raise(percent);
            AddonLoader.WindowPanelEvents.DefTargetHealthChange.Raise(snapshot.HasTarget ? snapshot : new TargetHealthSnapshot("Defensive", current, max, percent, 0, 0, 0));
        }
    }

    private static float CalculatePercent(float current, float max)
    {
        return max <= 0 ? 0 : current / max * 100;
    }
}
