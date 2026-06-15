using HarmonyLib;
using Il2Cpp;
using Il2CppPantheonPersist;
using PantheonAddonFramework.Models;
using PantheonAddonLoader.AddonManagement;
using PantheonAddonLoader.Events;
using PantheonAddonLoader.Models;

namespace PantheonAddonLoader.Hooks;

[HarmonyPatch(typeof(EntityPlayerGameObject), nameof(EntityPlayerGameObject.NetworkStart))]
public class PlayerNetworkStart
{
    private static void Postfix(EntityPlayerGameObject __instance)
    {
        // Fired in character select
        if (__instance.NetworkId.Value == 1)
        {
            return;
        }

        var player = new Player(__instance);
        EntityRegistry.AddOrUpdate(__instance, "Player");
        
        if (player.IsLocalPlayer)
        {
            Globals.LocalPlayer = __instance;
            AddonLoader.LocalPlayerEvents.LocalPlayerEntered.Raise(player);
            __instance.Inventory.add_ItemAddedEvent(new Action<Item, InventoryWithPersyst.AddFlags>((i, _) => AddonLoader.LocalPlayerEvents.ItemAdded.Raise(new InventoryItem(i))));
            __instance.Inventory.add_ItemDeletedEvent(new Action<Item, InventoryWithPersyst.DeleteFlags>((i, _) => AddonLoader.LocalPlayerEvents.ItemRemoved.Raise(new InventoryItem(i))));
        }

        AddonLoader.PlayerEvents.PlayerAdded.Raise(player);
    }
}

[HarmonyPatch(typeof(EntityPlayerGameObject), nameof(EntityPlayerGameObject.NetworkStop))]
public class PlayerNetworkStop
{
    private static void Postfix(EntityPlayerGameObject __instance)
    {
        EntityRegistry.Remove(__instance, "Player");
    }
}

[HarmonyPatch(typeof(Experience.Logic), nameof(Experience.Logic.SetExperience))]
public class ExperienceSetHook
{
    private static void Postfix(Experience.Logic __instance)
    {
        if (Globals.LocalPlayer?.Experience == __instance)
        {
            AddonLoader.LocalPlayerEvents.ExperienceChanged.Raise(new PlayerExperience(__instance.CalculateCurrentExperienceIntoLevel(), __instance.CalculateExperienceRequiredToNextLevel(), __instance.CalculatePercentThroughCurrentLevel()));
        }
    }
}

[HarmonyPatch(typeof(Targets.Logic), nameof(Targets.Logic.SetOffensive))]
public class TargetSetOffensiveHook
{
    private static void Postfix(Targets.Logic __instance)
    {
        if (Globals.LocalPlayer?.Targets == __instance)
        {
            var snapshot = TargetPoolSnapshots.CreateTargetSnapshot("Offensive", __instance.Offensive);
            AddonLoader.LocalPlayerEvents.OffensiveTargetChanged.Raise(snapshot.HealthPercent);
            AddonLoader.LocalPlayerEvents.OffensiveTargetHealthChanged.Raise(snapshot);
        }
    }
}

[HarmonyPatch(typeof(Targets.Logic), nameof(Targets.Logic.SetDefensive))]
public class TargetSetDefensiveHook
{
    private static void Postfix(Targets.Logic __instance)
    {
        if (Globals.LocalPlayer?.Targets == __instance)
        {
            var snapshot = TargetPoolSnapshots.CreateTargetSnapshot("Defensive", __instance.Defensive);
            AddonLoader.LocalPlayerEvents.DefensiveTargetChanged.Raise(snapshot.HealthPercent);
            AddonLoader.LocalPlayerEvents.DefensiveTargetHealthChanged.Raise(snapshot);
        }
    }
}

internal static class TargetPoolSnapshots
{
    public static TargetHealthSnapshot CreateTargetSnapshot(string targetType, IEntity? target)
    {
        var currentHealth = GetPoolValue(target, PoolType.Health, true);
        var maxHealth = GetPoolValue(target, PoolType.Health, false);
        var currentMana = GetPoolValue(target, PoolType.Mana, true);
        var maxMana = GetPoolValue(target, PoolType.Mana, false);
        return new TargetHealthSnapshot(
            TargetType: targetType,
            CurrentHealth: currentHealth,
            MaxHealth: maxHealth,
            HealthPercent: CalculatePercent(currentHealth, maxHealth),
            CurrentMana: currentMana,
            MaxMana: maxMana,
            ManaPercent: CalculatePercent(currentMana, maxMana));
    }

    public static float CalculatePercent(float current, float max)
    {
        return max <= 0 ? 0 : current / max * 100;
    }

    private static float GetPoolValue(IEntity? target, PoolType poolType, bool current)
    {
        try
        {
            return target == null ? 0 : current ? target.Pools.GetCurrent(poolType) : target.Pools.GetMax(poolType);
        }
        catch
        {
            return 0;
        }
    }
}
