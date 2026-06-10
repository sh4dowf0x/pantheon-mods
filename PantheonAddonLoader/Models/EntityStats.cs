using Il2Cpp;
using Il2CppPantheonPersist;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Models;

public class EntityStats : IEntityStats
{
    private readonly Pools.Logic _playerPools;

    public EntityStats(Pools.Logic playerPools)
    {
        _playerPools = playerPools;
    }

    public float CurrentHealth => _playerPools.GetCurrent(PoolType.Health);
    public float MaxHealth => _playerPools.GetMax(PoolType.Health);

    public float CurrentMana => _playerPools.GetCurrent(PoolType.Mana);
    public float MaxMana => _playerPools.GetMax(PoolType.Mana);

    public float CurrentOpportunity => _playerPools.GetCurrent(PoolType.Opportunity);
    public float MaxOpportunity => _playerPools.GetMax(PoolType.Opportunity);
    
    public float CurrentMomentum => _playerPools.GetCurrent(PoolType.Momentum);
    public float MaxMomentum => _playerPools.GetMax(PoolType.Momentum);
    
    public float CurrentVision => _playerPools.GetCurrent(PoolType.Vision);
    public float MaxVision => _playerPools.GetMax(PoolType.Vision);
    
    public float CurrentEndurance => _playerPools.GetCurrent(PoolType.Endurance);
    public float MaxEndurance => _playerPools.GetMax(PoolType.Endurance);
    
    public float CurrentResilience => _playerPools.GetCurrent(PoolType.Resilience);
    public float MaxResilience => _playerPools.GetMax(PoolType.Resilience);
    
    public float CurrentCelestialPower => _playerPools.GetCurrent(PoolType.CelestialPower);
    public float MaxCelestialPower => _playerPools.GetMax(PoolType.CelestialPower);
    
    public float CurrentEssence => _playerPools.GetCurrent(PoolType.Essence);
    public float MaxEssence => _playerPools.GetMax(PoolType.Essence);
    
    public float CurrentWrath => _playerPools.GetCurrent(PoolType.Wrath);
    public float MaxWrath => _playerPools.GetMax(PoolType.Wrath);
    
    public float CurrentReckoning => _playerPools.GetCurrent(PoolType.Reckoning);
    public float MaxReckoning => _playerPools.GetMax(PoolType.Reckoning);
    
    public float CurrentFireFocus => _playerPools.GetCurrent(PoolType.FireFocus);
    public float MaxFireFocus => _playerPools.GetMax(PoolType.FireFocus);
    
    public float CurrentBattlePoints => _playerPools.GetCurrent(PoolType.BattlePoints);
    public float MaxBattlePoints => _playerPools.GetMax(PoolType.BattlePoints);
    
    public float CurrentColdFocus => _playerPools.GetCurrent(PoolType.ColdFocus);
    public float MaxColdFocus => _playerPools.GetMax(PoolType.ColdFocus);
    
    public float CurrentArcaneFocus => _playerPools.GetCurrent(PoolType.ArcaneFocus);
    public float MaxArcaneFocus => _playerPools.GetMax(PoolType.ArcaneFocus);
    
    public float CurrentShockFocus => _playerPools.GetCurrent(PoolType.ShockFocus);
    public float MaxShockFocus => _playerPools.GetMax(PoolType.ShockFocus);
    
    public float CurrentChi => _playerPools.GetCurrent(PoolType.Chi);
    public float MaxChi => _playerPools.GetMax(PoolType.Chi);
    
    public float CurrentGateOfAnger => _playerPools.GetCurrent(PoolType.GateOfAnger);
    public float MaxGateOfAnger => _playerPools.GetMax(PoolType.GateOfAnger);
    
    public float CurrentGateOfPeace => _playerPools.GetCurrent(PoolType.GateOfPeace);
    public float MaxGateOfPeace => _playerPools.GetMax(PoolType.GateOfPeace);
    
    public float CurrentGateOfBalance => _playerPools.GetCurrent(PoolType.GateOfBalance);
    public float MaxGateOfBalance => _playerPools.GetMax(PoolType.GateOfBalance);
    
    public float CurrentGateOfSoul => _playerPools.GetCurrent(PoolType.GateOfSoul);
    public float MaxGateOfSoul => _playerPools.GetMax(PoolType.GateOfSoul);
    
    public float CurrentGateOfRelease => _playerPools.GetCurrent(PoolType.GateOfRelease);
    public float MaxGateOfRelease => _playerPools.GetMax(PoolType.GateOfRelease);
    
    public float CurrentNpcResource => _playerPools.GetCurrent(PoolType.NpcResource);
    public float MaxNpcResource => _playerPools.GetMax(PoolType.NpcResource);
    
    public float CurrentCelestialBond => _playerPools.GetCurrent(PoolType.CelestialBond);
    public float MaxCelestialBond => _playerPools.GetMax(PoolType.CelestialBond);
    
    public float CurrentReadiness => _playerPools.GetCurrent(PoolType.Readiness);
    public float MaxReadiness => _playerPools.GetMax(PoolType.Readiness);
    
    public float CurrentAbsorbShield => _playerPools.GetCurrent(PoolType.AbsorbShield);
    public float MaxAbsorbShield => _playerPools.GetMax(PoolType.AbsorbShield);
    
    public float CurrentBreath => _playerPools.GetCurrent(PoolType.Breath);
    public float MaxBreath => _playerPools.GetMax(PoolType.Breath);
    
    public float CurrentEnergy => _playerPools.GetCurrent(PoolType.Energy);
    public float MaxEnergy => _playerPools.GetMax(PoolType.Energy);
    
    public float CurrentThirst => _playerPools.GetCurrent(PoolType.Thirst);
    public float MaxThirst => _playerPools.GetMax(PoolType.Thirst);
}