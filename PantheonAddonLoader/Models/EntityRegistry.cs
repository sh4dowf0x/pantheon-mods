using Il2Cpp;
using Il2CppPantheonPersist;
using PantheonAddonFramework.Models;
using PantheonAddonLoader.Hooks;

namespace PantheonAddonLoader.Models;

internal static class EntityRegistry
{
    private static readonly Dictionary<uint, TrackedEntity> Entities = new();
    private static DateTime _nextUpdateUtc = DateTime.MinValue;

    public static void AddOrUpdate(BaseEntityGameObject? entity, string entityType)
    {
        if (entity == null)
        {
            return;
        }

        if (!TryGetNetworkId(entity, out var networkId) || networkId == 0)
        {
            return;
        }

        Entities[networkId] = new TrackedEntity(entity, entityType);
        RaiseSnapshot(entity, entityType, "seen", AddonLoader.EntityEvents.EntitySeen.Raise);
    }

    public static void Remove(BaseEntityGameObject? entity, string entityType)
    {
        if (entity == null)
        {
            return;
        }

        if (!TryGetNetworkId(entity, out var networkId) || networkId == 0)
        {
            return;
        }

        Entities.Remove(networkId);
        RaiseSnapshot(entity, entityType, "removed", AddonLoader.EntityEvents.EntityRemoved.Raise);
    }

    public static void PublishPeriodicUpdates()
    {
        if (DateTime.UtcNow < _nextUpdateUtc)
        {
            return;
        }

        _nextUpdateUtc = DateTime.UtcNow.AddSeconds(1);
        foreach (var tracked in Entities.Values.ToArray())
        {
            RaiseSnapshot(tracked.Entity, tracked.EntityType, "updated", AddonLoader.EntityEvents.EntityUpdated.Raise);
        }
    }

    private static void RaiseSnapshot(BaseEntityGameObject entity, string entityType, string eventType, Action<EntitySnapshot> raise)
    {
        try
        {
            var snapshot = CreateSnapshot(entity, entityType, eventType);
            if (snapshot != null)
            {
                raise(snapshot);
            }
        }
        catch (Exception ex)
        {
            AddonLoader.LoadedAddons.ForEach(addon => addon.Logger.Error($"Entity snapshot failed: {ex}"));
        }
    }

    private static EntitySnapshot? CreateSnapshot(BaseEntityGameObject entity, string entityType, string eventType)
    {
        if (!TryGetNetworkId(entity, out var networkId))
        {
            return null;
        }

        var info = entity.Info;
        var position = entity.Position;
        var rotation = entity.Rotation;
        var currentHealth = GetPoolValue(entity, PoolType.Health, true);
        var maxHealth = GetPoolValue(entity, PoolType.Health, false);
        var healthPercent = maxHealth > 0 ? currentHealth / maxHealth * 100f : 0f;
        var distance = GetDistanceFromLocal(position);
        var isLocal = IsLocalPlayer(entity, networkId);

        return new EntitySnapshot(
            TimestampUtc: DateTime.UtcNow,
            EventType: eventType,
            EntityType: entityType,
            RuntimeType: entity.GetType().FullName ?? "",
            NetworkId: networkId,
            CharacterId: Safe(() => info?.CharacterId ?? 0),
            Name: Safe(() => info?.DisplayName ?? ""),
            Title: Safe(() => info?.Title ?? ""),
            Kind: Safe(() => info?.Kind.ToString() ?? ""),
            Tier: Safe(() => info?.Tier.ToString() ?? ""),
            Profession: Safe(() => entity.Profession.ToString() ?? ""),
            Race: Safe(() => info?.Race.ToString() ?? ""),
            Class: Safe(() => info?.Class.ToString() ?? ""),
            Role: Safe(() => info?.Role.ToString() ?? ""),
            Level: Safe(() => entity.Experience?.Level ?? 0),
            X: position.x,
            Y: position.y,
            Z: position.z,
            HeadingY: rotation.eulerAngles.y,
            HealthCurrent: currentHealth,
            HealthMax: maxHealth,
            HealthPercent: healthPercent,
            DistanceFromLocal: distance,
            IsLocalPlayer: isLocal);
    }

    private static bool TryGetNetworkId(BaseEntityGameObject entity, out uint networkId)
    {
        networkId = 0;
        try
        {
            networkId = entity.NetworkId.Value;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsLocalPlayer(BaseEntityGameObject entity, uint networkId)
    {
        if (networkId != 0 && EntityPlayerGameObject.LocalPlayerId.Value == networkId)
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

    private static float GetPoolValue(BaseEntityGameObject entity, PoolType poolType, bool current)
    {
        try
        {
            return current ? entity.Pools.GetCurrent(poolType) : entity.Pools.GetMax(poolType);
        }
        catch
        {
            return 0f;
        }
    }

    private static float GetDistanceFromLocal(UnityEngine.Vector3 position)
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

    private sealed record TrackedEntity(BaseEntityGameObject Entity, string EntityType);
}
