#if UNITY_SERVER || UNITY_DEDICATED_SERVER
#if UNITY_SERVER
using UnityEngine;

/// <summary>
/// Windows/Linux server builds strip the IMGUI module by default.
/// Mirror's NetworkRoomManager still declares an OnGUI method, so we
/// keep a lightweight reference to GUI types to prevent stripping and
/// avoid runtime errors about missing IMGUI support.
/// </summary>
static class ServerImguiBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void PreserveImgui()
    {
        // Touch the types without allocating anything. The JIT sees these
        // references and keeps the IMGUI module available in headless builds.
        _ = typeof(GUI);
        _ = typeof(GUILayout);
    }
}
#endif
