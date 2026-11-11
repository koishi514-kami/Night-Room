using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Nightborn.Level;
using Nightborn.UI;

namespace Nightborn.EditorTools
{
    /// <summary>
    /// 一键盘「目标提示 + 教程提示」装饰（HANDOVER §4 P0-1 / P0-2 的接线部分）。
    ///
    /// 对每个关卡场景补三样东西：
    ///  1. `HUD_Nightborn` 上挂 <see cref="ObjectiveHud"/>（目标一行 + 贴边箭头 + 提示文案）；
    ///  2. 出口 / 祭坛挂 <see cref="ObjectiveTarget"/>（文案按本文件的关卡表）；
    ///  3. 出生走廊放两个 <see cref="TutorialHint"/> 触发体（关键处一行提示）。
    ///
    /// 幂等：已有的组件 / 物体不重复加，只更新文案。`Build Base Scene` 末尾会自动调用一次；
    /// 单独跑菜单 `Nightborn/装饰关卡（目标提示 + 教程提示）` 也行。
    ///
    /// 为什么单独一层而不是写进 Bootstrap：Bootstrap 负责体块几何，文案和提示位置是会反复调的，
    /// 混进去每次调字都要重刷 11 个场景。
    /// </summary>
    internal static class NightbornObjectiveSetup
    {
        internal struct HintSpec
        {
            public Vector3 Center;
            public Vector3 Size;
            public string Message;
        }

        internal sealed class LevelSpec
        {
            public string Objective = "";
            public string TargetName = "LEVEL_Exit";
            public HintSpec[] Hints = new HintSpec[0];
        }

        private static readonly Vector2 HintSize = new Vector2(10f, 3f);

        [MenuItem("Nightborn/装饰关卡（目标提示 + 教程提示）")]
        private static void DecorateAllMenu()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[Nightborn] Play 模式下不能装饰场景，先退出 Play。");
                return;
            }

