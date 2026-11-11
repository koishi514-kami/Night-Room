using UnityEngine;
using UnityEngine.InputSystem;

namespace Nightborn.Cameras
{
    /// <summary>
    /// 灰盒阶段的第三人称环绕相机：鼠标转视角，滚轮调距离，射线防止穿墙。
    /// 不用 Cinemachine，先保证基础场景能跑；后面接入 Cinemachine 3 时整体替换掉。
    /// </summary>
    public class ThirdPersonCamera : MonoBehaviour
    {
        [SerializeField] private Transform target;
        [SerializeField] private float distance = 3.4f;
        [SerializeField] private float minDistance = 2.0f;
        [SerializeField] private float maxDistance = 6.0f;
        [SerializeField] private float sensitivity = 0.16f;
        [SerializeField] private float pitchMin = -25f;
        [SerializeField] private float pitchMax = 55f;
        [SerializeField] private float heightOffset = 1.45f;
        [SerializeField] private float followSmooth = 0.06f;
        [SerializeField] private float collisionRadius = 0.28f;
        [SerializeField] private LayerMask collisionMask = ~0;

        private float _yaw;
        private float _pitch = 12f;
        private Vector3 _pivotVelocity;
        private Vector3 _pivot;
        private bool _pivotInit;

        // SphereCastNonAlloc 的缓冲：用来跳过玩家自己的碰撞体（起点就在胶囊体内，否则相机会被永久拉到贴脸）。
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        public Transform Target => target;
        public float CurrentDistance => distance;

        private void Awake()
        {
            if (target == null)
            {
                var player = GameObject.Find("PLAYER_Ada");
                if (player != null) target = player.transform;
                else
                {
                    var tagged = GameObject.FindWithTag("Player");
                    if (tagged != null) target = tagged.transform;
                }
            }

            if (target != null)
            {
                _yaw = target.eulerAngles.y;
                _pivot = target.position + Vector3.up * heightOffset;
                _pivotInit = true;
                transform.SetPositionAndRotation(_pivot, Quaternion.Euler(_pitch, _yaw, 0f));
            }
        }

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (target == null)
                Debug.LogError("[Nightborn] ThirdPersonCamera 没有找到目标，请检查 CAM_Rig 上的 target 字段或场景里的 PLAYER_Ada。");
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    Vector2 delta = mouse.delta.ReadValue() * sensitivity * 0.1f;
                    _yaw += delta.x;
                    _pitch -= delta.y;
                    _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

                    float scroll = mouse.scroll.ReadValue().y * 0.01f;
                    distance = Mathf.Clamp(distance - scroll, minDistance, maxDistance);
                }
                else if (mouse.leftButton.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.Locked;
                }

                if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            if (target == null) return;

            Vector3 pivotGoal = target.position + Vector3.up * heightOffset;
            _pivot = _pivotInit
                ? Vector3.SmoothDamp(_pivot, pivotGoal, ref _pivotVelocity, followSmooth)
                : pivotGoal;
            _pivotInit = true;

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

            float wanted = distance;
            float nearest = float.PositiveInfinity;
            int hitCount = Physics.SphereCastNonAlloc(_pivot, collisionRadius, -(rot * Vector3.forward),
                _hitBuffer, distance, collisionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit h = _hitBuffer[i];
                if (h.collider == null) continue;
                // 起点重叠（玩家自己的 CharacterController）直接跳过，不做穿墙拉近。
                if (h.distance <= 0.0001f) continue;
                if (target != null && h.collider.transform.root == target.root) continue;
                if (h.distance < nearest) nearest = h.distance;
            }

            // 命中近处的墙时按实际距离夹取，下界 0.35m：既不穿墙，也不会因为 minDistance 下限
            // 把相机顶进墙体（贴墙时 1.2m 的下限会插进墙面，属于旧行为的残留）。
            if (nearest < float.PositiveInfinity)
                wanted = Mathf.Clamp(nearest - 0.05f, 0.35f, distance);

            Vector3 camPos = _pivot - rot * Vector3.forward * wanted;
            transform.SetPositionAndRotation(_pivot, rot);

            var cam = GetComponentInChildren<UnityEngine.Camera>();
            if (cam != null) cam.transform.SetPositionAndRotation(camPos, rot);
        }
    }
}
