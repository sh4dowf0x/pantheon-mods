namespace PantheonAddonFramework.Models;

public sealed record CombatResultApplied(
    double Time,
    string AttackerName,
    string DefenderName,
    float Damage,
    float BeforeMitigationDamage,
    float MitigatedDamage,
    int ThreatToApply,
    string DamageType,
    string DamageStyle,
    string WeaponType,
    string ImpactType,
    string CombatResultType,
    string AbilityName,
    string BuffName);
