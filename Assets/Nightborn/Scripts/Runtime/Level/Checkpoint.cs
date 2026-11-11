using UnityEngine;

namespace Nightborn.Level
{
    /// <summary>检查点：玩家踩进去即记录（自动存档）。</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        [SerializeField] private int index;

        private Renderer[] _renderers;
        private bool _activated;

        public int Index => index;

        private void Awake()
        {
            _renderers = GetComponentsInChildren<Renderer>();
            var col = GetComponent<Collider>();
            col.isTrigger = true;
        }

        private void Start()
        {
            LevelFlow.Instance?.RegisterCheckpoint(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other.GetComponentInParent<Nightborn.Player.PlayerMotor>() == null) return;
            if (_activated) return;

            _activated = true;
            LevelFlow.Instance?.ActivateCheckpoint(this);

            foreach (var r in _renderers)
            {
                if (r == null) continue;
                var mat = r.material;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", new Color(0.88f, 0.64f, 0.29f) * 1.6f);
            }
        }
    }
}
