using Il2Cpp;
using Il2CppPantheonPersist;
using Il2CppViNL;
using PantheonAddonFramework.Models;
using UnityEngine;

namespace PantheonAddonLoader.Models;

public class Player : IPlayer
{
    private readonly EntityPlayerGameObject _entityPlayerGameObject;
    private static Il2Cpp.PlayerInput _movementOverride = Il2Cpp.PlayerInput.None;
    private static DateTime _movementOverrideUntilUtc = DateTime.MinValue;
    private static long _movementOverrideCharacterId;
    public IEntityStats Stats { get; }
    public IPlayerInventory Inventory { get; }
    public ICurrency InventoryCurrency { get; }
    public ICurrency BankCurrency { get; }

    public Player(EntityPlayerGameObject entityPlayerGameObject)
    {
        _entityPlayerGameObject = entityPlayerGameObject;
        Stats = new EntityStats(_entityPlayerGameObject.Pools);
        InventoryCurrency = new PlayerCurrency(_entityPlayerGameObject.Currency);
        BankCurrency = new BankCurrency(_entityPlayerGameObject.BankCurrency);
        Inventory = new PlayerInventory(_entityPlayerGameObject.Inventory);
    }

    public long CharacterId => _entityPlayerGameObject.info.CharacterId;
    public string Name => _entityPlayerGameObject.info.DisplayName;
    public int Level => _entityPlayerGameObject.Experience.Level;
    public bool IsMale => _entityPlayerGameObject.info.Gender == Gender.Male;
    public string Race => _entityPlayerGameObject.info.Race.ToString();
    public string Class => _entityPlayerGameObject.info.Class.ToString();
    
    public PlayerExperience GetExperience()
    {
        var experience = _entityPlayerGameObject.Experience;
        return new PlayerExperience(experience.CalculateCurrentExperienceIntoLevel(), experience.CalculateExperienceRequiredToNextLevel(), experience.CalculatePercentThroughCurrentLevel());
    }

    public PlayerPosition? GetPosition()
    {
        if (_entityPlayerGameObject == null)
        {
            return null;
        }

        var transform = _entityPlayerGameObject.transform;
        var position = transform.position;
        return new PlayerPosition(position.x, position.y, position.z, transform.eulerAngles.y);
    }

    public TargetSnapshot? GetOffensiveTarget()
    {
        return CreateTargetSnapshot(_entityPlayerGameObject.Targets?.Offensive);
    }

    public TargetSnapshot? GetDefensiveTarget()
    {
        return CreateTargetSnapshot(_entityPlayerGameObject.Targets?.Defensive);
    }

    public TargetHealthSnapshot GetOffensiveTargetHealth()
    {
        return CreateTargetHealthSnapshot("Offensive", _entityPlayerGameObject.Targets?.Offensive);
    }

    public TargetHealthSnapshot GetDefensiveTargetHealth()
    {
        return CreateTargetHealthSnapshot("Defensive", _entityPlayerGameObject.Targets?.Defensive);
    }

