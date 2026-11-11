using UnityEngine;
using Nightborn.Player;

namespace Nightborn.Level
{
    /// <summary>关卡出口：玩家进入触发区即通关。</summary>
    [RequireComponent(typeof(Collider))]
    public class ExitGoal : MonoBehaviour
    {
        private bool _done;
        private int _playerLayer = -1;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;
            _playerLayer = LayerMask.NameToLayer("Player");
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_done || other == null) return;
            if (!IsPlayer(other)) return;

            var flow = LevelFlow.Instance;
            // 没有流程管理器就不算通关 —— 别把 _done 锁死，否则流程组件后补上来就再也进不了门
            if (flow == null || flow.IsComplete) return;

            _done = true;

            // 通关后 HUD 的目标箭头不该还指着出口
            var objective = GetComponent<ObjectiveTarget>();
            if (objective != null) objective.MarkDone();

            flow.CompleteLevel();
        }

        private bool IsPlayer(Collider other)
        {
            if (_playerLayer >= 0 && other.gameObject.layer == _playerLayer) return true;
            return other.GetComponentInParent<PlayerMotor>() != null;
        }
    }
}
