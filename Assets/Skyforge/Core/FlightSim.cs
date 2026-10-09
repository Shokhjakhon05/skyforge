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

            FlightModel.Step(ref State, input, Spec, Mode, Wind, dt);
            KeepInsideWorld();

            Vector3 position = State.Position;
            float terrain = _island.Map.Height(position.x, position.z);
            float underside = position.y - Spec.Radius;

            if (terrain < Island.WaterLevel)
            {
                if (underside <= Island.WaterLevel) return SimEvent.Crashed;
            }
            else if (underside <= terrain)
            {
                float horizontal = new Vector2(State.Velocity.x, State.Velocity.z).magnitude;
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
