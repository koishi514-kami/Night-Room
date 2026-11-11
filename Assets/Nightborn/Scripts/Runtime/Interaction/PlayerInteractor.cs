using UnityEngine;
using UnityEngine.InputSystem;

namespace Nightborn.Interaction
{
    /// <summary>
    /// 玩家交互（M0）：按 E 从相机方向做一次 SphereCast，命中 <see cref="IInteractable"/> 就触发。
    /// 键位依据 HANDOVER §12 决策 22（E = 上下文交互）。
    /// </summary>
    public class PlayerInteractor : MonoBehaviour
    {
        [SerializeField] private float range = 2.6f;
        [SerializeField] private float probeRadius = 0.25f;
        [SerializeField] private LayerMask mask = ~0;

        private IInteractable _current;

        public IInteractable Current => _current;

        private void Awake()
        {
            // 射线从相机出发，会先命中玩家自己的 CharacterController，必须排除
            mask &= ~(1 << gameObject.layer);
        }

        private void Update()
        {
            _current = FindTarget();

            var kb = Keyboard.current;
            if (kb == null || !kb.eKey.wasPressedThisFrame) return;
            if (_current == null || !_current.CanInteract) return;

            _current.Interact(gameObject);
            Debug.Log("[Nightborn] 交互：" + _current.Prompt);
        }

        private IInteractable FindTarget()
        {
            var cam = UnityEngine.Camera.main;
            Vector3 origin;
            Vector3 dir;

            // 起点放在角色胸口，方向跟相机——第三人称下从相机出发会够不到近处物体
            origin = transform.position + Vector3.up * 1.2f;
            dir = cam != null ? cam.transform.forward : transform.forward;

            if (Physics.SphereCast(origin, probeRadius, dir, out RaycastHit hit, range, mask,
                    QueryTriggerInteraction.Collide))
            {
                var interactable = hit.collider.GetComponentInParent<IInteractable>();
                if (interactable != null && interactable.CanInteract) return interactable;
            }

            return null;
        }

        public void SetMask(LayerMask layerMask) => mask = layerMask;
    }
}
