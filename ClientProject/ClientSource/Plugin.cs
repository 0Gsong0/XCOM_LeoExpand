using HarmonyLib;

namespace XCOM_LeoExpand
{
    public partial class Plugin : IAssemblyPlugin
    {
        // Client-specific code
        partial void InitializeClient(Harmony harmony)
        {
            TLCharacterControlDebug.Init(harmony);
        }
    }
}
