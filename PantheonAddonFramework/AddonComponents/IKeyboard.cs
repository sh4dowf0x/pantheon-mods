namespace PantheonAddonFramework.AddonComponents;

public interface IKeyboard
{
    bool IsKeyDown(int keyCode);
    bool IsHeld(int keyCode);
}