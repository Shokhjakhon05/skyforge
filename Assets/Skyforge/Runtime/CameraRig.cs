using Skyforge.Core;
using UnityEngine;

namespace Skyforge
{
    public enum CameraView
    {
        Chase,
        Fpv,
        /// <summary>Straight down from above the drone; the top of the screen is where the nose points.</summary>
        Top,
        /// <summary>The gun camera slung under the nose: looks down the line the weapons fly.</summary>
        Gun,
    }

    /// <summary>Chase and first-person cameras. Both read the drone's interpolated pose.</summary>
    public sealed class CameraRig
    {
        public readonly Camera Camera;
        public CameraView View = CameraView.Chase;
        /// <summary>Optical magnification: 1, 2 or 4.</summary>
        public float Zoom = 1f;

        HeightMap _map;
        float _yaw;
        float _fieldOfView = 52f;
        bool _snap = true;
        float _shake;
        Vector3 _gunAim = Vector3.forward;
        bool _snapGun = true;
        /// <summary>What the gun camera should look at: the locked target, or nothing.</summary>
        public Vector3? GunTarget;
        /// <summary>How much farther back than behind the starter quadcopter the chase camera sits; set from the aircraft's size.</summary>
        public float Reach = 1f;

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

        /// <summary>Slow circle around a point, used behind the menu and result screens.</summary>
        /// <summary>The terrain the chase camera must stay above; changes when the region does.</summary>
        public void SetMap(HeightMap map) => _map = map;

        /// <summary>Looks at a point from straight in front of it, for the world map.</summary>
        public void LookAt(Vector3 center, float distance, float side)
        {
            Transform t = Camera.transform;
            t.position = center + new Vector3(side, 0f, -distance);
            t.rotation = Quaternion.identity;
            _fieldOfView = 42f;
            Camera.fieldOfView = _fieldOfView;
            _snap = true;
        }

        /// <param name="side">Metres to slide the view sideways, so the subject sits beside a panel instead of under it.</param>
        public void Orbit(Vector3 center, float time, float side = 0f, float distance = 11f, float rise = 0.38f)
        {
            float angle = time * 0.12f;
            Vector3 eye = center + new Vector3(Mathf.Sin(angle) * distance, distance * rise, Mathf.Cos(angle) * distance);
            Vector3 target = center + Vector3.up * 0.8f;
            Vector3 right = Vector3.Cross(Vector3.up, target - eye).normalized;
            eye += right * side;
            target += right * side;
            Transform t = Camera.transform;
            t.position = eye;
            t.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
            _fieldOfView = 46f;
            Camera.fieldOfView = _fieldOfView;
            _snap = true;
        }

        /// <summary>Rattles the picture: 1 is a blast right beside the drone.</summary>
        public void Shake(float amount) => _shake = Mathf.Max(_shake, Mathf.Clamp01(amount));

        /// <summary>Jump straight to the drone on the next update instead of easing over to it.</summary>
        public void Snap() => _snap = true;

        public void Update(Vector3 position, float yawDeg, Vector2 tiltDeg, float speedShare, float deltaTime)
        {
            float yawEase = _snap ? 1f : 1f - Mathf.Exp(-5f * deltaTime);
            _yaw = Mathf.LerpAngle(_yaw, yawDeg, yawEase);
            _snap = false;
            if (View != CameraView.Gun) _snapGun = true;

            Transform t = Camera.transform;
            float targetFov;
            if (View == CameraView.Fpv)
            {
                Quaternion heading = Quaternion.Euler(0f, yawDeg, 0f);
                t.position = position + heading * new Vector3(0f, -0.02f, 0.45f);
                // A stabilised gimbal: only a hint of the airframe's lean reaches the picture.
                t.rotation = Quaternion.Euler(6f + tiltDeg.y * 0.2f, yawDeg, -tiltDeg.x * 0.3f);
                targetFov = 62f + speedShare * 10f;
            }
            else if (View == CameraView.Gun)
            {
                Quaternion heading = Quaternion.Euler(0f, yawDeg, 0f);
                t.position = position + heading * new Vector3(0f, -0.35f, 0.3f);
                // Steadied like a gimbal; it eases onto whatever the weapons are locked on.
                Vector3 aim = heading * Quaternion.Euler(24f, 0f, 0f) * Vector3.forward;
                if (GunTarget.HasValue && (GunTarget.Value - t.position).sqrMagnitude > 16f)
                    aim = (GunTarget.Value - t.position).normalized;
                _gunAim = _snapGun ? aim : Vector3.Slerp(_gunAim, aim, 1f - Mathf.Exp(-6f * deltaTime));
                _snapGun = false;
                t.rotation = Quaternion.LookRotation(_gunAim, Vector3.up);
                targetFov = 46f;
            }
            else if (View == CameraView.Top)
            {
                t.position = position + Vector3.up * 30f;
                t.rotation = Quaternion.LookRotation(Vector3.down, Quaternion.Euler(0f, yawDeg, 0f) * Vector3.forward);
                targetFov = 62f;
            }
            else
            {
                Quaternion heading = Quaternion.Euler(0f, _yaw, 0f);
                float distance = (4.6f + speedShare * 1.6f) * Reach;
                Vector3 eye = position + heading * new Vector3(0f, 1.7f * Mathf.Lerp(1f, Reach, 0.7f), -distance);
                float floor = Mathf.Max(_map.Height(eye.x, eye.z), Island.WaterLevel) + 0.7f;
                if (eye.y < floor) eye.y = floor;

                Vector3 target = position + heading * new Vector3(0f, 0.5f, 3f);
                t.position = eye;
                t.rotation = Quaternion.LookRotation(target - eye, Vector3.up);
                targetFov = 50f + speedShare * 9f;
            }

            // Magnifying by N narrows the tangent of the half-angle N times.
            if (Zoom > 1f)
                targetFov = 2f * Mathf.Atan(Mathf.Tan(targetFov * 0.5f * Mathf.Deg2Rad) / Zoom) * Mathf.Rad2Deg;
            _fieldOfView = Mathf.Lerp(_fieldOfView, targetFov, 1f - Mathf.Exp(-6f * deltaTime));
            Camera.fieldOfView = _fieldOfView;

            if (_shake > 0.001f)
            {
                t.position += Random.insideUnitSphere * (_shake * 0.45f);
                t.rotation *= Quaternion.Euler(Random.insideUnitSphere * (_shake * 1.6f));
                _shake = Mathf.MoveTowards(_shake, 0f, deltaTime * 2.2f);
            }
        }
    }
}
