using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 不依赖场景配置：每个场景加载后自动挂自检浮层（菜单场景除外）。
    /// 注意 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 只在**首个**场景后跑一次，
    /// 所以这里再订阅一次 sceneLoaded，保证从菜单进关卡时浮层也在。
    /// </summary>
    internal static class NightbornRuntimeBoot
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            EnsureHud();
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureHud();
        }

        private static void EnsureHud()
        {
            if (Object.FindObjectOfType<NightbornDebugHud>() != null) return;
            // 菜单场景不挂调试浮层
            if (Object.FindObjectOfType<Nightborn.Menu.NightbornMenu>() != null) return;

            var go = new GameObject("DBG_NightbornHud");
            go.AddComponent<NightbornDebugHud>();
        }
    }
}