    public bool TrySetOffensiveTarget(TargetSnapshot? target)
    {
        if (_entityPlayerGameObject == null || !IsLocalPlayer || target == null || target.NetworkId == 0)
        {
            return false;
        }

        try
        {
            var targets = _entityPlayerGameObject.GetComponent<Targets>();
            targets?.Rpc?.SetServerOffensiveTarget(new NetworkId(target.NetworkId));
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TrySetDefensiveTarget(TargetSnapshot? target)
    {
        if (_entityPlayerGameObject == null || !IsLocalPlayer || target == null || target.NetworkId == 0)
        {
            return false;
        }

        try
        {
            var targets = _entityPlayerGameObject.GetComponent<Targets>();
            targets?.Rpc?.SetServerDefensiveTarget(new NetworkId(target.NetworkId), false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool TryApplyMovementInput(PlayerMovementInput input)
    {
        return TryApplyMovementInput(input, 0.25);
    }

    public bool TryApplyMovementInput(PlayerMovementInput input, double seconds)
    {
        if (_entityPlayerGameObject == null || !IsLocalPlayer)
        {
            return false;
        }

        var gameInput = ToGameInput(input);
        _movementOverride = gameInput;
        _movementOverrideCharacterId = CharacterId;
        _movementOverrideUntilUtc = gameInput == Il2Cpp.PlayerInput.None
            ? DateTime.MinValue
            : DateTime.UtcNow.AddSeconds(Math.Clamp(seconds, 0.02, 0.5));

        var stateMachine = _entityPlayerGameObject.StateMachine;
        if (stateMachine == null || !stateMachine.HasPlayerPhysics)
        {
            return true;
        }

        var playerPhysics = stateMachine.GetOrCreatePlayerPhysics();
        if (playerPhysics == null || playerPhysics.mover == null)
        {
            return false;
        }

        var inputs = new Il2Cpp.PlayerCharacterInputs
        {
            Flags = gameInput
        };

        playerPhysics.mover.SetInputs(ref inputs);
        return true;
    }

    public static bool TryGetMovementOverride(Il2Cpp.IEntity entity, out Il2Cpp.PlayerInput input)
    {
        input = Il2Cpp.PlayerInput.None;
        if (_movementOverride == Il2Cpp.PlayerInput.None || DateTime.UtcNow > _movementOverrideUntilUtc)
        {
            return false;
        }

        if (entity == null || entity.Info.CharacterId != _movementOverrideCharacterId)
        {
            return false;
        }

        input = _movementOverride;
        return true;
    }

    private static Il2Cpp.PlayerInput ToGameInput(PlayerMovementInput input)
    {
        var flags = Il2Cpp.PlayerInput.None;
        if (input.HasFlag(PlayerMovementInput.Forward))
        {
            flags |= Il2Cpp.PlayerInput.Forward;
        }

        if (input.HasFlag(PlayerMovementInput.Backward))
        {
            flags |= Il2Cpp.PlayerInput.Backward;
        }

        if (input.HasFlag(PlayerMovementInput.Left))
        {
            flags |= Il2Cpp.PlayerInput.Left;
        }

        if (input.HasFlag(PlayerMovementInput.Right))
        {
            flags |= Il2Cpp.PlayerInput.Right;
        }

        if (input.HasFlag(PlayerMovementInput.Sprint))
        {
            flags |= Il2Cpp.PlayerInput.Sprint;
        }

        if (input.HasFlag(PlayerMovementInput.TurnLeft))
        {
            flags |= Il2Cpp.PlayerInput.TurnLeft;
        }

        if (input.HasFlag(PlayerMovementInput.TurnRight))
        {
            flags |= Il2Cpp.PlayerInput.TurnRight;
        }

        return flags;
    }

    public bool IsLocalPlayer => _entityPlayerGameObject.NetworkId.Value == EntityPlayerGameObject.LocalPlayerId.Value;

    private static TargetSnapshot? CreateTargetSnapshot(IEntity? entity)
    {
        if (entity == null)
        {
            return null;
        }

        try
        {
            var name = entity.Info?.DisplayName ?? "";
            var characterId = entity.Info?.CharacterId ?? 0;
            return new TargetSnapshot(name, characterId, entity.NetworkId.Value);
        }
        catch
        {
            return null;
        }
    }

    private static TargetHealthSnapshot CreateTargetHealthSnapshot(string targetType, IEntity? entity)
    {
        var currentHealth = GetPoolValue(entity, PoolType.Health, true);
        var maxHealth = GetPoolValue(entity, PoolType.Health, false);
        var currentMana = GetPoolValue(entity, PoolType.Mana, true);
        var maxMana = GetPoolValue(entity, PoolType.Mana, false);
        return new TargetHealthSnapshot(
            TargetType: targetType,
            CurrentHealth: currentHealth,
            MaxHealth: maxHealth,
            HealthPercent: CalculatePercent(currentHealth, maxHealth),
            CurrentMana: currentMana,
            MaxMana: maxMana,
            ManaPercent: CalculatePercent(currentMana, maxMana));
    }

    private static float GetPoolValue(IEntity? entity, PoolType poolType, bool current)
    {
        try
        {
            return entity == null ? 0 : current ? entity.Pools.GetCurrent(poolType) : entity.Pools.GetMax(poolType);
        }
        catch
        {
            return 0;
        }
    }

    private static float CalculatePercent(float current, float max)
    {
        return max <= 0 ? 0 : current / max * 100;
    }
}
