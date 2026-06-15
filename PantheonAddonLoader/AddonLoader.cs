using System.Runtime.Loader;
using Il2CppInterop.Runtime.Injection;
using MelonLoader;
using PantheonAddonFramework;
using PantheonAddonLoader.AddonComponents;
using PantheonAddonLoader.AddonManagement;
using PantheonAddonLoader.Events;
using PantheonAddonLoader.Models;
using PantheonAddonLoader.UI;

namespace PantheonAddonLoader;

public class AddonLoader : MelonMod
{
    public const string ModVersion = "1.0.0";
    
    private static readonly string GameAddonsFolderPath = Path.Combine(AppContext.BaseDirectory, "Mods", "PantheonAddons");
    private static readonly string LegacyAddonsFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PantheonAddons");
    private static AssemblyLoadContext? _assemblyLoadContext;
    
    public static readonly List<Addon> LoadedAddons = new();
    public static readonly WindowPanelEvents WindowPanelEvents = new();
    public static readonly LocalPlayerEvents LocalPlayerEvents = new();
    public static readonly PlayerEvents PlayerEvents = new();
    public static readonly EntityEvents EntityEvents = new();
    public static readonly LifecycleEvents LifecycleEvents = new();
    public static readonly ChatEvents ChatEvents = new();
    public static readonly CombatEvents CombatEvents = new();

    public static readonly CustomAssetManager CustomAssetManager = new();
    public static readonly CustomChatCommands CustomChatCommands = new();

    public static bool HasUpdated { get; private set; }
    
    public override void OnInitializeMelon()
    {
        ClassInjector.RegisterTypeInIl2Cpp<AddonPointerClickHandler>();

        if (!Directory.Exists(GameAddonsFolderPath))
        {
            MelonLogger.Msg($"Creating addons folder {GameAddonsFolderPath}");
            Directory.CreateDirectory(GameAddonsFolderPath);
        }

        if (!Directory.Exists(LegacyAddonsFolderPath))
        {
            Directory.CreateDirectory(LegacyAddonsFolderPath);
        }
        
        // MelonLoader's OnApplicationQuit doesn't fire unless the game shuts down cleanly
        // e.g., it does not fire if the console window is closed, so instead listen for ProcessExit to save
        // when closed via the console window
        AppDomain.CurrentDomain.ProcessExit += (s, e) => 
        {
            MelonPreferences.Save();
        };
        LoadAddons();
    }

    public override void OnUpdate()
    {
        HasUpdated = true;
        EntityRegistry.PublishPeriodicUpdates();
        WorldItemRegistry.PublishPeriodicUpdates();
        LifecycleEvents.OnUpdate.Raise();
    }

    public static void ReloadAddons()
    {
        foreach (var addon in LoadedAddons)
        {
            addon.Dispose();
        }

        LoadAddons();
    }
    
    private static void LoadAddons()
    {
        LoadedAddons.Clear();
        
        MelonLogger.Msg($"Loading addons in {GameAddonsFolderPath}");
        MelonLogger.Msg($"Loading legacy addons in {LegacyAddonsFolderPath}");
        
        _assemblyLoadContext?.Unload();

        _assemblyLoadContext = new AssemblyLoadContext("Addons", true);

        var loadedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        
        foreach (var addonFile in GetAddonFiles(loadedFileNames))
        {
            try
            {
                // Read using a stream instead of LoadFromFile to prevent locking, and load to a separate assembly context
                // so that we can unload it later, as MelonLoader doesn't like loading an assembly with the same name as
                // an already loaded assembly
                using var reader = File.OpenRead(addonFile);
                var assembly = _assemblyLoadContext.LoadFromStream(reader);
                foreach (var type in assembly.GetTypes())
                {
                    if (!type.IsSubclassOf(typeof(Addon)))
                    {
                        continue;
                    }
                
                    try
                    {
                        var addon = ScriptActivator.ActivateAddon(type);
                        if (addon != null)
                        {
                            LoadedAddons.Add(addon);
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Error($"Failed to activate addon type {type.FullName} from {Path.GetFileName(addonFile)}: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Failed to load addon assembly {addonFile}: {ex}");
            }
        }
    }

    private static IEnumerable<string> GetAddonFiles(HashSet<string> loadedFileNames)
    {
        foreach (var folderPath in new[] { GameAddonsFolderPath, LegacyAddonsFolderPath })
        {
            if (!Directory.Exists(folderPath))
            {
                continue;
            }

            foreach (var addonFile in Directory.GetFiles(folderPath, "*.dll"))
            {
                var fileName = Path.GetFileName(addonFile);
                if (!loadedFileNames.Add(fileName))
                {
                    MelonLogger.Warning($"Skipping duplicate addon DLL {addonFile}; an addon with this file name was already loaded from a higher-priority folder.");
                    continue;
                }

                yield return addonFile;
            }
        }
    }
}