            DecorateAll();
        }

        /// <summary>给所有关卡场景补目标 / 教程提示。BuildAll 末尾也会调它。</summary>
        internal static void DecorateAll()
        {
            var table = BuildTable();
            string returnTo = SceneManager.GetActiveScene().path;
            EditorSceneManager.SaveOpenScenes();

            int scenes = 0;
            int added = 0;

            foreach (var entry in EditorBuildSettings.scenes)
            {
                if (!entry.enabled) continue;

                string name = System.IO.Path.GetFileNameWithoutExtension(entry.path);
                LevelSpec spec;
                if (!table.TryGetValue(name, out spec)) continue;

                var scene = EditorSceneManager.OpenScene(entry.path, OpenSceneMode.Single);
                int hintCount;
                if (Decorate(scene, spec, out hintCount))
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }

                scenes++;
                added += hintCount;
            }

            Debug.Log("[Nightborn] 目标 / 教程装饰完成：" + scenes + " 个关卡场景，新增提示触发体 " + added + " 个。");

            if (!string.IsNullOrEmpty(returnTo))
                EditorSceneManager.OpenScene(returnTo, OpenSceneMode.Single);
        }

        private static bool Decorate(Scene scene, LevelSpec spec, out int hintsAdded)
        {
            hintsAdded = 0;
            bool changed = false;

            // 1) HUD 上挂 ObjectiveHud
            var hud = FindByName(scene, "HUD_Nightborn");
            if (hud != null)
            {
                if (hud.GetComponent<ObjectiveHud>() == null)
                {
                    hud.AddComponent<ObjectiveHud>();
                    changed = true;
                }
            }

            // 2) 导航目标：出口或祭坛
            var target = FindByName(scene, spec.TargetName);
            if (target != null)
            {
                var objective = target.GetComponent<ObjectiveTarget>();
                if (objective == null)
                {
                    objective = target.AddComponent<ObjectiveTarget>();
                    changed = true;
                }

                if (objective.Label != spec.Objective)
                {
                    objective.SetLabel(spec.Objective);
                    changed = true;
                }
            }
            else
            {
                Debug.LogWarning("[Nightborn] " + scene.name + " 找不到目标物体 " + spec.TargetName);
            }

            // 3) 教程提示触发体
            for (int i = 0; i < spec.Hints.Length; i++)
            {
                var hint = spec.Hints[i];
                string hintName = "HINT_" + (i + 1).ToString("00");

                var go = FindByName(scene, hintName);
                if (go == null)
                {
                    go = new GameObject(hintName);
                    go.transform.position = hint.Center;
                    changed = true;
                    hintsAdded++;
                }

                var col = go.GetComponent<BoxCollider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(hint.Size.x, hint.Size.y, hint.Size.z);

                var tutorial = go.GetComponent<TutorialHint>();
                if (tutorial == null) tutorial = go.AddComponent<TutorialHint>();
                if (tutorial.Message != hint.Message) tutorial.Configure(hint.Message, 5.5f);
            }

            return changed;
        }

        private static GameObject FindByName(Scene scene, string name)
        {
            foreach (var root in scene.GetRootGameObjects())
            {
                if (root.name == name) return root;
                var found = FindDeep(root.transform, name);
                if (found != null) return found;
            }
            return null;
        }

        private static GameObject FindDeep(Transform parent, string name)
        {
            for (int i = 0; i < parent.childCount; i++)
            {
                var child = parent.GetChild(i);
                if (child.name == name) return child.gameObject;
                var found = FindDeep(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 关卡表：目标文案 + 两个出生走廊上的提示。
        /// 两个提示都放在 z = −13 / −6 的开阔带上（Bootstrap 注释里写明这一段避开了建筑和矮墙）。
        /// </summary>
        private static Dictionary<string, LevelSpec> BuildTable()
        {
            var table = new Dictionary<string, LevelSpec>();

            table["L_Ch01_S01_Street"] = new LevelSpec
            {
                Objective = "抵达街区北侧出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "守卫靠光看清你：站在暗处，他要确认得慢得多。"),
                    Hint(0f, -6f, "按 E 吹灭街灯；贴着墙走，别站在街心。")
                }
            };

            table["L_Ch01_S02_Street"] = new LevelSpec
            {
                Objective = "抵达街区北侧出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "执灯修士的灯吹不灭：绕开他的光锥，或者等他转过去。"),
                    Hint(0f, -6f, "被看到不算失败，离开视线后确认进度会慢慢掉。")
                }
            };

            table["L_Ch01_S03_Street"] = new LevelSpec
            {
                Objective = "抵达街区北侧出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "掩体变少了：贴着箱子按方向键推，用箱子挡光。"),
                    Hint(0f, -6f, "街灯被挪到必经之路上——先熄灯再穿过去。")
                }
            };

            table["L_Ch02_S01_Church"] = new LevelSpec
            {
                Objective = "穿出教堂的西门",
                Hints = new[]
                {
                    Hint(0f, -13f, "夜巡犬闻味道：10 m 内快跑会留下气味，慢走几乎不留。"),
                    Hint(0f, -6f, "柱廊两侧有烛台，按 E 吹灭就能造出一片黑区。")
                }
            };

            table["L_Ch02_S02_Church"] = new LevelSpec
            {
                Objective = "穿出教堂的西门",
                Hints = new[]
                {
                    Hint(0f, -13f, "两只猎犬分别巡主廊和侧廊：等它们的背都对着你再走。"),
                    Hint(0f, -6f, "血视（Q）能 3 秒内标出所有敌人的位置。")
                }
            };

            table["L_Ch02_S03_Church"] = new LevelSpec
            {
                Objective = "穿出教堂的西门",
                Hints = new[]
                {
                    Hint(0f, -13f, "烛台只剩一盏了，留着当诱饵，别在门口就用掉。"),
                    Hint(0f, -6f, "猎犬加修士：先等犬走过去，再处理修士的光锥。")
                }
            };

            table["L_Ch03_S01_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "钟楼看守是重甲，吸不动血，只能躲。"),
                    Hint(0f, -6f, "坡道能拉开距离——他上坡比平地慢。")
                }
            };

            table["L_Ch03_S02_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "看守在二层，守卫在地面：先把上面的解决掉再下地。"),
                    Hint(0f, -6f, "吸血只能从背后或侧面，1.4 秒锁定期间你不能动。")
                }
            };

            table["L_Ch03_S03_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "四种敌人同场。化形（Tab）6 秒灭光且能穿薄幕，用来穿空档。"),
                    Hint(0f, -6f, "被围猎时他们会扑向你最后已知位置——立刻换方向。")
                }
            };

            table["L_Ch04_S01_Cathedral"] = new LevelSpec
            {
                // 终章的目标文案由 ObjectiveHud 按 BossRitual 阶段实时给，这里只是占位标签
                TargetName = "BOSS_Altar",
                Objective = "完成祭坛的三段仪式",
                Hints = new[]
                {
                    Hint(0f, -13f, "第一段：吹灭三盏圣灯，祭坛的护罩才会裂开。"),
                    Hint(0f, -6f, "第二段用化形（Tab）穿两道薄幕；第三段喝压光药剂（1）再贴近祭坛。")
                }
            };

            return table;
        }

        private static HintSpec Hint(float x, float z, string message)
        {
            return new HintSpec
            {
                Center = new Vector3(x, 1.5f, z),
                Size = new Vector3(HintSize.x, HintSize.y, HintSize.x * 0.3f),
                Message = message
            };
        }
    }
}
