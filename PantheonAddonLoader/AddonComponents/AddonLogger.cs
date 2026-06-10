using MelonLoader;
using PantheonAddonFramework.AddonComponents;

namespace PantheonAddonLoader.AddonComponents;

public class AddonLogger : ILogger
{
    public void Debug(string message)
    {
        MelonLogger.Msg(message);
    }

    public void Info(string message)
    {
        MelonLogger.Msg(message);
    }

    public void Warn(string message)
    {
        MelonLogger.Warning(message);
    }

    public void Error(string message)
    {
        MelonLogger.Error(message);
    }
}