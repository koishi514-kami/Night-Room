using UnityEngine;
using Nightborn.Player;

namespace Nightborn.Boss
{
    /// <summary>仪式门：玩家穿过即向 BossRitual 报一次（第二段的判定体）。</summary>
    [RequireComponent(typeof(Collider))]
    public class RitualGate : MonoBehaviour
    {
        [SerializeField] private BossRitual ritual;
        private bool _reported;

        /// <summary>是否已经被 Boss 记上一笔（第二段真的算过这道门）。HUD 导航用它跳过已完成的门。</summary>
        public bool Reported => _reported;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
            if (ritual == null) ritual = FindObjectOfType<BossRitual>();
        }

        private void OnTriggerEnter(Collider other)
        {
            TryReport(other);
        }

        // 玩家可能在阶段切到第二段的**那一帧**就已经站在门里（例如灭最后一盏圣灯时站在侧廊），
        // 只有 Enter 的话永远不会再触发。Stay 也判一次，_reported 让重复调用是零成本的。
        private void OnTriggerStay(Collider other)
        {
            TryReport(other);
        }

        private void TryReport(Collider other)
        {
            if (_reported || other == null || ritual == null) return;

            var motor = other.GetComponentInParent<PlayerMotor>();
            if (motor == null) return;

            // 关键：先让 Boss 判断这一笔算不算，只有它真的记下了才上锁。
            // 原来先 _reported = true 再上报，而上报在第二段之前会被直接 return ——
            // 玩家在灭完圣灯之前路过一次门，这道门就永久作废，第二段再也过不去（卡关）。
            if (!ritual.TryPassGate(this)) return;

            _reported = true;

            var shift = motor.GetComponent<ShiftForm>();
            Debug.Log("[Nightborn] 仪式门穿过：" + name + (shift != null && shift.IsShadow ? "（影身）" : "（光身）"));
        }
    }
}
