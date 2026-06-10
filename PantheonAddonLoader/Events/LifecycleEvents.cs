using PantheonAddonFramework.Events;

namespace PantheonAddonLoader.Events;

public class LifecycleEvents : ILifecycleEvents
{
    public AddonEvent OnUpdate { get; } = new();
}