using Skyforge.Core;
using UnityEngine;

namespace Skyforge
{
    public enum CameraView
    {
        Chase,
        Fpv,
    }

    /// <summary>Chase and first-person cameras. Both read the drone's interpolated pose.</summary>
    public sealed class CameraRig
    {
        public readonly Camera Camera;
        public CameraView View = CameraView.Chase;

        readonly HeightMap _map;
        float _yaw;
        float _fieldOfView = 52f;
        bool _snap = true;

        public CameraRig(Transform parent, HeightMap map)
        {
            _map = map;
            var obj = new GameObject("Camera");
            obj.transform.SetParent(parent, false);
            Camera = obj.AddComponent<Camera>();
            Camera.nearClipPlane = 0.25f;
            Camera.farClipPlane = 3200f;
            Camera.clearFlags = CameraClearFlags.Skybox;
            obj.AddComponent<AudioListener>();
        }

        /// <summary>Jump straight to the drone on the next update instead of easing over to it.</summary>
        public void Snap() => _snap = true;

        public void Update(Vector3 position, float yawDeg, Vector2 tiltDeg, float speedShare, float deltaTime)
        {
            float yawEase = _snap ? 1f : 1f - Mathf.Exp(-5f * deltaTime);
            _yaw = Mathf.LerpAngle(_yaw, yawDeg, yawEase);
            _snap = false;

            Transform t = Camera.transform;
            float targetFov;
            if (View == CameraView.Fpv)
            {
                Quaternion heading = Quaternion.Euler(0f, yawDeg, 0f);
                t.position = position + heading * new Vector3(0f, -0.02f, 0.45f);
                t.rotation = Quaternion.Euler(6f + tiltDeg.y * 0.35f, yawDeg, -tiltDeg.x * 0.7f);
                targetFov = 62f + speedShare * 10f;
            }
            else
            {
                Quaternion heading = Quaternion.Euler(0f, _yaw, 0f);
                float distance = 4.6f + speedShare * 1.6f;
                Vector3 eye = position + heading * new Vector3(0f, 1.7f, -distance);
                float floor = Mathf.Max(_map.Height(eye.x, eye.z), Island.WaterLevel) + 0.7f;
                if (eye.y < floor) eye.y = floor;

                Vector3 target = position + heading * new Vector3(0f, 0.5f, 3f);
                t.position = eye;
                t.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
                targetFov = 50f + speedShare * 9f;
            }

            _fieldOfView = Mathf.Lerp(_fieldOfView, targetFov, 1f - Mathf.Exp(-4f * deltaTime));
            Camera.fieldOfView = _fieldOfView;
        }
    }
}
