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
        public string Prompt => "拾取血之遗物";

        private void Start()
        {
            LevelFlow.Instance?.RegisterRelic(this);
        }

        public void Interact(GameObject actor)
        {
            LevelFlow.Instance?.CollectRelic(this);
        }

        public void Collect()
        {
            _collected = true;
            if (relicRenderer != null) relicRenderer.enabled = false;
            if (glow != null) glow.enabled = false;
            foreach (var col in GetComponentsInChildren<Collider>()) col.enabled = false;
        }
    }
}
