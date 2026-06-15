using HarmonyLib;
using Il2Cpp;
using Il2CppViNL;
using PantheonAddonLoader.Models;

namespace PantheonAddonLoader.Hooks;

[HarmonyPatch(typeof(EntityNpcGameObject), nameof(EntityNpcGameObject.NetworkStart))]
public class NpcNetworkStartHook
{
    private static void Postfix(EntityNpcGameObject __instance)
    {
        EntityRegistry.AddOrUpdate(__instance, "NPC");
    }
}

[HarmonyPatch(typeof(EntityNpcGameObject), nameof(EntityNpcGameObject.NetworkStop))]
public class NpcNetworkStopHook
{
    private static void Postfix(EntityNpcGameObject __instance, NetworkObject networkObject)
    {
        EntityRegistry.Remove(__instance, "NPC");
    }
}
