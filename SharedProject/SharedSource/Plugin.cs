using HarmonyLib;

namespace XCOM_LeoExpand
{
    public partial class Plugin : IAssemblyPlugin
    {
        // These are automatically assigned by the plugin service after the Constructor is called
        public IConfigService ConfigService { get; set; }
        public IPluginManagementService PluginService { get; set; }
        public ILoggerService LoggerService { get; set; }
        public static Harmony? harmony;
        partial void InitializeClient(Harmony harmony);
        public void Initialize()
        {
            // When your plugin is loading, use this instead of the constructor for code relying on
            // the services above.
            
            // Put any code here that does not rely on other plugins.
            LoggerService.Log($"XCOM_LeoExpand Plugin Initialized.");
            harmony = new Harmony("XCOM_LeoExpand_harmonyPath");
            InitializeClient(harmony);
            TLCharacterControlSystem.Init(harmony);
        }

        public void OnLoadCompleted()
        {
            // After all plugins have loaded
            // Put code that interacts with other plugins here.
        }

        public void PreInitPatching()
        {
            //Called right after the constructor
        }

        public void Dispose()
        {
            if (harmony != null)
            {
                harmony.UnpatchSelf();
                harmony = null;
            }
        }
    }
}
