using HarmonyLib;
using Il2Cpp;
using PantheonAddonLoader.Models;

namespace PantheonAddonLoader.Hooks;

[HarmonyPatch(typeof(PlayerCharacterInputs), nameof(PlayerCharacterInputs.Current))]
public sealed class PlayerCharacterInputsCurrentHook
{
    private static void Postfix(IEntity entity, ref PlayerCharacterInputs __result)
    {
        if (Player.TryGetMovementOverride(entity, out var input))
        {
            __result.Flags |= input;
        }
    }
}
