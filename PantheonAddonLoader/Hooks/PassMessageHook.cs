using HarmonyLib;
using Il2Cpp;
using Il2CppPantheonPersist;
using PantheonAddonFramework.Models;

namespace PantheonAddonLoader.Hooks;

[HarmonyPatch(typeof(UIChatWindows), nameof(UIChatWindows.PassMessage), typeof(string), typeof(string), typeof(ChatChannelType))]
public class PassMessageHook
{
    private static void Postfix(UIChatWindows __instance, string name, string message, ChatChannelType channel)
    {
        AddonLoader.ChatEvents.MessageReceived.Raise(new ChatMessage(name, message, channel.ToString()));
    }
}

[HarmonyPatch(typeof(UIChatWindows), nameof(UIChatWindows.PassCombatMessage), typeof(IEntity), typeof(IEntity), typeof(bool), typeof(string), typeof(ChatChannelType), typeof(CombatLogDirectionalFilter), typeof(CombatLogFilter), typeof(CombatLogPlayerFilter))]
public class PassCombatMessageHook
{
    private static void Postfix(UIChatWindows __instance, bool isDamage, string message, ChatChannelType channel, CombatLogDirectionalFilter direction, CombatLogFilter filter, CombatLogPlayerFilter playerFilter)
    {
        var sender = isDamage ? "Combat Damage" : "Combat";
        var annotatedMessage = $"[{direction}/{filter}/{playerFilter}] {message}";
        AddonLoader.ChatEvents.MessageReceived.Raise(new ChatMessage(sender, annotatedMessage, channel.ToString()));
    }
}
