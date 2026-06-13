namespace PantheonAddonFramework.Models;

public sealed record TargetHealthSnapshot(
    string TargetType,
    float CurrentHealth,
    float MaxHealth,
    float HealthPercent,
    float CurrentMana,
    float MaxMana,
    float ManaPercent)
{
    public bool HasTarget => MaxHealth > 0;
    public bool HasMana => MaxMana > 0;
}
