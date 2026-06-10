namespace PantheonAddonFramework.Events;

public class AddonEvent
{
    private event Action Event = delegate { };
    
    public void Subscribe(Action handler) => Event += handler;
    public void Unsubscribe(Action handler) => Event -= handler;
    internal void Raise()
    {
        foreach (Action handler in Event.GetInvocationList())
        {
            try
            {
                handler();
            }
            catch
            {
            }
        }
    }
}

public class AddonEvent<T>
{
    private event Action<T> Event = delegate { }; // Prevents null reference exceptions

    public void Subscribe(Action<T> handler) => Event += handler;
    public void Unsubscribe(Action<T> handler) => Event -= handler;
    internal void Raise(T arg)
    {
        foreach (Action<T> handler in Event.GetInvocationList())
        {
            try
            {
                handler(arg);
            }
            catch
            {
            }
        }
    }
}
