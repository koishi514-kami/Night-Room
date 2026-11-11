using System.Collections.Generic;
using UnityEngine;
using Nightborn.Player;

namespace Nightborn.Level
{
    /// <summary>检查点：玩家踩进去即记录（自动存档）。</summary>
    [RequireComponent(typeof(Collider))]
    public class Checkpoint : MonoBehaviour
    {
        /// <summary>激活后的自发光（HDR 下别再往上加，1.6 那档会直接过曝成白金，见 DEVLOG §22.1）。</summary>
        private static readonly Color ActiveEmission = new Color(0.88f, 0.64f, 0.29f) * 0.9f;
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private int index;

        private Material[] _materials;      // 只实例化一次，触发时只改属性
        private bool _activated;
        private int _playerLayer = -1;

        public int Index => index;

        private void Awake()
        {
            var col = GetComponent<Collider>();
            if (col != null) col.isTrigger = true;

            _playerLayer = LayerMask.NameToLayer("Player");

            // 只对「可见」的渲染器改自发光：触发体自身的 MeshRenderer 在 Bootstrap 里是关掉的，
            // 旧实现对它照样 r.material，每次进关都白建一个材质实例。
            var renderers = GetComponentsInChildren<Renderer>();
            var mats = new List<Material>(renderers.Length);
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null || !r.enabled) continue;
                var mat = r.material;
                if (mat != null && !mats.Contains(mat)) mats.Add(mat);
            }
            _materials = mats.ToArray();
        }

        private void Start()
        {
            LevelFlow.Instance?.RegisterCheckpoint(this);
        }

        private void OnTriggerEnter(Collider other)
        {
            if (_activated || other == null) return;
            if (!IsPlayer(other)) return;

            _activated = true;
            LevelFlow.Instance?.ActivateCheckpoint(this);

            for (int i = 0; i < _materials.Length; i++)
            {
                var mat = _materials[i];
                if (mat == null) continue;
                mat.EnableKeyword("_EMISSION");
                mat.SetColor(EmissionColorId, ActiveEmission);
            }
        }

        /// <summary>先按层判定（不碰组件，最便宜），层对不上再回落到父级查找（玩家被挂在子物体上时也认）。</summary>
        private bool IsPlayer(Collider other)
        {
            if (_playerLayer >= 0 && other.gameObject.layer == _playerLayer) return true;
            return other.GetComponentInParent<PlayerMotor>() != null;
        }
    }
}
