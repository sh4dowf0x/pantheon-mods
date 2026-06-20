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
        
        LocalPlayerTracker.TrySetLocalPlayer(__instance, player);

        AddonLoader.PlayerEvents.PlayerAdded.Raise(player);
    }
}

[HarmonyPatch(typeof(EntityPlayerGameObject), nameof(EntityPlayerGameObject.NetworkStop))]
public class PlayerNetworkStop
{
    private static void Postfix(EntityPlayerGameObject __instance)
    {
        LocalPlayerTracker.TryClearLocalPlayer(__instance);
        EntityRegistry.Remove(__instance, "Player");
    }
}

internal static class LocalPlayerTracker
{
    public static void SyncFromGameState()
    {
        try
        {
            var localPlayer = EntityPlayerGameObject.LocalPlayer;
            if (localPlayer == null)
            {
                return;
            }

            var localPointer = localPlayer.Pointer;
            if (localPointer == IntPtr.Zero || Globals.LocalPlayer?.Pointer == localPointer)
            {
                return;
            }

            foreach (var player in UnityEngine.Object.FindObjectsOfType<EntityPlayerGameObject>())
            {
                if (player == null || player.Pointer != localPointer)
                {
                    continue;
                }

                TrySetLocalPlayer(player, new Player(player), force: true);
                return;
            }
        }
        catch (Exception ex)
        {
            AddonLoader.LoadedAddons.ForEach(addon => addon.Logger.Error($"Local player sync failed: {ex}"));
        }
    }

    public static void TrySetLocalPlayer(EntityPlayerGameObject entity, Player player, bool force = false)
    {
        if (!force && !IsLocalPlayer(entity, player))
        {
            return;
        }

        if (Globals.LocalPlayer?.NetworkId.Value == entity.NetworkId.Value)
        {
            return;
        }

        Globals.LocalPlayer = entity;
        AddonLoader.LocalPlayerEvents.LocalPlayerEntered.Raise(player);
        entity.Inventory.add_ItemAddedEvent(new Action<Item, InventoryWithPersyst.AddFlags>((i, _) => AddonLoader.LocalPlayerEvents.ItemAdded.Raise(new InventoryItem(i))));
        entity.Inventory.add_ItemDeletedEvent(new Action<Item, InventoryWithPersyst.DeleteFlags, int>((i, _, _) => AddonLoader.LocalPlayerEvents.ItemRemoved.Raise(new InventoryItem(i))));
    }

    public static void TryClearLocalPlayer(EntityPlayerGameObject entity)
    {
        if (Globals.LocalPlayer?.NetworkId.Value != entity.NetworkId.Value)
        {
            return;
        }

        var player = new Player(entity);
        Globals.LocalPlayer = null;
        AddonLoader.LocalPlayerEvents.LocalPlayerLeft.Raise(player);
    }

    private static bool IsLocalPlayer(EntityPlayerGameObject entity, Player player)
    {
        if (player.IsLocalPlayer)
        {
            return true;
        }

        try
        {
            var localPlayer = EntityPlayerGameObject.LocalPlayer;
            return localPlayer != null
                && localPlayer.Pointer != IntPtr.Zero
                && localPlayer.Pointer == entity.Pointer;
        }
        catch
        {
            return false;
        }
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
