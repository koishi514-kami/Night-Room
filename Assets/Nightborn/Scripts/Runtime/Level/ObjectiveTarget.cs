using System.Collections.Generic;
using UnityEngine;

namespace Nightborn.Level
{
    /// <summary>
    /// 目标点：告诉 HUD「该往哪走」（HANDOVER §4 P0-1 的导航端）。
    ///
    /// 挂在出口 / 祭坛 / 遗物这类物体上，`ObjectiveHud` 每帧从静态表里挑一个未完成的、优先级最小的目标导航。
    /// 优先级数字小的先导（遗物 20 会被出口 10 压住 —— 也就是默认先导出口，拿完遗物后仍导出口）。
    ///
    /// 现状（静态审计确认）：`Editor/NightbornObjectiveSetup` 只给 `LEVEL_Exit` / `BOSS_Altar`
    /// 挂目标点，所以每个关卡场景里实际只有一个目标；`priority` 的 10 / 20 分级目前**没有第二种目标**
    /// 参与竞争（遗物不会成为导航目标），`MarkDone()` 由 `ExitGoal` 在通关那一刻调用
    /// （通关后箭头不再指着出口）。保留这两套 API 是为了以后加「先拿遗物再出口」这类多目标导航。
    /// </summary>
    [DisallowMultipleComponent]
    public class ObjectiveTarget : MonoBehaviour
    {
        /// <summary>场景里所有目标点（OnEnable 注册，OnDisable 注销）。</summary>
        public static readonly List<ObjectiveTarget> All = new List<ObjectiveTarget>();

        [SerializeField] private string label = "";
        [Tooltip("数字小的先导航。")]
        [SerializeField] private int priority = 10;
        [Tooltip("箭头瞄的位置相对物体原点的偏移（一般抬到胸口高度）。")]
        [SerializeField] private Vector3 aimOffset = new Vector3(0f, 1.2f, 0f);

        private bool _done;

        public string Label => label;
        public int Priority => priority;
        public bool IsDone => _done;
        public bool HasLabel => !string.IsNullOrEmpty(label);
        public Vector3 AimPoint => transform.position + aimOffset;

        private void OnEnable()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        private void OnDisable()
        {
            All.Remove(this);
        }

        public void SetLabel(string value)
        {
            label = value;
        }

        public void MarkDone()
        {
            _done = true;
        }

        /// <summary>
        /// 当前该导航的目标：未完成里优先级最小的；同优先级取更近的。没有就返回 null。
        ///
        /// 顺手把静态表里的空引用压掉：`All` 是静态的，跨场景不清空；虽然 `OnDisable` 会注销，
        /// 但被 `DestroyImmediate`（编辑器工具 / 换场景的非标准路径）干掉的物体会留空位，
        /// 于是 `All` 会随场景切换越来越长 —— 每次 Resolve 顺便压实，长度恒定。
        /// </summary>
        public static ObjectiveTarget Resolve(Vector3 from)
        {
            ObjectiveTarget best = null;
            int alive = 0;

            for (int i = 0; i < All.Count; i++)
            {
                var t = All[i];
                if (t == null) continue;

                if (alive != i) All[alive] = t;
                alive++;

                if (t._done) continue;

                if (best == null)
                {
                    best = t;
                    continue;
                }

                if (t.priority < best.priority) best = t;
                else if (t.priority == best.priority &&
                         Vector3.SqrMagnitude(t.AimPoint - from) < Vector3.SqrMagnitude(best.AimPoint - from))
                    best = t;
            }

            if (alive < All.Count) All.RemoveRange(alive, All.Count - alive);
            return best;
        }
    }
}
