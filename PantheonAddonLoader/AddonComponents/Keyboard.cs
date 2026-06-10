using PantheonAddonFramework.AddonComponents;
using UnityEngine;

namespace PantheonAddonLoader.AddonComponents;

public class Keyboard : IKeyboard
{
    public bool IsKeyDown(int keyCode)
    {
        return Input.GetKeyDown((KeyCode)keyCode);
    }

    public bool IsHeld(int keyCode)
    {
        return Input.GetKey((KeyCode)keyCode);
    }
}