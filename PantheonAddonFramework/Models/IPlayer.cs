namespace PantheonAddonFramework.Models;

public interface IPlayer
{
    IEntityStats Stats { get; }
    
    IPlayerInventory Inventory { get; }
    
    ICurrency InventoryCurrency { get; }
    
    ICurrency BankCurrency { get; }

    long CharacterId { get; }
    
    string Name { get; }

    int Level { get; }

    bool IsMale { get; }

    string Race { get; }

    string Class { get; }

    PlayerExperience? GetExperience();

    PlayerPosition? GetPosition();

    bool TryApplyMovementInput(PlayerMovementInput input);

    bool TryApplyMovementInput(PlayerMovementInput input, double seconds);

    bool IsLocalPlayer { get; }
}
