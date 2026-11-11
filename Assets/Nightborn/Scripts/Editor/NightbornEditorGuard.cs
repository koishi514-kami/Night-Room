using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Nightborn.EditorTools
{
    /// <summary>
    /// 编辑器守卫：
    /// 1) 无论当前开着哪个场景，按 Play 都进开场街区；
    /// 2) 编辑器中如果只剩一个空场景（占位模板被删掉那种情况），自动打开开场街区；
    /// 3) 提供菜单「Nightborn/检查场景连线」，排查黑屏 / 角色不动。
    /// </summary>
    [InitializeOnLoad]
    internal static class NightbornEditorGuard
    {
        private const string ScenePath = "Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity";
        private const string MenuScenePath = "Assets/Nightborn/Scenes/L_Main.unity";
        private const string Tag = "[Nightborn]";

        private static bool _autoOpened;

        static NightbornEditorGuard()
        {
            EditorApplication.delayCall += OnEditorReady;
        }

        private static void OnEditorReady()
        {
            // 有主菜单就先进菜单（产品行为）；没有就退回直接进关卡
            var menu = AssetDatabase.LoadAssetAtPath<SceneAsset>(MenuScenePath);
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null)
            {
                Debug.LogWarning(Tag + " 找不到场景 " + ScenePath + "，请确认 Assets 目录完整。");
                return;
            }

            var startScene = menu != null ? menu : scene;
            if (EditorSceneManager.playModeStartScene != startScene)
            {
                EditorSceneManager.playModeStartScene = startScene;
                Debug.Log(Tag + " 已把 Play 默认场景设为 " + (menu != null ? MenuScenePath : ScenePath));
            }

            // 编辑器里如果开着的是空场景（比如原来的 SampleScene 已删除），直接换成开场街区，
            // 免得「点 Play 什么都看不见」。
            if (!_autoOpened && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
            {
                _autoOpened = true;
                var active = SceneManager.GetActiveScene();
                bool empty = string.IsNullOrEmpty(active.path) && !active.isDirty
                             || string.IsNullOrEmpty(active.path) && Object.FindObjectOfType<Nightborn.Player.PlayerMotor>() == null;
                if (empty)
                {
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                    Debug.Log(Tag + " 已自动打开开场街区（当前是空场景）。");
                }
            }
        }

        [MenuItem("Nightborn/打开主菜单场景")]
        private static void OpenMenuScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(MenuScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Nightborn/打开开场街区场景")]
        private static void OpenStreetScene()
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("Nightborn/检查场景连线")]
        private static void ValidateScene()
        {
            var scene = SceneManager.GetActiveScene();
            var report = new System.Text.StringBuilder();
            report.AppendLine(Tag + " 当前场景：" + scene.name + " (" + (string.IsNullOrEmpty(scene.path) ? "<未保存>" : scene.path) + ")");
            report.AppendLine("  Unity " + Application.unityVersion + " / 色彩空间 " + PlayerSettings.colorSpace);

            var player = GameObject.Find("PLAYER_Ada");
            var rig = GameObject.Find("CAM_Rig");
            var cam = UnityEngine.Camera.main;
            var lantern = GameObject.Find("PLAYER_Lantern");
            var volume = GameObject.Find("POSTFX_GlobalVolume");

            report.AppendLine("  PLAYER_Ada：" + Describe(player));
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                report.AppendLine("    CharacterController：" + (cc == null ? "<缺失>" : "高 " + cc.height + " 半径 " + cc.radius));
                var motor = player.GetComponent<Nightborn.Player.PlayerMotor>();
                report.AppendLine("    PlayerMotor：" + (motor == null ? "<缺失>" : "已挂"));
            }

            report.AppendLine("  CAM_Rig：" + Describe(rig));
            report.AppendLine("  Camera.main：" + (cam == null
                ? "<缺失：没有带 MainCamera tag 的相机>"
                : cam.name + " @ " + cam.transform.position + " clip " + cam.nearClipPlane + "-" + cam.farClipPlane));
            report.AppendLine("  PLAYER_Lantern：" + Describe(lantern));
            report.AppendLine("  POSTFX_GlobalVolume：" + Describe(volume));

            var urp = GraphicsSettings.currentRenderPipeline;
            report.AppendLine("  渲染管线：" + (urp == null ? "<缺失：Built-in>" : urp.name));

            if (cam != null)
            {
                if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 40f))
                    report.AppendLine("  相机正前方 40m 首个碰撞：" + hit.collider.name + " 距离 " + hit.distance.ToString("F2"));
                else
                    report.AppendLine("  相机正前方 40m 无碰撞（可能对着空处）");
            }

            Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("Nightborn 场景检查", "结果已输出到 Console。", "好");
        }

        private static string Describe(GameObject go)
        {
            return go == null ? "<缺失：未找到>" : "已找到 @ " + go.transform.position.ToString("F2");
        }
    }
}
