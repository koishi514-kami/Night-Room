using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 不依赖场景配置：每个场景加载后自动挂自检浮层（菜单场景除外）。
    /// 注意 `[RuntimeInitializeOnLoadMethod(AfterSceneLoad)]` 只在**首个**场景后跑一次，
    /// 所以这里再订阅一次 sceneLoaded，保证从菜单进关卡时浮层也在。
    ///
    /// 幂等性（本轮核过）：HUD 是运行时建的、不带 DontDestroyOnLoad，切场景时跟着旧场景一起销毁，
    /// 新场景再建一份 —— 不会叠出两份 HUD / AudioListener。唯一的漏洞是浮层被 SetActive(false) 收起时
    /// `FindObjectOfType` 找不到（它只搜激活物体），会再建一份，所以这里统一用 includeInactive=true 查找。
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
            EnsureOnce<Nightborn.UI.NightbornHud>("HUD_Nightborn");

            // 目标提示：目标一行 + 贴边箭头 + 教程文案（HANDOVER §4 P0-1/P0-2）
            EnsureOnce<Nightborn.UI.ObjectiveHud>("HUD_Objectives");

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            // 开发用自检浮层（默认隐藏，F1 打开；F2 跑一遍 WalkthroughTest）
            EnsureOnce<NightbornDebugHud>("DBG_NightbornHud");
#endif
        }

        /// <summary>按类型幂等创建（含未激活物体）：已经有一份就不再建。</summary>
        private static void EnsureOnce<T>(string name) where T : Component
        {
            if (Object.FindObjectOfType<T>(true) != null) return;

            var go = new GameObject(name);
            go.AddComponent<T>();
        }
    }
}
