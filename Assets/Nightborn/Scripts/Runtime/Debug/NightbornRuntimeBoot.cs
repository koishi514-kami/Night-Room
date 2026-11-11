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
            // 菜单场景不挂任何 HUD
            if (Object.FindObjectOfType<Nightborn.Menu.NightbornMenu>() != null) return;

            // 正式 HUD
            if (Object.FindObjectOfType<Nightborn.UI.NightbornHud>() == null)
            {
                var hudGo = new GameObject("HUD_Nightborn");
                hudGo.AddComponent<Nightborn.UI.NightbornHud>();
            }

            // 目标提示：目标一行 + 贴边箭头 + 教程文案（HANDOVER §4 P0-1/P0-2）
            if (Object.FindObjectOfType<Nightborn.UI.ObjectiveHud>() == null)
            {
                var objGo = new GameObject("HUD_Objectives");
                objGo.AddComponent<Nightborn.UI.ObjectiveHud>();
            }

            // 开发用自检浮层（默认隐藏，F1 打开）
            if (Object.FindObjectOfType<NightbornDebugHud>() == null)
            {
                var go = new GameObject("DBG_NightbornHud");
                go.AddComponent<NightbornDebugHud>();
            }
        }
    }
}
