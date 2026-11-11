using System;
using UnityEngine;
using Nightborn.Interaction;

namespace Nightborn.Level
{
    /// <summary>
    /// 可吹灭的街灯（M0 光操纵三件套之一：熄灯）。
    /// 按 E 吹灭 → 关掉灯光 + 熄灭灯球自发光，制造一片可以通行的黑区。
    /// </summary>
    public class DouseableLamp : MonoBehaviour, IInteractable
    {
        [SerializeField] private Light[] lights;
        [SerializeField] private Renderer[] bulbs;
        [SerializeField] private Color bulbColor = new Color(0.878f, 0.643f, 0.294f, 1f);

        private bool _doused;

        public bool CanInteract => !_doused;

        /// <summary>任意一盏灯被吹灭（音频等系统订阅）。</summary>
        public static event Action<Vector3> DousedEvent;
        public string Prompt => "吹灭街灯";
        public bool Doused => _doused;

        private void Awake()
        {
            if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>();
            if (bulbs == null || bulbs.Length == 0) bulbs = GetComponentsInChildren<Renderer>();
        }

        public void Interact(GameObject actor)
        {
            if (_doused) return;
            _doused = true;

            foreach (var l in lights)
            {
                if (l != null) l.enabled = false;
            }

            foreach (var r in bulbs)
            {
                if (r == null) continue;
                var mat = r.material;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", bulbColor * 0.02f);
            }

            DousedEvent?.Invoke(transform.position);
            Debug.Log("[Nightborn] 吹灭街灯：" + name);
        }
    }
}
