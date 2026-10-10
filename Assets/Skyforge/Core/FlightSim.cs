using UnityEngine;

namespace Skyforge.Core
{
    public enum SimEvent
    {
        None,
        TookOff,
        Landed,
        Crashed,
    }

    /// <summary>The drone in a location: flight dynamics plus ground, water and obstacle contact.</summary>
    public sealed class FlightSim
    {
        public const float Ceiling = 400f;

        public FlightState State;
        public FlightMode Mode = FlightMode.Assist;
        public Vector3 Wind;
        public readonly DroneSpec Spec;
        readonly Island _island;

        /// <summary>Downward speed at the most recent landing or crash into the ground.</summary>
        public float LastTouchdownSpeed { get; private set; }

        public FlightSim(Island island, DroneSpec spec)
        {
            _island = island;
            Spec = spec;
            Reset();
        }

        /// <summary>Puts the drone back on the pad.</summary>
        public void Reset()
        {
            Vector3 spawn = _island.Spawn;
            State = new FlightState
            {
                Position = new Vector3(spawn.x, _island.Map.Height(spawn.x, spawn.z) + Spec.Radius, spawn.z),
                YawDeg = _island.SpawnYawDeg,
                Grounded = true,
            };
        }

        /// <summary>Height of the drone's underside above ground or water.</summary>
        public float AltitudeAboveGround
        {
            get
            {
                float ground = Mathf.Max(_island.Map.Height(State.Position.x, State.Position.z), Island.WaterLevel);
                return Mathf.Max(0f, State.Position.y - Spec.Radius - ground);
            }
        }

        /// <summary>Set while the automatic landing is flying the drone.</summary>
        public bool AutoLanding;

        /// <summary>Whether the landing assist is fitted; a strike drone has none, it is meant to dive in.</summary>
        public bool Cushion = true;
        /// <summary>Height above the ground at which the landing assist starts to hold the descent back.</summary>
        public const float CushionHeight = 7f;

        public SimEvent Step(FlightInput input, float dt)
        {
            var result = SimEvent.None;
            if (State.Grounded)
            {
                if (input.Throttle <= 0.1f)
                {
                    State.Velocity = Vector3.zero;
                    State.YawRateDeg = 0f;
                    State.TiltDeg.x = FlightModel.Approach(State.TiltDeg.x, 0f, Spec.TiltResponse, dt);
                    State.TiltDeg.y = FlightModel.Approach(State.TiltDeg.y, 0f, Spec.TiltResponse, dt);
                    return SimEvent.None;
                }
                State.Grounded = false;
                result = SimEvent.TookOff;
            }

            // An automatic landing is flown with every assist on, whatever mode the pilot prefers.
            FlightModel.Step(ref State, input, Spec, AutoLanding ? FlightMode.Assist : Mode, Wind, dt);
            if (Cushion || AutoLanding) ApplyCushion(dt);
            KeepInsideWorld();

            Vector3 position = State.Position;
            float terrain = _island.Map.Height(position.x, position.z);
            float underside = position.y - Spec.Radius;

            if (terrain < Island.WaterLevel)
            {
                if (underside <= Island.WaterLevel)
                {
                    float sideways = new Vector2(State.Velocity.x, State.Velocity.z).magnitude;
                    LastTouchdownSpeed = Mathf.Max(0f, -State.Velocity.y);
                    bool gentle = LastTouchdownSpeed <= Spec.SafeTouchdownSpeed && sideways <= Spec.SafeTouchdownSpeed * 2f;
                    if (!Spec.WaterLanding || !gentle) return SimEvent.Crashed;

                    // A drone with floats settles on the surface.
                    State.Position = new Vector3(position.x, Island.WaterLevel + Spec.Radius, position.z);
                    State.Velocity = Vector3.zero;
                    State.Grounded = true;
                    return SimEvent.Landed;
                }
            }
            else if (underside <= terrain)
            {
                float horizontal = new Vector2(State.Velocity.x, State.Velocity.z).magnitude;
                LastTouchdownSpeed = Mathf.Max(0f, -State.Velocity.y);
                bool hard = -State.Velocity.y > Spec.SafeTouchdownSpeed || horizontal > Spec.SafeTouchdownSpeed * 2f;
                if (hard) return SimEvent.Crashed;

                State.Position = new Vector3(position.x, terrain + Spec.Radius, position.z);
                State.Velocity = Vector3.zero;
                State.Grounded = true;
                return SimEvent.Landed;
            }

            if (_island.Obstacles.Hits(position, Spec.Radius)) return SimEvent.Crashed;
            return result;
        }

