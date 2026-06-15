using Il2Cpp;
using Il2CppPantheonPersist;
using PantheonAddonFramework.Models;
using PantheonAddonLoader.Hooks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace PantheonAddonLoader.Models;

internal static class WorldItemRegistry
{
    private const string EntityType = "GroundSpawn";
    private static readonly Dictionary<uint, NetworkWorldItem> Entities = new();
    private static DateTime _nextScanUtc = DateTime.MinValue;
    private static DateTime _nextErrorLogUtc = DateTime.MinValue;

    public static void PublishPeriodicUpdates()
    {
        if (DateTime.UtcNow < _nextScanUtc)
        {
            return;
        }

        _nextScanUtc = DateTime.UtcNow.AddSeconds(1);

        try
        {
            ScanLoadedWorldItems();
        }
        catch (Exception ex)
        {
            LogThrottled($"Ground spawn scan failed: {ex}");
        }
    }

    private static void ScanLoadedWorldItems()
    {
        var seenThisScan = new HashSet<uint>();
        var worldItems = Object.FindObjectsOfType<NetworkWorldItem>();

        foreach (var worldItem in worldItems)
        {
            if (worldItem == null || !TryGetEntityId(worldItem, out var entityId))
            {
                continue;
            }

            seenThisScan.Add(entityId);
            var eventType = Entities.ContainsKey(entityId) ? "updated" : "seen";
            Entities[entityId] = worldItem;
            RaiseSnapshot(worldItem, entityId, eventType, eventType == "seen"
                ? AddonLoader.EntityEvents.EntitySeen.Raise
                : AddonLoader.EntityEvents.EntityUpdated.Raise);
        }

        foreach (var missingEntityId in Entities.Keys.Where(entityId => !seenThisScan.Contains(entityId)).ToArray())
        {
            var worldItem = Entities[missingEntityId];
            Entities.Remove(missingEntityId);
            RaiseSnapshot(worldItem, missingEntityId, "removed", AddonLoader.EntityEvents.EntityRemoved.Raise);
        }
    }

    private static void RaiseSnapshot(NetworkWorldItem worldItem, uint entityId, string eventType, Action<EntitySnapshot> raise)
    {
        try
        {
            raise(CreateSnapshot(worldItem, entityId, eventType));
        }
        catch (Exception ex)
        {
            LogThrottled($"Ground spawn snapshot failed: {ex}");
        }
    }

    private static EntitySnapshot CreateSnapshot(NetworkWorldItem worldItem, uint entityId, string eventType)
    {
        var info = worldItem.Info;
        var position = GetPosition(worldItem);
        var rotation = GetRotation(worldItem);
        var currentHealth = GetPoolValue(worldItem, PoolType.Health, true);
        var maxHealth = GetPoolValue(worldItem, PoolType.Health, false);
        var healthPercent = maxHealth > 0 ? currentHealth / maxHealth * 100f : 0f;

        return new EntitySnapshot(
            TimestampUtc: DateTime.UtcNow,
            EventType: eventType,
            EntityType: EntityType,
            RuntimeType: worldItem.GetType().FullName ?? "",
            NetworkId: entityId,
            CharacterId: Safe(() => info?.CharacterId ?? 0),
            Name: GetName(worldItem, info),
            Title: GetWorldItemDetails(worldItem),
            Kind: Safe(() => worldItem.WorldItemType.ToString() ?? ""),
            Tier: Safe(() => worldItem.Flags.ToString() ?? ""),
            Profession: Safe(() => worldItem.Profession.ToString() ?? ""),
            Race: Safe(() => info?.Race.ToString() ?? ""),
            Class: Safe(() => worldItem.HarvestType.ToString() ?? ""),
            Role: Safe(() => worldItem.ModelName ?? ""),
            Level: Safe(() => worldItem.Experience?.Level ?? 0),
            X: position.x,
            Y: position.y,
            Z: position.z,
            HeadingY: rotation.eulerAngles.y,
            HealthCurrent: currentHealth,
            HealthMax: maxHealth,
            HealthPercent: healthPercent,
            DistanceFromLocal: GetDistanceFromLocal(position),
            IsLocalPlayer: false);
    }

    private static bool TryGetEntityId(NetworkWorldItem worldItem, out uint entityId)
    {
        entityId = Safe(() => worldItem.NetworkId.Value);
        if (entityId != 0)
        {
            return true;
        }

        var instanceId = Safe(() => worldItem.GetInstanceID());
        if (instanceId == 0)
        {
            return false;
        }

        entityId = 0x80000000u | unchecked((uint)instanceId);
        return true;
    }

    private static string GetName(NetworkWorldItem worldItem, EntityInfo? info)
    {
        var displayName = Safe(() => worldItem.displayName);
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        displayName = Safe(() => info?.DisplayName ?? "");
        if (!string.IsNullOrWhiteSpace(displayName))
        {
            return displayName;
        }

        return Safe(() => worldItem.gameObject.name) ?? "";
    }

    private static string GetWorldItemDetails(NetworkWorldItem worldItem)
    {
        var details = new List<string>();

        if (Safe(() => worldItem.HasBeenHarvested))
        {
            details.Add("harvested");
        }

        if (Safe(() => worldItem.IsOpen))
        {
            details.Add("open");
        }

        if (Safe(() => worldItem.isRelevantToPlayer))
        {
            details.Add("relevant");
        }

        return string.Join(",", details);
    }

    private static Vector3 GetPosition(NetworkWorldItem worldItem)
    {
        var position = Safe(() => worldItem.transform.position);
        if (position != default)
        {
            return position;
        }

        return Safe(() => worldItem.Position);
    }

    private static Quaternion GetRotation(NetworkWorldItem worldItem)
    {
        var rotation = Safe(() => worldItem.transform.rotation);
        if (rotation != default)
        {
            return rotation;
        }

        return Safe(() => worldItem.Rotation);
    }

    private static float GetPoolValue(NetworkWorldItem worldItem, PoolType poolType, bool current)
    {
        try
        {
            return current ? worldItem.Pools.GetCurrent(poolType) : worldItem.Pools.GetMax(poolType);
        }
        catch
        {
            return 0f;
        }
    }

    private static float GetDistanceFromLocal(Vector3 position)
    {
        try
        {
            if (Globals.LocalPlayer == null)
            {
                return 0f;
            }

            var localPosition = Globals.LocalPlayer.Position;
            var dx = position.x - localPosition.x;
            var dy = position.y - localPosition.y;
            var dz = position.z - localPosition.z;
            return MathF.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
        }
        catch
        {
            return 0f;
        }
    }

    private static T Safe<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch
        {
            return default!;
        }
    }

    private static void LogThrottled(string message)
    {
        if (DateTime.UtcNow < _nextErrorLogUtc)
        {
            return;
        }

        _nextErrorLogUtc = DateTime.UtcNow.AddSeconds(30);
        AddonLoader.LoadedAddons.ForEach(addon => addon.Logger.Error(message));
    }
}
