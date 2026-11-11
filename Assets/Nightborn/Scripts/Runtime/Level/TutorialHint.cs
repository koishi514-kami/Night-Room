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

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
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
            if (other.GetComponentInParent<PlayerMotor>() == null) return;

            _fired = true;
            ObjectiveHud.ShowHint(message, duration);
        }
    }
}
