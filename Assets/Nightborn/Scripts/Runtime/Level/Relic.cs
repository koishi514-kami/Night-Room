using System;
using UnityEngine;
using Nightborn.Interaction;

namespace Nightborn.Level
{
    /// <summary>血之遗物（每关一处可选收集品，影响结局叙事）。按 E 拾取。</summary>
    public class Relic : MonoBehaviour, IInteractable
    {
        [SerializeField] private string relicId = "relic_01";
        [SerializeField] private Renderer relicRenderer;
        [SerializeField] private Light glow;

        private bool _collected;

        public string RelicId => relicId;
        public bool Collected => _collected;
        public bool CanInteract => !_collected;

        /// <summary>任意遗物被拾取（音频等系统订阅）。</summary>
        public static event Action<Vector3> CollectedAny;
        public string Prompt => "拾取血之遗物";

        private void Start()
        {
            LevelFlow.Instance?.RegisterRelic(this);
        }

        public void Interact(GameObject actor)
        {
            if (_collected) return;
            LevelFlow.Instance?.CollectRelic(this);
        }

        public void Collect()
        {
            if (_collected) return;   // 幂等：重复调用不重复播报、不重复结算
            _collected = true;

            if (relicRenderer != null) relicRenderer.enabled = false;
            if (glow != null) glow.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
            CollectedAny?.Invoke(transform.position);
        }
    }
}
