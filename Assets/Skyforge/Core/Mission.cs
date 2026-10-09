using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public enum ObjectiveType
    {
        /// <summary>Get airborne.</summary>
        TakeOff,
        /// <summary>Hold the drone near a point until the scan fills up.</summary>
        Scan,
        /// <summary>Touch down within the radius of a point.</summary>
        Land,
    }

    public sealed class ObjectiveDefinition
    {
        public ObjectiveType Type;
        public string TextKey;
        public Vector3 Position;
        public float Radius;
        /// <summary>How far above or below <see cref="Position"/> still counts, for Scan.</summary>
        public float HeightWindow;
        public float HoldSeconds;
    }

    /// <summary>
    /// Everything that makes one mission: what to do, in which order, and what it pays.
    /// Missions are data; <see cref="MissionRun"/> is the only code that plays them.
    /// </summary>
    public sealed class MissionDefinition
    {
        public string Id;
        public string TitleKey;
        public string BriefKey;
        public List<ObjectiveDefinition> Objectives = new List<ObjectiveDefinition>();
        /// <summary>Finishing faster than this earns the second star.</summary>
        public float TimeGoalSeconds;
        public int BaseReward;
        public int StarReward;
        /// <summary>Where the operator is; signal strength is measured from here.</summary>
        public Vector3 BasePosition;
    }

    public enum MissionStatus
    {
        InProgress,
        Succeeded,
        Failed,
    }

    public enum FailReason
    {
        None,
        Destroyed,
        BatteryEmpty,
        SignalLost,
    }

    /// <summary>
    /// One attempt at a mission. Fed with the drone's state every simulation step; tracks the
    /// battery, signal and hull gauges, walks through the objectives and decides the outcome.
    /// </summary>
    public sealed class MissionRun
    {
        public const float LowBattery = 0.20f;
        public const float LowSignal = 0.25f;
        /// <summary>Seconds without any signal before the drone is lost.</summary>
        public const float SignalGraceSeconds = 5f;
        /// <summary>Touchdowns slower than this do no damage.</summary>
        public const float GentleTouchdownSpeed = 2f;

        public readonly MissionDefinition Definition;
        readonly DroneSpec _spec;
        float _signalLostFor;

        public MissionStatus Status { get; private set; }
        public FailReason FailReason { get; private set; }
        public int ObjectiveIndex { get; private set; }
        /// <summary>0..1 progress of the current objective, for objectives that fill up.</summary>
        public float ObjectiveProgress { get; private set; }
        public float Elapsed { get; private set; }
        public float Battery { get; private set; } = 1f;
        public float Signal { get; private set; } = 1f;
        public float Hull { get; private set; } = 1f;

        public bool Damaged => Hull < 1f;
        public float SignalCountdown => Mathf.Max(0f, SignalGraceSeconds - _signalLostFor);

        public ObjectiveDefinition CurrentObjective =>
            ObjectiveIndex < Definition.Objectives.Count ? Definition.Objectives[ObjectiveIndex] : null;

        public MissionRun(MissionDefinition definition, DroneSpec spec)
        {
            Definition = definition;
            _spec = spec;
        }

        /// <param name="load">Motor effort 0..1, as shown by the drone.</param>
        /// <param name="touchdownSpeed">Vertical speed of the touchdown when <paramref name="simEvent"/> is Landed.</param>
        public void Step(FlightState state, SimEvent simEvent, float load, float touchdownSpeed, float dt)
        {
            if (Status != MissionStatus.InProgress) return;

            if (simEvent == SimEvent.Crashed)
            {
                Hull = 0f;
                Fail(FailReason.Destroyed);
                return;
            }
            if (simEvent == SimEvent.Landed && touchdownSpeed > GentleTouchdownSpeed)
            {
                float severity = Mathf.InverseLerp(GentleTouchdownSpeed, _spec.SafeTouchdownSpeed, touchdownSpeed);
                Hull = Mathf.Max(0.05f, Hull - 0.1f - 0.3f * severity);
            }

            // The clock starts with the first take-off, so reading the HUD on the pad costs nothing.
            bool started = ObjectiveIndex > 0 || !state.Grounded;
            if (started) Elapsed += dt;

            if (!state.Grounded)
            {
                Battery = Mathf.Max(0f, Battery - dt / _spec.BatterySeconds * (0.6f + 0.8f * Mathf.Clamp01(load)));
                if (Battery <= 0f)
                {
                    Fail(FailReason.BatteryEmpty);
                    return;
                }
            }

            float distance = Vector3.Distance(state.Position, Definition.BasePosition);
            Signal = Mathf.Clamp01((_spec.SignalRange - distance) / (_spec.SignalRange * 0.4f));
            _signalLostFor = Signal <= 0f ? _signalLostFor + dt : 0f;
            if (_signalLostFor >= SignalGraceSeconds)
            {
                Fail(FailReason.SignalLost);
                return;
            }

            AdvanceObjective(state, dt);
        }

        void AdvanceObjective(FlightState state, float dt)
        {
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null) return;

            bool done = false;
            switch (objective.Type)
            {
                case ObjectiveType.TakeOff:
                    done = !state.Grounded;
                    break;
                case ObjectiveType.Scan:
                    if (IsInside(state.Position, objective))
                        ObjectiveProgress = Mathf.Min(1f, ObjectiveProgress + dt / objective.HoldSeconds);
                    else
                        ObjectiveProgress = Mathf.Max(0f, ObjectiveProgress - dt / objective.HoldSeconds * 0.5f);
                    done = ObjectiveProgress >= 1f;
                    break;
                case ObjectiveType.Land:
                    done = state.Grounded && HorizontalDistance(state.Position, objective.Position) <= objective.Radius;
                    break;
            }
            if (!done) return;

            ObjectiveIndex++;
            ObjectiveProgress = 0f;
            if (ObjectiveIndex >= Definition.Objectives.Count) Status = MissionStatus.Succeeded;
        }

        public static bool IsInside(Vector3 position, ObjectiveDefinition objective)
        {
            return HorizontalDistance(position, objective.Position) <= objective.Radius &&
                   Mathf.Abs(position.y - objective.Position.y) <= objective.HeightWindow;
        }

        static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x, dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        void Fail(FailReason reason)
        {
            Status = MissionStatus.Failed;
            FailReason = reason;
        }
    }

    /// <summary>What a finished attempt earned. Same inputs always give the same stars.</summary>
    public sealed class MissionOutcome
    {
        public bool Succeeded;
        public FailReason FailReason;
        public float Time;
        public bool TimeGoalMet;
        public bool NoDamage;
        /// <summary>0 on failure; otherwise 1 for finishing, +1 for the time goal, +1 for no damage.</summary>
        public int Stars;
        /// <summary>Filled in by <see cref="Progress.Grant"/>.</summary>
        public int CreditsGranted;
        public bool Granted;

        public static MissionOutcome From(MissionRun run)
        {
            var outcome = new MissionOutcome
            {
                Succeeded = run.Status == MissionStatus.Succeeded,
                FailReason = run.FailReason,
                Time = run.Elapsed,
            };
            if (!outcome.Succeeded) return outcome;

            outcome.TimeGoalMet = run.Elapsed <= run.Definition.TimeGoalSeconds;
            outcome.NoDamage = !run.Damaged;
            outcome.Stars = 1 + (outcome.TimeGoalMet ? 1 : 0) + (outcome.NoDamage ? 1 : 0);
            return outcome;
        }
    }

    /// <summary>The missions that exist so far. Stage 4 moves these into data files.</summary>
    public static class MissionCatalog
    {
        public const string FirstContactId = "first_contact";

        /// <summary>Tutorial mission: scan the three weather beacons and come back.</summary>
        public static MissionDefinition FirstContact(Island island)
        {
            var mission = new MissionDefinition
            {
                Id = FirstContactId,
                TitleKey = "mission.first_contact.title",
                BriefKey = "mission.first_contact.brief",
                TimeGoalSeconds = 180f,
                BaseReward = 150,
                StarReward = 50,
                BasePosition = island.Spawn,
            };
            mission.Objectives.Add(new ObjectiveDefinition { Type = ObjectiveType.TakeOff, TextKey = "objective.takeoff" });
            foreach (Vector3 beacon in island.Beacons)
            {
                mission.Objectives.Add(new ObjectiveDefinition
                {
                    Type = ObjectiveType.Scan,
                    TextKey = "objective.scan",
                    Position = beacon + Vector3.up * (Island.BeaconHeight + 6f),
                    Radius = 12f,
                    HeightWindow = 14f,
                    HoldSeconds = 3f,
                });
            }
            mission.Objectives.Add(new ObjectiveDefinition
            {
                Type = ObjectiveType.Land,
                TextKey = "objective.land",
                Position = island.Spawn,
                Radius = 8f,
            });
            return mission;
        }
    }
}
