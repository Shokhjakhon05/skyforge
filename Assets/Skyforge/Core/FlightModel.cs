using UnityEngine;

namespace Skyforge.Core
{
    public enum FlightMode
    {
        /// <summary>Stick sets the wanted speed; the drone holds position and brakes by itself.</summary>
        Assist,
        /// <summary>Stick sets the tilt directly; no braking, the drone coasts.</summary>
        Sport,
    }

    public struct FlightInput
    {
        /// <summary>x = strafe right, y = forward, each -1..1.</summary>
        public Vector2 Move;
        /// <summary>-1 descend .. 1 climb.</summary>
        public float Throttle;
        /// <summary>-1 turn left .. 1 turn right.</summary>
        public float Yaw;
    }

    public struct FlightState
    {
        public Vector3 Position;
        public Vector3 Velocity;
        public float YawDeg;
        public float YawRateDeg;
        /// <summary>Lean of the airframe in degrees: x = to the right, y = forward.</summary>
        public Vector2 TiltDeg;
        public bool Grounded;
    }

    /// <summary>Handling parameters of one drone. Differences between drones live here.</summary>
    public sealed class DroneSpec
    {
        public string Id = "sparrow";
        public float MaxSpeed = 16f;
        public float MaxTiltDeg = 34f;
        /// <summary>How fast the airframe reaches the wanted tilt, 1/s.</summary>
        public float TiltResponse = 7f;
        /// <summary>How hard Assist chases the wanted speed, 1/s.</summary>
        public float AssistGain = 2.5f;
        public float MaxClimb = 6f;
        public float MaxDescent = 5f;
        public float VerticalResponse = 4f;
        public float YawRateDeg = 115f;
        public float YawResponse = 8f;
        public float Radius = 0.45f;
        /// <summary>Touching the ground faster than this is a crash.</summary>
        public float SafeTouchdownSpeed = 3.5f;
        /// <summary>Flight time on a full battery at hover effort.</summary>
        public float BatterySeconds = 480f;
        /// <summary>Distance from the operator at which the link is gone.</summary>
        public float SignalRange = 900f;

        // What sets one drone apart from another beyond raw speed.
        /// <summary>Highest camera magnification: 2, 4 or 8.</summary>
        public float MaxZoom = 4f;
        /// <summary>Multiplier on how far the camera can observe, photograph and mark.</summary>
        public float OpticsRange = 1f;
        /// <summary>Multiplier on damage taken; below 1 is a tougher airframe.</summary>
        public float DamageScale = 1f;
        /// <summary>Share of a cargo's handling penalty this drone feels.</summary>
        public float PayloadEase = 1f;
        /// <summary>Multiplier on how fast watchers notice the drone; below 1 is stealthier.</summary>
        public float DetectScale = 1f;
        /// <summary>Share of the wind the drone is pushed by.</summary>
        public float WindFactor = 1f;
        /// <summary>Bad weather does not drain this drone's battery faster.</summary>
        public bool WeatherProof;
        public bool WaterLanding;
        /// <summary>Fog and darkness do not shorten this drone's sight.</summary>
        public bool Thermal;
        /// <summary>Multiplier on the radius of radars; below 1 the drone jams them.</summary>
        public float JamScale = 1f;
        /// <summary>A winged drone cannot fly slower than this.</summary>
        public float MinSpeed;
        /// <summary>Metres added to the hit radius of a strike.</summary>
        public float StrikeBonus;
        /// <summary>How much of the wind speed this drone's munition picks up while falling.</summary>
        public float WindCarry = Ballistics.WindCarry;
        /// <summary>Multiplier on how fast scans, observations and marks fill up.</summary>
        public float ScanSpeed = 1f;

        // Weapons, carried in every combat mission. Landing on the pad reloads them.
        public int Rockets = 8;
        public int Bombs = 3;
        public float RocketDamage = 50f;
        public float BombDamage = 220f;
        /// <summary>How far away a target can be locked for a rocket.</summary>
        public float LockRange = 150f;
        /// <summary>Seconds between rockets.</summary>
        public float FireInterval = 0.7f;

        public static DroneSpec Sparrow() => new DroneSpec();

        /// <summary>
        /// More than zero makes this a one-way attack drone: it carries no weapons, it is the
        /// weapon, and this many of them stand ready for one sortie.
        /// </summary>
        public int OneWay;

        public DroneSpec Clone() => (DroneSpec)MemberwiseClone();

        /// <summary>
        /// The one-way strike drone flown in missions that are fought with them: light, quick,
        /// and able to dive, because it is not coming back.
        /// </summary>
        public DroneSpec AsStrikeDrone()
        {
            DroneSpec strike = Clone();
            strike.MaxSpeed *= 1.3f;
            strike.MaxDescent = Mathf.Max(MaxDescent, 14f);
            strike.TiltResponse *= 1.2f;
            strike.DamageScale *= 0.7f;
            return strike;
        }

