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
                    changed = true;
                    hintsAdded++;
                }

                // 位置每次都写：文案表里挪了提示位置，单独跑菜单「装饰关卡」也能跟上（幂等，不做增量）。
                if (go.transform.position != hint.Center)
                {
                    go.transform.position = hint.Center;
                    changed = true;
                }

                var col = go.GetComponent<BoxCollider>();
                if (col == null) col = go.AddComponent<BoxCollider>();
                col.isTrigger = true;
                col.center = Vector3.zero;
                col.size = new Vector3(hint.Size.x, hint.Size.y, hint.Size.z);

                var tutorial = go.GetComponent<TutorialHint>();
                if (tutorial == null) tutorial = go.AddComponent<TutorialHint>();
                if (tutorial.Message != hint.Message) tutorial.Configure(hint.Message, 5.5f);

                // 提示触发体是 10×3×3 的隐形体积；不标 ignoreFromBuild 的话它会被烘成 NavMesh 上的洞，
                // 敌人的巡逻路径正好从 z=−13 / −6 穿过（街区守卫、教堂猎犬都在里面）。
                NightbornBootstrap.IgnoreNavMesh(go);
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
        /// 关卡表：目标文案 + 教程提示。
        ///
        /// 2026-11-11 重写原则（关卡设计审计）：
        ///  · 每个提示只讲**这一关新增的那一件事**，不重复已经教过的（E 熄灯 / 蹲暗处 / 潜行在 S01 就讲完）；
        ///  · 每关 3 个提示：两个在出生走廊（z=−13 / −6 的开阔带），第三个放在**需要新机制的那个门口**——
        ///    S01 的 Tab 提示必须立在北门薄幕前，放在 20 m 外等于没讲；
        ///  · 一行、≤ 40 个汉字（ObjectiveHud 的提示行宽 1400 px / 字号 24）。
        /// </summary>
        private static Dictionary<string, LevelSpec> BuildTable()
        {
            var table = new Dictionary<string, LevelSpec>();

            // ---------- 第一章 · 街区 ----------
            table["L_Ch01_S01_Street"] = new LevelSpec
            {
                Objective = "北门薄幕后的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "守卫靠光看清你：站进暗处，他确认得慢得多。"),
                    Hint(0f, -6f, "按 E 吹灭街灯。贴着墙走，别站在街心。"),
                    // P0：北门被薄幕封死，而这关一个字没提 Tab —— 玩家会站在幕前等一堵不存在的门打开。
                    Hint(0f, 13f, "北门被薄幕封死：按 Tab 化成影身就能穿过去。")
                }
            };

            table["L_Ch01_S02_Street"] = new LevelSpec
            {
                Objective = "北门薄幕后的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "执灯修士的灯吹不灭，只能绕开或等巡照间隙。"),
                    Hint(0f, -6f, "被看见不等于失败：离开视线后确认进度会慢慢退。"),
                    Hint(0f, 13f, "北门照旧是薄幕：Tab 影身 6 秒，够走进院内。")
                }
            };

            table["L_Ch01_S03_Street"] = new LevelSpec
            {
                Objective = "北门薄幕后的出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "掩体只剩三处：顶住箱子按方向键，用它挡光。"),
                    Hint(0f, -6f, "街灯挪到必经路上了：先吹灭再穿，别硬闯。"),
                    Hint(0f, 13f, "薄幕没有门闩：Tab 影身穿过去就是出口。")
                }
            };

            // ---------- 第二章 · 教堂 ----------
            table["L_Ch02_S01_Church"] = new LevelSpec
            {
                Objective = "穿过圣所拱门，抵达北门",
                Hints = new[]
                {
                    Hint(0f, -13f, "夜巡犬靠气味：10 m 内快跑会被咬住，慢走几乎不留。"),
                    Hint(0f, -6f, "柱廊两座烛台都能按 E 吹灭，吹出一片黑区。"),
                    // 隔屏两座拱门都挂幕：第二段的「穿幕」从此是真的，提示立在幕前
                    Hint(0f, 7f, "圣所两座拱门都挂着薄幕：Tab 影身穿过去。")
                }
            };

            table["L_Ch02_S02_Church"] = new LevelSpec
            {
                Objective = "穿过圣所拱门，抵达北门",
                Hints = new[]
                {
                    Hint(0f, -13f, "两只猎犬分巡主廊与侧廊：等它们背对你再挪。"),
                    Hint(0f, -6f, "血视（Q）3 秒标出敌人与遗物，冷却 25 秒。"),
                    Hint(0f, 7f, "两道拱门挑犬背对的那侧，影身穿幕只要 6 秒。")
                }
            };

            table["L_Ch02_S03_Church"] = new LevelSpec
            {
                Objective = "穿过圣所拱门，抵达北门",
                Hints = new[]
                {
                    Hint(0f, -13f, "烛台只剩一盏：留到拱门前再造黑，别提前用掉。"),
                    Hint(0f, -6f, "猎犬加修士：先等犬走开，再绕修士的光锥。"),
                    Hint(0f, 7f, "两座拱门都封着幕，西边多一名修士 —— 走东边。")
                }
            };

            // ---------- 第三章 · 宅邸 ----------
            table["L_Ch03_S01_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的塔门出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "钟楼看守是重甲，吸不动血，只能躲。"),
                    // 坡道口在 (−10, −13)，提示就立在坡脚
                    Hint(-9f, -10.5f, "西侧长坡直通二层：坡顶接回廊，栏杆留了缺口。"),
                    Hint(-9.5f, -0.5f, "塔门挂着薄幕：Tab 影身 6 秒够走到出口。", 6.5f)
                }
            };

            table["L_Ch03_S02_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的塔门出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "看守在二层、守卫在地面，会同时施压：先看谁背对。"),
                    Hint(0f, -6f, "吸血只能从背后或侧面，1.4 秒锁定期间你不能动。"),
                    Hint(-9.5f, -0.5f, "回廊北端是塔门：薄幕一穿就到出口。", 6.5f)
                }
            };

            table["L_Ch03_S03_Mansion"] = new LevelSpec
            {
                Objective = "上到二层回廊的塔门出口",
                Hints = new[]
                {
                    Hint(0f, -13f, "四敌同场：坡道是唯一的上下通道，别在坡上被堵。"),
                    Hint(0f, -6f, "化形（Tab）6 秒灭光穿幕，空档就靠它抢。"),
                    Hint(-9.5f, -0.5f, "塔门前先看看守在哪：等他背对再化形穿门。", 6.5f)
                }
            };

            // ---------- 终章 · 主教座堂 ----------
            table["L_Ch04_S01_Cathedral"] = new LevelSpec
            {
                // 终章的目标文案由 ObjectiveHud 按 BossRitual 阶段实时给，这里只是占位标签
                TargetName = "BOSS_Altar",
                Objective = "完成祭坛的三段仪式",
                Hints = new[]
                {
                    Hint(0f, -13f, "第一段：按 E 吹灭三盏圣灯，护罩才会裂开。"),
                    Hint(0f, -6f, "第二段：东西侧堂各有一道薄幕，Tab 影身穿进去。"),
                    Hint(0f, 6f, "第三段：喝压光药剂（1），从台阶走上祭坛贴近它。")
                }
            };

            return table;
        }

        private static HintSpec Hint(float x, float z, string message, float y = 1.5f)
        {
            return new HintSpec
            {
                Center = new Vector3(x, y, z),
                Size = new Vector3(HintSize.x, HintSize.y, HintSize.x * 0.3f),
                Message = message
            };
        }
    }
}
