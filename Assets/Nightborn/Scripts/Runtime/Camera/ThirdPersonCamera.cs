using UnityEngine;
using UnityEngine.InputSystem;
using Nightborn.Settings;

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
        [Tooltip("灵敏度，单位「度/像素」。0.16 ≈ 一次满屏横划转 300°。")]
        [SerializeField] private float sensitivity = 0.16f;
        [Tooltip("滚轮一格（Windows 一格 = 120）换算的距离，乘以 0.004 后一格约 0.48 m。")]
        [SerializeField] private float scrollStep = 0.004f;
        [SerializeField] private float pitchMin = -45f;
        [SerializeField] private float pitchMax = 60f;
        [SerializeField] private float heightOffset = 1.45f;
        [SerializeField] private float followSmooth = 0.06f;
        [Tooltip("竖直方向的跟随时间：比水平慢，0.4 m 台阶才不晃眼。")]
        [SerializeField] private float verticalSmooth = 0.14f;
        [SerializeField] private float collisionRadius = 0.28f;
        [SerializeField] private LayerMask collisionMask = ~0;

        // 名字不要改：主 agent 的真机验收脚本用反射改 _yaw 来摆机位。
        private float _yaw;
        private float _pitch = 12f;
        // 反转 Y：从 NightbornSettings 拉（没有设置文件时是 false，行为和改动前完全一致）
        private bool _invertY;
        private Vector3 _pivotVelocity;
        private float _pivotVelocityY;
        private Vector3 _pivot;
        private float _pivotY;
        private bool _pivotInit;
        private float _smoothedDistance = -1f;
        private UnityEngine.Camera _cam;

        // SphereCastNonAlloc 的缓冲：用来跳过玩家自己的碰撞体（起点就在胶囊体内，否则相机会被永久拉到贴脸）。
        private readonly RaycastHit[] _hitBuffer = new RaycastHit[8];

        public Transform Target => target;
        public float CurrentDistance => _smoothedDistance < 0f ? distance : _smoothedDistance;

        private void Awake()
        {
            // 先把设置里的灵敏度 / 反转 Y 拉过来：LateUpdate 第一次读 sensitivity 之前必须就位
            ApplySensitivitySetting();

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

            // 场景里序列化的是建场景时的旧默认值（-25 / 55）。这里把"旧默认值"迁移到新范围，
            // 但只在仍是旧默认值时才动，避免覆盖掉在 Inspector 里手调过的值。
            if (Mathf.Approximately(pitchMin, -25f)) pitchMin = -45f;
            if (Mathf.Approximately(pitchMax, 55f)) pitchMax = 60f;

            _cam = GetComponentInChildren<UnityEngine.Camera>();

            if (target != null)
            {
                _yaw = target.eulerAngles.y;
                _pivot = target.position + Vector3.up * heightOffset;
                _pivotY = _pivot.y;
                _pivotInit = true;
                transform.SetPositionAndRotation(_pivot, Quaternion.Euler(_pitch, _yaw, 0f));
            }
        }

        private void OnEnable()
        {
            // 在设置面板里拖灵敏度：立刻生效，不用重进场景（HANDOVER §5 第 3 条的接线）
            NightbornSettings.OnSensitivityChanged -= ApplySensitivitySetting;
            NightbornSettings.OnSensitivityChanged += ApplySensitivitySetting;
        }

        private void OnDisable()
        {
            NightbornSettings.OnSensitivityChanged -= ApplySensitivitySetting;
        }

        /// <summary>
        /// 取值优先级：**设置里明确改过的值** &gt; 场景序列化值（11 个场景里都存着 sensitivity）
        /// &gt; 代码默认 0.16。
        /// `HasSensitivity` 只在设置文件里真的写过 sensitivity 键时为 true —— 玩家只调过音量不会覆盖场景值。
        /// `invertY` 没有这层历史包袱，直接跟设置走。
        /// </summary>
        private void ApplySensitivitySetting()
        {
            if (NightbornSettings.HasSensitivity) sensitivity = NightbornSettings.Sensitivity;
            _invertY = NightbornSettings.InvertY;
        }

        private void Start()
        {
            LockCursor();
            if (target == null)
                Debug.LogError("[Nightborn] ThirdPersonCamera 没有找到目标，请检查 CAM_Rig 上的 target 字段或场景里的 PLAYER_Ada。");
        }

        private static void LockCursor()
        {
            Cursor.lockState = CursorLockMode.Locked;
            // 重新锁定后必须把 visible 也压回去：Esc 解锁时置了 true，
            // 只改 lockState 不还原 visible，编辑器里会留一个"看不见但仍在"的软光标。
            Cursor.visible = false;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.1f);
            bool focused = Application.isFocused;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                if (Cursor.lockState == CursorLockMode.Locked)
                {
                    // delta 每帧都要读掉，但失焦期间不应用：重新聚焦的第一帧 delta 会突然很大（视角瞬移）。
                    Vector2 raw = mouse.delta.ReadValue();
                    if (focused)
                    {
                        // 旧实现多乘了一个 0.1（实际 0.016 度/像素 = 转 180 度要划一万多像素，鼠标像推不动）。
                        // delta 本身就是像素量，单位直接取「度/像素」。
                        _yaw += raw.x * sensitivity;
                        // 反转 Y：把鼠标 y 增量的符号翻过来（设置里勾了就生效，不用重进场景）
                        _pitch -= raw.y * sensitivity * (_invertY ? -1f : 1f);
                        _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);
                        // 不绕回 0–360，长时间游玩 _yaw 会累积到几万度，float 精度掉光后视角会一格一格跳。
                        _yaw = Mathf.Repeat(_yaw, 360f);

                        float scroll = mouse.scroll.ReadValue().y * scrollStep;
                        distance = Mathf.Clamp(distance - scroll, minDistance, maxDistance);
                    }
                }
                else if (focused && mouse.leftButton.wasPressedThisFrame)
                {
                    LockCursor();
                }

                if (focused && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
                {
                    Cursor.lockState = CursorLockMode.None;
                    Cursor.visible = true;
                }
            }

            if (target == null) return;

            Vector3 pivotGoal = target.position + Vector3.up * heightOffset;
            if (!_pivotInit)
            {
                _pivot = pivotGoal;
                _pivotY = pivotGoal.y;
            }
            else
            {
                // 水平（XZ）用 0.06 s 追，竖直交给下面的 verticalSmooth：
                // 0.4 m 台阶每上一级目标点就瞬跳 0.4 m——水平要跟手，竖直慢半拍才不会每级都把画面顶一下。
                Vector3 flatGoal = new Vector3(pivotGoal.x, _pivot.y, pivotGoal.z);
                _pivot = Vector3.SmoothDamp(_pivot, flatGoal, ref _pivotVelocity, followSmooth);
                _pivotY = Mathf.SmoothDamp(_pivotY, pivotGoal.y, ref _pivotVelocityY, verticalSmooth);
                _pivot = new Vector3(_pivot.x, _pivotY, _pivot.z);
            }
            _pivotInit = true;

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);

            float nearest = float.PositiveInfinity;
            int hitCount = Physics.SphereCastNonAlloc(_pivot, collisionRadius, -(rot * Vector3.forward),
                _hitBuffer, distance, collisionMask, QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit h = _hitBuffer[i];
                if (h.collider == null) continue;
                // 玩家自己的 CharacterController（起点就在胶囊体内）必须跳过，否则相机会被永久拉到贴脸。
                if (target != null && h.collider.transform.root == target.root) continue;
                // 注意：起点已经嵌在墙里时 SphereCast 会给出 distance = 0 的"初始重叠"，
                // 旧实现把它和玩家自身一起跳过了 —— 贴墙那一帧相机就被放到墙里（穿墙一帧）。
                if (h.distance < nearest) nearest = h.distance;
            }

            float wanted = distance;
            if (nearest < float.PositiveInfinity)
                wanted = Mathf.Clamp(nearest - 0.05f, 0.35f, distance);

            // 拉近立刻生效（任何情况下都不许穿墙），退远用 8/s 指数追上：
            // 角落里两个面交替命中时 wanted 会逐帧跳，直接赋值就是"贴脸闪"。
            if (_smoothedDistance < 0f || wanted <= _smoothedDistance) _smoothedDistance = wanted;
            else _smoothedDistance = Mathf.Lerp(_smoothedDistance, wanted, 1f - Mathf.Exp(-8f * dt));

            Vector3 camPos = _pivot - rot * Vector3.forward * _smoothedDistance;
            transform.SetPositionAndRotation(_pivot, rot);

            if (_cam == null) _cam = GetComponentInChildren<UnityEngine.Camera>();
            if (_cam != null) _cam.transform.SetPositionAndRotation(camPos, rot);
        }
    }
}
