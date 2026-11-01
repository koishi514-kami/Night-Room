using UnityEngine;
using UnityEngine.InputSystem;

namespace Nightborn.Cameras
{
    /// <summary>
    /// 灰盒阶段的第三人称环绕相机：右键/直接拖动鼠标转视角，滚轮调距离。
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

        private float _yaw;
        private float _pitch = 12f;
        private Vector3 _pivotVelocity;

        private void Start()
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (target != null) _yaw = target.eulerAngles.y;
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            if (mouse != null)
            {
                Vector2 delta = mouse.delta.ReadValue() * sensitivity * 0.1f;
                _yaw += delta.x;
                _pitch -= delta.y;
                _pitch = Mathf.Clamp(_pitch, pitchMin, pitchMax);

                float scroll = mouse.scroll.ReadValue().y * 0.01f;
                distance = Mathf.Clamp(distance - scroll, minDistance, maxDistance);

                if (mouse.rightButton.wasPressedThisFrame) Cursor.lockState = CursorLockMode.Locked;
            }

            if (target == null) return;

            Vector3 pivotGoal = target.position + Vector3.up * heightOffset;
            Vector3 pivot = Vector3.SmoothDamp(transform.position, pivotGoal, ref _pivotVelocity, followSmooth);

            Quaternion rot = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 camPos = pivot - rot * Vector3.forward * distance;

            transform.SetPositionAndRotation(pivot, rot);
            var cam = GetComponentInChildren<UnityEngine.Camera>();
            if (cam != null) cam.transform.SetPositionAndRotation(camPos, rot);
        }
    }
}
