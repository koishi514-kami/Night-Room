using UnityEngine;
using Nightborn.Player;
using Nightborn.UI;

namespace Nightborn.Level
{
    /// <summary>
    /// 教程 / 关键处一行提示（HANDOVER §4 P0-2，设计决策 20）。
    ///
    /// 玩家第一次踩进触发体时，由 <see cref="ObjectiveHud"/> 在目标文案下面显示一行话，几秒后淡出。
    /// 触发体不挡路（isTrigger），也不会被画出来。
    ///
    /// 触发语义（静态审计确认）：
    ///  · `once = true` 时靠 `_fired` 只播一次；离开再进不重播，被抓回检查点也不重播
    ///    （组件状态跟着场景走，回检查点不会重建）。重新加载关卡才会重置 —— 这是期望行为。
    ///  · 出生点 (0, 0.2, −17) 不在任何提示体里：提示体中心 z = −13 / −6、z 方向 3 m，
    ///    也就是 −14.5…−11.5 和 −7.5…−4.5，离出生点还有 2.5 m 余量，所以**不会开局糊屏**。
    ///    另外 `CharacterController` 出生时如果正好和触发器重叠，Unity 不会补发 OnTriggerEnter，
    ///    这一条也可以当保险。
    ///  · 两个提示体相距 7 m（走速 3.2 m/s ≈ 2.2 s），而提示时长 5.5 s —— 必然会「前一条还在播」。
    ///    以前是后一条直接顶掉前一条；现在 <see cref="ObjectiveHud.ShowHint"/> 内部排队依次播。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class TutorialHint : MonoBehaviour
    {
        [SerializeField, TextArea] private string message = "";
        [SerializeField] private float duration = 5f;
        [Tooltip("只触发一次（默认）。关掉就是每次踩进来都提示。")]
        [SerializeField] private bool once = true;

        private bool _fired;

        public string Message => message;
        public bool HasFired => _fired;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true; // 不挡路
        }

        /// <summary>运行时 / 编辑器工具注入文案。</summary>
        public void Configure(string text, float seconds = 5f, bool onlyOnce = true)
        {
            message = text;
            duration = seconds;
            once = onlyOnce;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (once && _fired) return;
            if (string.IsNullOrEmpty(message)) return;

            // 已经通关了就别再推教程（通关瞬间玩家还在场景里走动）
            if (LevelFlow.Instance != null && LevelFlow.Instance.IsComplete) return;

            if (other.GetComponentInParent<PlayerMotor>() == null) return;

            _fired = true;
            ObjectiveHud.ShowHint(message, duration);
        }
    }
}
