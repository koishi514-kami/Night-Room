using UnityEngine;
using Nightborn.DebugTools;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 不依赖场景配置：进入 Play 自动挂上自检浮层，场景里忘放也能用。
    /// </summary>
    internal static class NightbornRuntimeBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void EnsureHud()
        {
            if (Object.FindObjectOfType<NightbornDebugHud>() != null) return;
            var go = new GameObject("DBG_NightbornHud");
            go.AddComponent<NightbornDebugHud>();
        }
    }
}
