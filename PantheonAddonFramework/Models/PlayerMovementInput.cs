namespace PantheonAddonFramework.Models;

[Flags]
public enum PlayerMovementInput
{
    None = 0,
    Forward = 1,
    Backward = 2,
    Left = 4,
    Right = 8,
    Sprint = 32,
    TurnLeft = 64,
    TurnRight = 128
}