        /// <summary>The same drone carrying cargo: slower, lazier to respond and weaker in the climb.</summary>
        /// <param name="payload">0 = empty, 1 = the heaviest load.</param>
        public DroneSpec WithPayload(float payload)
        {
            payload = Mathf.Clamp01(payload) * PayloadEase;
            DroneSpec loaded = Clone();
            loaded.MaxSpeed *= 1f - 0.30f * payload;
            loaded.TiltResponse *= 1f - 0.45f * payload;
            loaded.AssistGain *= 1f - 0.30f * payload;
            loaded.MaxClimb *= 1f - 0.35f * payload;
            loaded.YawRateDeg *= 1f - 0.20f * payload;
            return loaded;
        }
    }

    /// <summary>
    /// Flight dynamics of a multirotor, free of scene objects so it can be unit tested.
    /// Horizontal motion comes from tilting the thrust vector; linear drag is sized so that
    /// full tilt tops out exactly at <see cref="DroneSpec.MaxSpeed"/>.
    /// </summary>
    public static class FlightModel
    {
        public const float Gravity = 9.81f;

        public static void Step(ref FlightState s, FlightInput input, DroneSpec spec, FlightMode mode,
            Vector3 wind, float dt)
        {
            input.Move = Vector2.ClampMagnitude(input.Move, 1f);
            input.Throttle = Mathf.Clamp(input.Throttle, -1f, 1f);
            input.Yaw = Mathf.Clamp(input.Yaw, -1f, 1f);

            s.YawRateDeg = Approach(s.YawRateDeg, input.Yaw * spec.YawRateDeg, spec.YawResponse, dt);
            s.YawDeg = Mathf.Repeat(s.YawDeg + s.YawRateDeg * dt, 360f);

            float yaw = s.YawDeg * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(Mathf.Cos(yaw), 0f, -Mathf.Sin(yaw));

            float maxAccel = Gravity * Mathf.Tan(spec.MaxTiltDeg * Mathf.Deg2Rad);
            float drag = maxAccel / spec.MaxSpeed;

            wind *= spec.WindFactor;
            var airVelocity = new Vector3(s.Velocity.x - wind.x, 0f, s.Velocity.z - wind.z);
            var groundLocal = new Vector2(Vector3.Dot(s.Velocity, right), Vector3.Dot(s.Velocity, forward));
            var airLocal = new Vector2(Vector3.Dot(airVelocity, right), Vector3.Dot(airVelocity, forward));

            Vector2 targetTilt;
            if (mode == FlightMode.Assist)
            {
                // Acceleration needed to reach the wanted ground speed, plus what drag (and wind) takes away.
                Vector2 wanted = input.Move * spec.MaxSpeed;
                // A wing keeps flying forward whatever the stick says.
                if (spec.MinSpeed > 0f) wanted.y = Mathf.Max(wanted.y, spec.MinSpeed);
                Vector2 accel = (wanted - groundLocal) * spec.AssistGain + airLocal * drag;
                accel = Vector2.ClampMagnitude(accel, maxAccel);
                targetTilt = new Vector2(
                    Mathf.Atan(accel.x / Gravity) * Mathf.Rad2Deg,
                    Mathf.Atan(accel.y / Gravity) * Mathf.Rad2Deg);
            }
            else
            {
                targetTilt = input.Move * spec.MaxTiltDeg;
                if (spec.MinSpeed > 0f)
                    targetTilt.y = Mathf.Max(targetTilt.y, Mathf.Atan(drag * spec.MinSpeed / Gravity) * Mathf.Rad2Deg);
            }

            s.TiltDeg.x = Approach(s.TiltDeg.x, targetTilt.x, spec.TiltResponse, dt);
            s.TiltDeg.y = Approach(s.TiltDeg.y, targetTilt.y, spec.TiltResponse, dt);

            Vector3 thrust =
                right * (Gravity * Mathf.Tan(s.TiltDeg.x * Mathf.Deg2Rad)) +
                forward * (Gravity * Mathf.Tan(s.TiltDeg.y * Mathf.Deg2Rad));
            Vector3 horizontalAccel = thrust - airVelocity * drag;

            float targetVertical = input.Throttle >= 0f
                ? input.Throttle * spec.MaxClimb
                : input.Throttle * spec.MaxDescent;
            float vertical = Approach(s.Velocity.y, targetVertical, spec.VerticalResponse, dt);

            s.Velocity = new Vector3(
                s.Velocity.x + horizontalAccel.x * dt,
                vertical,
                s.Velocity.z + horizontalAccel.z * dt);
            s.Position += s.Velocity * dt;
        }

        /// <summary>Exponential approach; gives the same result for any step size.</summary>
        public static float Approach(float current, float target, float rate, float dt)
        {
            return target + (current - target) * Mathf.Exp(-rate * dt);
        }
    }
}
