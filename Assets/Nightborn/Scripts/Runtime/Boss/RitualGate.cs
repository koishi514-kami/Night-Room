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

        private void Awake()
        {
            var col = GetComponent<Collider>();
            col.isTrigger = true;
            if (ritual == null) ritual = FindObjectOfType<BossRitual>();
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_reported) return;
            if (other.GetComponentInParent<PlayerMotor>() == null) return;
            _reported = true;
            ritual?.NotifyGatePassed();
        }
    }
}
