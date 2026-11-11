using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Nightborn.EditorTools
{
    /// <summary>
    /// 「Play 当前场景」：<see cref="NightbornEditorGuard"/> 把 `playModeStartScene` 锁在 L_Main，
    /// 想单独调某一关就得先 Play 再手动换场景。这里用 `SessionState` 记一个一次性请求，
    /// 进入 Play 之后把场景换过去 —— 不碰 `playModeStartScene`，所以「点 Play 进菜单」的产品行为不变，
    /// 也不会给下一次 Play 留状态（SessionState 是编辑器会话级，且用完即清）。
    /// </summary>
    [InitializeOnLoad]
    internal static class NightbornScenePlay
    {
        private const string RequestKey = "Nightborn.PlayRequestScene";

        static NightbornScenePlay()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        [MenuItem("Nightborn/Play 当前场景 _F5")]
        private static void PlayCurrentScene()
        {
            if (EditorApplication.isPlaying)
            {
                EditorApplication.isPlaying = false;
                return;
            }

            var active = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(active.path))
            {
                Debug.LogWarning("[Nightborn] 当前场景没保存过，没法用它启动 Play。");
                return;
            }

            SessionState.SetString(RequestKey, active.path);
            EditorApplication.isPlaying = true;
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.EnteredPlayMode) return;

            string path = SessionState.GetString(RequestKey, "");
            if (string.IsNullOrEmpty(path)) return;

            SessionState.EraseString(RequestKey);

            string sceneName = Path.GetFileNameWithoutExtension(path);
            var active = SceneManager.GetActiveScene();
            if (active.name == sceneName) return; // 守卫已经把我送进这一关了，不用再切

            if (Application.CanStreamedLevelBeLoaded(sceneName))
            {
                SceneManager.LoadScene(sceneName);
                Debug.Log("[Nightborn] Play 当前场景 → " + sceneName);
            }
            else
            {
                Debug.LogWarning("[Nightborn] 场景 " + sceneName + " 不在 Build Settings 里，没法切换。");
            }
        }
    }
}
