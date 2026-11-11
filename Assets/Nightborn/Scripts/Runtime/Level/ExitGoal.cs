using UnityEngine;
using Nightborn.Player;

namespace Nightborn.Level
{
    /// <summary>关卡出口：玩家进入触发区即通关。</summary>
    [RequireComponent(typeof(Collider))]
    public class ExitGoal : MonoBehaviour
    {
        private bool _done;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_done) return;
            if (other.GetComponentInParent<PlayerMotor>() == null) return;
            _done = true;
            LevelFlow.Instance?.CompleteLevel();
        }
    }
}
