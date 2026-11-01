using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace Nightborn.EditorTools
{
    /// <summary>
    /// 编辑器守卫：保证「无论当前开着哪个场景，按 Play 一定进开场街区」，
    /// 并提供一键检查场景连线（排查黑屏 / 角色不动）。
    /// </summary>
    [InitializeOnLoad]
    internal static class NightbornEditorGuard
    {
        private const string ScenePath = "Assets/Nightborn/Scenes/L_Ch01_S01_Street.unity";
        private const string StrictPrefix = "[Nightborn]";

        static NightbornEditorGuard()
        {
            EditorApplication.delayCall += ApplyPlayModeScene;
        }

        private static void ApplyPlayModeScene()
        {
            var scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null) return;
            if (EditorSceneManager.playModeStartScene != scene)
            {
                EditorSceneManager.playModeStartScene = scene;
                Debug.Log(StrictPrefix + " 已设置 Play 默认场景：" + ScenePath);
            }
        }

        [MenuItem("Nightborn/检查场景连线")]
        private static void ValidateScene()
        {
            var scene = SceneManager.GetActiveScene();
            var report = new System.Text.StringBuilder();
            report.AppendLine(StrictPrefix + " 当前场景：" + scene.name + " (" + scene.path + ")");
            report.AppendLine("  Unity " + Application.unityVersion + "  /  " + PlayerSettings.colorSpace);

            var player = GameObject.Find("PLAYER_Ada");
            var rig = GameObject.Find("CAM_Rig");
            var cam = UnityEngine.Camera.main;
            var lantern = GameObject.Find("PLAYER_Lantern");
            var volume = GameObject.Find("POSTFX_GlobalVolume");

            report.AppendLine("  PLAYER_Ada：" + Describe(player));
            if (player != null)
            {
                var cc = player.GetComponent<CharacterController>();
                report.AppendLine("    CharacterController：" + (cc == null ? "<color=missing>" : "高 " + cc.height + " 半径 " + cc.radius));
                var motor = player.GetComponent<Nightborn.Player.PlayerMotor>();
                report.AppendLine("    PlayerMotor：" + (motor == null ? "<color=missing>" : "已挂"));
            }

            report.AppendLine("  CAM_Rig：" + Describe(rig));
            report.AppendLine("  Camera.main：" + (cam == null
                ? "<color=missing 没有 MainCamera tag 的相机>"
                : cam.name + " @ " + cam.transform.position + " clip " + cam.nearClipPlane + "-" + cam.farClipPlane));
            report.AppendLine("  PLAYER_Lantern：" + Describe(lantern));
            report.AppendLine("  POSTFX_GlobalVolume：" + Describe(volume));

            var urp = GraphicsSettings.currentRenderPipeline;
            report.AppendLine("  渲染管线：" + (urp == null ? "<color=missing Built-in>" : urp.name));

            if (cam != null)
            {
                if (Physics.Raycast(cam.transform.position, cam.transform.forward, out RaycastHit hit, 40f))
                    report.AppendLine("  相机正前方 40m 首个碰撞：" + hit.collider.name + " 距离 " + hit.distance.ToString("F2"));
                else
                    report.AppendLine("  相机正前方 40m 无碰撞（可能朝空处）");
            }

            Debug.Log(report.ToString());
            EditorUtility.DisplayDialog("Nightborn 场景检查", "结果已输出到 Console。", "好");
        }

        private static string Describe(GameObject go)
        {
            return go == null
                ? "<color=missing 未找到>"
                : "已找到 @ " + go.transform.position.ToString("F2");
        }
    }
}
