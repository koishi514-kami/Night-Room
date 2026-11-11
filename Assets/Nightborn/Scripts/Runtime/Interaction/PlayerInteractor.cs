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

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private UnityEngine.Camera _cam;
        private IInteractable _current;

        public IInteractable Current => _current;

        private void Awake()
        {
            // 射线从相机方向出发，会先命中玩家自己的 CharacterController，必须排除
            mask &= ~(1 << gameObject.layer);
            _cam = UnityEngine.Camera.main;
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
            if (_cam == null) _cam = UnityEngine.Camera.main;

            // 起点放在角色胸口，方向跟相机——第三人称下从相机出发会够不到近处物体
            Vector3 origin = transform.position + Vector3.up * 1.2f;
            Vector3 dir = _cam != null ? _cam.transform.forward : transform.forward;

            // 用 NonAlloc 取全部命中再挑最近的"可交互物"，而不是"最近的那个碰撞体"：
            // 旧写法只看第一个命中，前面挡一块墙 / 一个货箱 / 一个触发体就直接返回 null，
            // 站在灯前也按不了 E。
            int count = Physics.SphereCastNonAlloc(origin, probeRadius, dir, _hits, range, mask,
                QueryTriggerInteraction.Collide);

            IInteractable best = null;
            float bestDist = float.MaxValue;

            for (int i = 0; i < count; i++)
            {
                RaycastHit h = _hits[i];
                if (h.collider == null) continue;
                if (h.collider.transform.root == transform.root) continue;
                if (h.distance >= bestDist) continue;

                var interactable = PickInteractable(h.collider);
                if (interactable == null) continue;

                best = interactable;
                bestDist = h.distance;
            }

            return best;
        }

        /// <summary>同一个物体上挂了多个 IInteractable 时，取第一个"当前可交互"的。</summary>
        private static IInteractable PickInteractable(Collider col)
        {
            var list = col.GetComponentsInParent<IInteractable>();
            if (list == null || list.Length == 0) return null;
            for (int i = 0; i < list.Length; i++)
            {
                if (list[i] != null && list[i].CanInteract) return list[i];
            }
            return null;
        }

        public void SetMask(LayerMask layerMask) => mask = layerMask;
    }
}