        /// <summary>
        /// The landing assist: close to the ground the drone will not sink faster than it can
        /// safely touch down, however hard the stick is held, and it stops sliding sideways in the
        /// last metre. It works in both flight modes: Sport takes away the help in the air, not the
        /// undercarriage. Over water only a drone with floats gets it.
        /// </summary>
        void ApplyCushion(float dt)
        {
            float terrain = _island.Map.Height(State.Position.x, State.Position.z);
            if (terrain < Island.WaterLevel && !Spec.WaterLanding) return;
            float height = State.Position.y - Spec.Radius - Mathf.Max(terrain, Island.WaterLevel);
            if (height > CushionHeight) return;

            float gentle = Mathf.Min(Spec.SafeTouchdownSpeed * 0.55f, MissionRun.GentleTouchdownSpeed * 0.8f);
            float allowed = Mathf.Lerp(gentle, Mathf.Max(gentle, Spec.MaxDescent), Mathf.Clamp01(height / CushionHeight));
            if (State.Velocity.y < -allowed) State.Velocity.y = -allowed;
            if (height < 1.2f && State.Velocity.y < 0f)
            {
                float keep = Mathf.Exp(-5f * dt);
                State.Velocity.x *= keep;
                State.Velocity.z *= keep;
            }
        }

        /// <summary>
        /// What an automatic landing would do with the sticks: slide over the point, then come
        /// straight down. The cushion makes the touchdown itself gentle.
        /// </summary>
        public static FlightInput LandingInput(FlightState state, Vector3 point)
        {
            Vector3 offset = point - state.Position;
            float yaw = state.YawDeg * Mathf.Deg2Rad;
            var forward = new Vector3(Mathf.Sin(yaw), 0f, Mathf.Cos(yaw));
            var right = new Vector3(forward.z, 0f, -forward.x);
            var want = new Vector2(Vector3.Dot(offset, right), Vector3.Dot(offset, forward)) * 0.16f;
            var speed = new Vector2(Vector3.Dot(state.Velocity, right), Vector3.Dot(state.Velocity, forward));
            Vector2 move = Vector2.ClampMagnitude(want - speed * 0.07f, 1f);
            float away = new Vector2(offset.x, offset.z).magnitude;
            // Down over the point, slower the nearer the ground; farther out it only loses a little height.
            float throttle = away < 3f ? -Mathf.Clamp(0.25f + Mathf.Max(0f, -offset.y) * 0.12f, 0.3f, 1f) : away < 10f ? -0.3f : 0f;
            return new FlightInput { Move = move, Throttle = throttle, Yaw = 0f };
        }

        void KeepInsideWorld()
        {
            float limit = _island.Map.HalfSize * 1.15f;
            Vector3 position = State.Position;
            var flat = new Vector2(position.x, position.z);
            if (flat.magnitude > limit)
            {
                Vector2 outward = flat.normalized;
                flat = outward * limit;
                var velocity = new Vector2(State.Velocity.x, State.Velocity.z);
                float leaving = Vector2.Dot(velocity, outward);
                if (leaving > 0f) velocity -= outward * leaving;
                State.Velocity = new Vector3(velocity.x, State.Velocity.y, velocity.y);
                position = new Vector3(flat.x, position.y, flat.y);
            }
            if (position.y > Ceiling)
            {
                position.y = Ceiling;
                State.Velocity = new Vector3(State.Velocity.x, Mathf.Min(State.Velocity.y, 0f), State.Velocity.z);
            }
            State.Position = position;
        }
    }
}
