// ============================================================================
// FreeCameraController.cs
// 简易飞行相机控制器，方便在测试场景中观察 HUD
// WASD 移动 / 鼠标右键旋转 / 滚轮加速
// ============================================================================

using UnityEngine;

namespace GPUHud.Demo
{
    /// <summary>
    /// 自由飞行相机控制器
    /// </summary>
    public class FreeCameraController : MonoBehaviour
    {
        [SerializeField] private float _moveSpeed = 20f;
        [SerializeField] private float _lookSpeed = 3f;
        [SerializeField] private float _sprintMultiplier = 3f;
        [SerializeField] private float _scrollSpeed = 10f;

        private float _yaw;
        private float _pitch;

        private void Start()
        {
            var euler = transform.eulerAngles;
            _yaw = euler.y;
            _pitch = euler.x;
        }

        private void Update()
        {
            // 鼠标右键旋转
            if (Input.GetMouseButton(1))
            {
                _yaw += Input.GetAxis("Mouse X") * _lookSpeed;
                _pitch -= Input.GetAxis("Mouse Y") * _lookSpeed;
                _pitch = Mathf.Clamp(_pitch, -89f, 89f);
                transform.rotation = Quaternion.Euler(_pitch, _yaw, 0f);
            }

            // 滚轮调整速度
            _moveSpeed += Input.mouseScrollDelta.y * _scrollSpeed;
            _moveSpeed = Mathf.Clamp(_moveSpeed, 1f, 200f);

            // WASD + QE 移动
            float speed = _moveSpeed;
            if (Input.GetKey(KeyCode.LeftShift))
                speed *= _sprintMultiplier;

            var move = Vector3.zero;
            if (Input.GetKey(KeyCode.W)) move += transform.forward;
            if (Input.GetKey(KeyCode.S)) move -= transform.forward;
            if (Input.GetKey(KeyCode.A)) move -= transform.right;
            if (Input.GetKey(KeyCode.D)) move += transform.right;
            if (Input.GetKey(KeyCode.Q)) move -= transform.up;
            if (Input.GetKey(KeyCode.E)) move += transform.up;

            transform.position += move.normalized * speed * Time.deltaTime;
        }
    }
}
