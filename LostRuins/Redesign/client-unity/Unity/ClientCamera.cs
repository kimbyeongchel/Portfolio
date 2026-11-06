// 카메라는 LateUpdate에서, 마우스 델타 × 감도 (프레임레이트와 무관, F5).
using UnityEngine;

namespace LostRuins.Client
{
    public sealed class ClientCamera : MonoBehaviour
    {
        [SerializeField] InputReader input;
        [SerializeField] Transform pivot;
        [SerializeField] float sensitivity = 0.12f;      // 도/마우스 단위
        [SerializeField] float minPitch = -45f, maxPitch = 45f;
        float yaw, pitch;

        public Quaternion YawRotation { get { return Quaternion.Euler(0f, yaw, 0f); } }
        public ushort YawU16 { get { return (ushort)(Mathf.Repeat(yaw, 360f) / 360f * 65535f); } }

        void LateUpdate()
        {
            Vector2 d = input.LookDelta;
            yaw += d.x * sensitivity;
            pitch = Mathf.Clamp(pitch - d.y * sensitivity, minPitch, maxPitch);
            pivot.rotation = Quaternion.Euler(pitch, yaw, 0f);
        }
    }
}
