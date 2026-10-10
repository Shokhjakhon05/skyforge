using System.Collections.Generic;
using UnityEngine;

namespace Skyforge.Core
{
    public enum ObjectiveType
    {
        /// <summary>Get airborne.</summary>
        TakeOff,
        /// <summary>Fly through a point.</summary>
        Reach,
        /// <summary>Hold the drone near a point until the scan fills up.</summary>
        Scan,
        /// <summary>Keep a target in the camera's view, within range, for a while.</summary>
        Observe,
        /// <summary>Press the action button while the target is framed; zoom extends the range.</summary>
        Photo,
        /// <summary>Stay between a minimum and maximum distance from a moving target.</summary>
        Follow,
        /// <summary>Get close to a point that has no marker; a scanner reading leads the way.</summary>
        Find,
        /// <summary>Touch down within the radius of a point.</summary>
        Land,
        /// <summary>Release a munition so that it lands within the radius of the target.</summary>
        Strike,
        /// <summary>Observe every listed threat before the escorted actor reaches it.</summary>
        Mark,
        /// <summary>Wait for an actor to finish its route.</summary>
        Arrive,
        /// <summary>Destroy every listed target, with rockets, bombs or the drone itself.</summary>
        Destroy,
    }

    public sealed class ObjectiveDefinition
    {
        public ObjectiveType Type;
        public string TextKey;
        /// <summary>Where the objective is. For a moving target this is ignored in favour of <see cref="ActorId"/>.</summary>
        public Vector3 Position;
        /// <summary>The actor this objective is about, or null for a fixed position.</summary>
        public string ActorId;
        public float Radius;
        /// <summary>How far above or below <see cref="Position"/> still counts, for Scan.</summary>
        public float HeightWindow;
        public float HoldSeconds;
        /// <summary>Observe, Photo, Mark, Find: how far away the drone may be.</summary>
        public float Range;
        /// <summary>Follow: allowed band of distance to the target.</summary>
        public float MinDistance, MaxDistance;
        /// <summary>Strike: lowest height above ground from which the release is allowed.</summary>
        public float MinAltitude;
        public int Shots = 1;
        /// <summary>Mark: the threats, in route order.</summary>
        public List<Vector3> Targets = new List<Vector3>();
        /// <summary>Reach: draw the point as a gate to fly through.</summary>
        public bool Gate;
        /// <summary>Destroy: indices into the mission's targets.</summary>
        public List<int> TargetIndices = new List<int>();
    }

    public sealed class ActorDefinition
    {
        public string Id;
        public string Kind;
        public List<Vector3> Path = new List<Vector3>();
        public float Speed;
        /// <summary>The actor stands still until this objective is the current one.</summary>
        public int StartObjective;
        /// <summary>The drone is noticed when it comes closer than this; zero means the actor never notices.</summary>
        public float DetectRange;
    }

    /// <summary>A radar: it sees the drone inside its radius when the drone is higher than the floor above ground.</summary>
    public sealed class ZoneDefinition
    {
        public Vector3 Position;
        public float Radius;
        public float Floor;
    }

    public sealed class PropDefinition
    {
        public string Id;
        public string Kind;
        public Vector3 Position;
    }

    /// <summary>
    /// Everything that makes one mission: what to do, in which order, and what it pays.
    /// Missions are data; <see cref="MissionRun"/> is the only code that plays them.
    /// </summary>
    public sealed class MissionDefinition
    {
        public string Id;
        public string Region;
        public int Order;
        public string Template;
        public string TitleKey;
        public string BriefKey;
        public string WeatherId = WeatherCatalog.Clear;
        public List<ObjectiveDefinition> Objectives = new List<ObjectiveDefinition>();
        public List<ActorDefinition> Actors = new List<ActorDefinition>();
        public List<ZoneDefinition> Zones = new List<ZoneDefinition>();
        public List<PropDefinition> Props = new List<PropDefinition>();
        /// <summary>Everything that can be destroyed, hostile or friendly.</summary>
        public List<TargetDefinition> Targets = new List<TargetDefinition>();
        /// <summary>Above zero the mission is flown with this many strike drones, each of which is its own munition.</summary>
        public int StrikeDrones;
        /// <summary>Finishing faster than this earns the second star.</summary>
        public float TimeGoalSeconds;
        public int BaseReward;
        public int StarReward;
        /// <summary>Charge at the start, 0..1.</summary>
        public float BatteryStart = 1f;
        /// <summary>0 = empty, 1 = the heaviest cargo; makes the drone slower and lazier.</summary>
        public float Payload;
        /// <summary>Where the operator is; signal strength is measured from here.</summary>
        public Vector3 BasePosition;

        /// <summary>A copy whose lists can be changed without touching this definition.</summary>
        public MissionDefinition Copy()
        {
            var copy = (MissionDefinition)MemberwiseClone();
            copy.Objectives = new List<ObjectiveDefinition>(Objectives);
            copy.Actors = new List<ActorDefinition>(Actors);
            copy.Zones = new List<ZoneDefinition>(Zones);
            copy.Props = new List<PropDefinition>(Props);
            copy.Targets = new List<TargetDefinition>(Targets);
            return copy;
        }

        public bool HasDetectors
        {
            get
            {
                if (Zones.Count > 0) return true;
                foreach (ActorDefinition actor in Actors)
                    if (actor.DetectRange > 0f) return true;
                return false;
            }
        }
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
        Detected,
        Missed,
        Ambushed,
        ShotDown,
        /// <summary>Something the player was protecting has been destroyed.</summary>
        AllyLost,
        /// <summary>A target reached the end of its road alive.</summary>
        Escaped,
        OutOfDrones,
    }

    /// <summary>What the player did with the action button on a step.</summary>
    public enum ActionResult
    {
        None,
        PhotoTaken,
        /// <summary>The target was not framed or was too far.</summary>
        PhotoMissed,
        Released,
        /// <summary>Too low to release.</summary>
        ReleaseRefused,
        RocketFired,
        BombDropped,
        /// <summary>Nothing is in the sights.</summary>
        NoLock,
        NoAmmo,
    }

    /// <summary>Where a moving actor is right now.</summary>
    public struct ActorState
    {
        public Vector3 Position;
        public Vector3 Forward;
        public float Travelled;
        public bool Arrived;
    }

    /// <summary>A munition on its way down.</summary>
    public struct Shot
    {
        public bool Active;
        public Vector3 From;
        public Vector3 Impact;
        public float Duration;
        public float Elapsed;
    }

    /// <summary>
    /// One attempt at a mission. Fed with the drone's state every simulation step; tracks the
    /// battery, signal, hull and detection gauges, moves the actors, walks through the objectives
    /// and decides the outcome.
    /// </summary>
    public sealed partial class MissionRun
    {
        public const float LowBattery = 0.20f;
        public const float LowSignal = 0.25f;
        /// <summary>Seconds without any signal before the drone is lost.</summary>
        public const float SignalGraceSeconds = 5f;
        /// <summary>Touchdowns slower than this do no damage.</summary>
        public const float GentleTouchdownSpeed = 2f;
        /// <summary>Half-angle of the camera's view used for Observe, Photo and Mark.</summary>
        public const float FrameHalfAngleDeg = 26f;
        /// <summary>Seconds of being seen before the mission is blown, and seconds to calm down again.</summary>
        public const float DetectSeconds = 2.5f, ForgetSeconds = 6f;
        /// <summary>A convoy this close to an unmarked threat is ambushed.</summary>
        public const float AmbushDistance = 25f;

        public readonly MissionDefinition Definition;
        readonly DroneSpec _spec;
        readonly Weather _weather;
        readonly HeightMap _map;
        readonly ActorState[] _actors;
        float _signalLostFor;
        bool[] _marked = new bool[0];
        float[] _markProgress = new float[0];
        Shot _shot;
        Vector3 _lastWind;

        public MissionStatus Status { get; private set; }
        public FailReason FailReason { get; private set; }
        public int ObjectiveIndex { get; private set; }
        /// <summary>0..1 progress of the current objective, for objectives that fill up.</summary>
        public float ObjectiveProgress { get; private set; }
        public float Elapsed { get; private set; }
        public float Battery { get; private set; }
        public float Signal { get; private set; } = 1f;
        public float Hull { get; private set; } = 1f;
        /// <summary>0 unseen .. 1 mission blown.</summary>
        public float Detection { get; private set; }
        /// <summary>True on the steps when something is watching the drone.</summary>
        public bool BeingDetected { get; private set; }
        /// <summary>True while the current Observe, Photo or Mark target is framed and in range.</summary>
        public bool TargetFramed { get; private set; }
        public int ShotsLeft { get; private set; }
        public Shot Shot => _shot;
        /// <summary>Set on the step a munition lands; where it landed and whether it hit.</summary>
        public bool ShotLanded { get; private set; }
        public bool ShotHit { get; private set; }

        public bool Damaged => Hull < 1f;
        public float SignalCountdown => Mathf.Max(0f, SignalGraceSeconds - _signalLostFor);

        public ObjectiveDefinition CurrentObjective =>
            ObjectiveIndex < Definition.Objectives.Count ? Definition.Objectives[ObjectiveIndex] : null;

        public MissionRun(MissionDefinition definition, DroneSpec spec, HeightMap map = null)
        {
            Definition = definition;
            _spec = spec;
            _map = map;
            _weather = WeatherCatalog.Get(definition.WeatherId);
            Battery = Mathf.Clamp01(definition.BatteryStart);

            _actors = new ActorState[definition.Actors.Count];
            for (int i = 0; i < _actors.Length; i++) _actors[i] = ActorAt(definition.Actors[i], 0f);
            StartCombat();
            EnterObjective();
        }

        public ActorState Actor(int index) => _actors[index];

        public bool TryGetActor(string id, out ActorState state)
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                if (Definition.Actors[i].Id != id) continue;
                state = _actors[i];
                return true;
            }
            state = default;
            return false;
        }

        /// <summary>Where an objective currently is; moving targets are followed.</summary>
        public Vector3 TargetPosition(ObjectiveDefinition objective)
        {
            if (!string.IsNullOrEmpty(objective.ActorId) && TryGetActor(objective.ActorId, out ActorState actor))
                return actor.Position + Vector3.up * 2f;
            if (objective.Type == ObjectiveType.Destroy)
            {
                int nearest = NearestTarget(objective, _dronePosition);
                return nearest >= 0 ? _targets[nearest].Position + Vector3.up * 2f : _dronePosition;
            }
            return objective.Position;
        }

        public bool IsMarked(int threat) => threat < _marked.Length && _marked[threat];

        /// <summary>Find: 0 far away .. 1 on top of it.</summary>
        public float ScannerReading(Vector3 dronePosition)
        {
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null || objective.Type != ObjectiveType.Find || objective.Range <= 0f) return 0f;
            return Mathf.Clamp01(1f - Vector3.Distance(dronePosition, objective.Position) / (objective.Range * _spec.ScanSpeed));
        }

        /// <summary>Where a munition released now would land.</summary>
        public Vector3 PredictImpact(FlightState state, out float seconds)
        {
            return Ballistics.Impact(state.Position, state.Velocity, _lastWind, _map, out seconds, _spec.WindCarry);
        }

        /// <summary>How far the camera reaches for an objective: optics, zoom, and weather unless the sensor is thermal.</summary>
        public float SightRange(ObjectiveDefinition objective, float zoom)
        {
            float visibility = _spec.Thermal ? 1f : _weather.Visibility;
            return objective.Range * zoom * _spec.OpticsRange * visibility;
        }

        /// <param name="load">Motor effort 0..1, as shown by the drone.</param>
        /// <param name="touchdownSpeed">Vertical speed of the touchdown when <paramref name="simEvent"/> is Landed.</param>
        /// <param name="action">True on the step the action button was pressed.</param>
        /// <param name="zoom">Camera magnification; it stretches the Photo and Observe range.</param>
        /// <param name="secondary">True on the step the second weapon button was pressed: bombs.</param>
        public ActionResult Step(FlightState state, SimEvent simEvent, float load, float touchdownSpeed, float dt,
            bool action = false, Vector3 wind = default, float zoom = 1f, bool secondary = false)
        {
            ShotLanded = false;
            _events.Clear();
            if (Status != MissionStatus.InProgress || NeedsRespawn) return ActionResult.None;
            _lastWind = wind;

            if (simEvent == SimEvent.Crashed)
            {
                if (Kamikaze)
                {
                    // Flying a strike drone into something is what it is for.
                    DroneLost(state.Position, true);
                    return ActionResult.None;
                }
                Hull = 0f;
                Fail(FailReason.Destroyed);
                return ActionResult.None;
            }
            if (simEvent == SimEvent.Landed && touchdownSpeed > GentleTouchdownSpeed)
            {
                float severity = Mathf.InverseLerp(GentleTouchdownSpeed, _spec.SafeTouchdownSpeed, touchdownSpeed);
                Hull = Mathf.Max(0.05f, Hull - (0.1f + 0.3f * severity) * _spec.DamageScale);
            }
            if (simEvent == SimEvent.Landed && IsCombat && !Kamikaze &&
                HorizontalDistance(state.Position, Definition.BasePosition) <= RearmRadius &&
                (Rockets < _spec.Rockets || Bombs < _spec.Bombs))
            {
                Resupply();
                _events.Add(new CombatEvent { Type = CombatEventType.Rearmed, Position = state.Position, Target = -1 });
            }

            // The clock starts with the first take-off, so reading the HUD on the pad costs nothing.
            bool started = ObjectiveIndex > 0 || !state.Grounded;
            if (started) Elapsed += dt;

            if (!state.Grounded)
            {
                float weatherDrain = _spec.WeatherProof ? 1f : _weather.BatteryDrain;
                float drain = (0.6f + 0.8f * Mathf.Clamp01(load)) * weatherDrain;
                Battery = Mathf.Max(0f, Battery - dt / _spec.BatterySeconds * drain);
                if (Battery <= 0f)
                {
                    Fail(FailReason.BatteryEmpty);
                    return ActionResult.None;
                }
            }

            float distance = Vector3.Distance(state.Position, Definition.BasePosition);
            float range = _spec.SignalRange * _weather.SignalRange;
            Signal = Mathf.Clamp01((range - distance) / (range * 0.4f));
            _signalLostFor = Signal <= 0f ? _signalLostFor + dt : 0f;
            if (_signalLostFor >= SignalGraceSeconds)
            {
                Fail(FailReason.SignalLost);
                return ActionResult.None;
            }

            MoveActors(dt);
            if (!UpdateCombat(state, dt)) return ActionResult.None;
            if (NeedsRespawn) return ActionResult.None;
            if (!UpdateDetection(state, dt)) return ActionResult.None;
            if (!CheckAmbush()) return ActionResult.None;
            if (!UpdateShot(dt)) return ActionResult.None;

            return AdvanceObjective(state, dt, action, zoom, secondary);
        }

        // ---------------------------------------------------------------- actors and detection

        void MoveActors(float dt)
        {
            for (int i = 0; i < _actors.Length; i++)
            {
                ActorDefinition definition = Definition.Actors[i];
                if (ObjectiveIndex < definition.StartObjective || _actors[i].Arrived) continue;
                _actors[i] = ActorAt(definition, _actors[i].Travelled + definition.Speed * dt);
            }
        }

        ActorState ActorAt(ActorDefinition definition, float travelled) => AlongPath(definition.Path, travelled);

        /// <summary>Where something that has gone a distance along a path is, on the ground.</summary>
        ActorState AlongPath(List<Vector3> path, float travelled)
        {
            var state = new ActorState { Travelled = travelled, Forward = Vector3.forward };
            if (path.Count == 0) return state;

            float left = travelled;
            Vector3 position = path[0];
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector3 leg = path[i + 1] - path[i];
                leg.y = 0f;
                float length = leg.magnitude;
                if (length > 0.01f) state.Forward = leg / length;
                if (left <= length)
                {
                    position = Vector3.Lerp(path[i], path[i + 1], length > 0.01f ? left / length : 0f);
                    state.Position = OnGround(position);
                    return state;
                }
                left -= length;
                position = path[i + 1];
            }
            state.Position = OnGround(position);
            state.Arrived = path.Count > 1;
            return state;
        }

        Vector3 OnGround(Vector3 position)
        {
            if (_map != null) position.y = Mathf.Max(_map.Height(position.x, position.z), Island.WaterLevel);
            return position;
        }

        float HeightAboveGround(Vector3 position)
        {
            if (_map == null) return position.y;
            return position.y - Mathf.Max(_map.Height(position.x, position.z), Island.WaterLevel);
        }

        /// <summary>Returns false when the drone has been seen for too long and the mission is over.</summary>
        bool UpdateDetection(FlightState state, float dt)
        {
            bool seen = false;
            foreach (ZoneDefinition zone in Definition.Zones)
            {
                if (HorizontalDistance(state.Position, zone.Position) > zone.Radius * _spec.JamScale) continue;
                if (HeightAboveGround(state.Position) > zone.Floor) seen = true;
            }
            for (int i = 0; i < _actors.Length && !seen; i++)
            {
                float notice = Definition.Actors[i].DetectRange;
                if (notice > 0f && Vector3.Distance(state.Position, _actors[i].Position) < notice) seen = true;
            }

            BeingDetected = seen && !state.Grounded;
            Detection = BeingDetected
                ? Mathf.Min(1f, Detection + dt / DetectSeconds * _spec.DetectScale)
                : Mathf.Max(0f, Detection - dt / ForgetSeconds);
            if (Detection < 1f) return true;
            Fail(FailReason.Detected);
            return false;
        }

        /// <summary>Returns false when an escorted actor has run into a threat nobody marked.</summary>
        bool CheckAmbush()
        {
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null || objective.Type != ObjectiveType.Mark) return true;
            if (!TryGetActor(objective.ActorId, out ActorState convoy)) return true;
            for (int i = 0; i < objective.Targets.Count; i++)
            {
                if (_marked[i]) continue;
                if (HorizontalDistance(convoy.Position, objective.Targets[i]) > AmbushDistance) continue;
                Fail(FailReason.Ambushed);
                return false;
            }
            return true;
        }

        // ---------------------------------------------------------------- strike

        /// <summary>Returns false when the last munition missed and the mission is over.</summary>
        bool UpdateShot(float dt)
        {
            if (!_shot.Active) return true;
            _shot.Elapsed += dt;
            if (_shot.Elapsed < _shot.Duration) return true;

            _shot.Active = false;
            ShotLanded = true;
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null || objective.Type != ObjectiveType.Strike) return true;

            ShotHit = HorizontalDistance(_shot.Impact, TargetPosition(objective)) <= objective.Radius + _spec.StrikeBonus;
            if (ShotHit)
            {
                CompleteObjective();
                return true;
            }
            if (ShotsLeft > 0) return true;
            Fail(FailReason.Missed);
            return false;
        }

        // ---------------------------------------------------------------- objectives

        void EnterObjective()
        {
            ObjectiveProgress = 0f;
            TargetFramed = false;
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null) return;
            if (objective.Type == ObjectiveType.Strike) ShotsLeft = Mathf.Max(1, objective.Shots);
            if (objective.Type == ObjectiveType.Mark)
            {
                _marked = new bool[objective.Targets.Count];
                _markProgress = new float[objective.Targets.Count];
            }
        }

        void CompleteObjective()
        {
            ObjectiveIndex++;
            if (ObjectiveIndex >= Definition.Objectives.Count) Status = MissionStatus.Succeeded;
            EnterObjective();
        }

        ActionResult AdvanceObjective(FlightState state, float dt, bool action, float zoom, bool secondary)
        {
            ObjectiveDefinition objective = CurrentObjective;
            if (objective == null) return ActionResult.None;

            var result = ActionResult.None;
            // Weapons are live for the whole of a combat mission, except while the button means something else.
            if (objective.Type != ObjectiveType.Photo && objective.Type != ObjectiveType.Strike && (action || secondary))
                result = FireWeapons(state, action, secondary);
            bool done = false;
            Vector3 target = TargetPosition(objective);
            TargetFramed = false;

            switch (objective.Type)
            {
                case ObjectiveType.TakeOff:
                    done = !state.Grounded;
                    break;

                case ObjectiveType.Reach:
                case ObjectiveType.Find:
                    done = Vector3.Distance(state.Position, target) <= objective.Radius;
                    break;


                case ObjectiveType.Scan:
                    if (IsInside(state.Position, objective))
                        ObjectiveProgress = Mathf.Min(1f, ObjectiveProgress + dt / objective.HoldSeconds * _spec.ScanSpeed);
                    else
                        ObjectiveProgress = Mathf.Max(0f, ObjectiveProgress - dt / objective.HoldSeconds * 0.5f);
                    done = ObjectiveProgress >= 1f;
                    break;

                case ObjectiveType.Observe:
                    TargetFramed = IsFramed(state, target, SightRange(objective, zoom));
                    if (TargetFramed)
                        ObjectiveProgress = Mathf.Min(1f, ObjectiveProgress + dt / objective.HoldSeconds * _spec.ScanSpeed);
                    else
                        ObjectiveProgress = Mathf.Max(0f, ObjectiveProgress - dt / objective.HoldSeconds * 0.5f);
                    done = ObjectiveProgress >= 1f;
                    break;

                case ObjectiveType.Photo:
                    TargetFramed = IsFramed(state, target, SightRange(objective, zoom));
                    if (action)
                    {
                        result = TargetFramed ? ActionResult.PhotoTaken : ActionResult.PhotoMissed;
                        done = TargetFramed;
                    }
                    break;

                case ObjectiveType.Follow:
                    float gap = HorizontalDistance(state.Position, target);
                    TargetFramed = gap >= objective.MinDistance && gap <= objective.MaxDistance && !state.Grounded;
                    if (TargetFramed) ObjectiveProgress = Mathf.Min(1f, ObjectiveProgress + dt / objective.HoldSeconds);
                    done = ObjectiveProgress >= 1f;
                    break;

                case ObjectiveType.Land:
                    done = state.Grounded && HorizontalDistance(state.Position, target) <= objective.Radius;
                    break;

                case ObjectiveType.Strike:
                    if (action && !_shot.Active && ShotsLeft > 0)
                    {
                        if (HeightAboveGround(state.Position) < objective.MinAltitude)
                        {
                            result = ActionResult.ReleaseRefused;
                        }
                        else
                        {
                            Vector3 impact = Ballistics.Impact(state.Position, state.Velocity, _lastWind, _map, out float fall, _spec.WindCarry);
                            _shot = new Shot { Active = true, From = state.Position, Impact = impact, Duration = fall };
                            ShotsLeft--;
                            result = ActionResult.Released;
                        }
                    }
                    break;

                case ObjectiveType.Mark:
                    int remaining = 0;
                    float best = 0f;
                    for (int i = 0; i < objective.Targets.Count; i++)
                    {
                        if (_marked[i]) continue;
                        remaining++;
                        if (IsFramed(state, objective.Targets[i], SightRange(objective, zoom)))
                        {
                            TargetFramed = true;
                            _markProgress[i] += dt / objective.HoldSeconds * _spec.ScanSpeed;
                            if (_markProgress[i] >= 1f)
                            {
                                _marked[i] = true;
                                remaining--;
                            }
                        }
                        best = Mathf.Max(best, Mathf.Min(1f, _markProgress[i]));
                    }
                    ObjectiveProgress = best;
                    done = remaining == 0;
                    break;

                case ObjectiveType.Arrive:
                    done = TryGetActor(objective.ActorId, out ActorState arriving) && arriving.Arrived;
                    break;

                case ObjectiveType.Destroy:
                    int standing = Remaining(objective);
                    ObjectiveProgress = objective.TargetIndices.Count > 0
                        ? 1f - standing / (float)objective.TargetIndices.Count
                        : 1f;
                    done = standing == 0;
                    break;
            }

            if (done) CompleteObjective();
            return result;
        }

        /// <summary>Number of threats of the current Mark objective that are still unmarked.</summary>
        public int ThreatsLeft
        {
            get
            {
                int left = 0;
                foreach (bool marked in _marked)
                    if (!marked) left++;
                return left;
            }
        }

        /// <summary>
        /// The drone's camera looks along its heading. A target is framed when it is within range,
        /// close enough to that heading, and no hill stands in between.
        /// </summary>
        public bool IsFramed(FlightState state, Vector3 target, float range)
        {
            Vector3 offset = target - state.Position;
            if (offset.magnitude > range) return false;

            var flat = new Vector2(offset.x, offset.z);
            if (flat.sqrMagnitude > 1f)
            {
                float yaw = state.YawDeg * Mathf.Deg2Rad;
                var heading = new Vector2(Mathf.Sin(yaw), Mathf.Cos(yaw));
                if (Vector2.Angle(heading, flat) > FrameHalfAngleDeg) return false;
            }
            return HasLineOfSight(state.Position, target);
        }

        bool HasLineOfSight(Vector3 from, Vector3 to)
        {
            if (_map == null) return true;
            const int samples = 12;
            for (int i = 1; i < samples; i++)
            {
                Vector3 point = Vector3.Lerp(from, to, i / (float)samples);
                if (_map.Height(point.x, point.z) > point.y + 0.5f) return false;
            }
            return true;
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

    /// <summary>Flight of an unpowered munition dropped from the drone.</summary>
    public static class Ballistics
    {
        /// <summary>How much of the wind speed the falling munition picks up.</summary>
        public const float WindCarry = 0.5f;

        public static Vector3 Impact(Vector3 from, Vector3 velocity, Vector3 wind, HeightMap map, out float seconds,
            float windCarry = WindCarry)
        {
            // The ground under the landing point is not known until the drift is, so settle it in a few rounds.
            float ground = GroundAt(map, from.x, from.z);
            Vector3 impact = from;
            seconds = 0f;
            for (int i = 0; i < 4; i++)
            {
                float height = Mathf.Max(0f, from.y - ground);
                seconds = Mathf.Sqrt(2f * height / FlightModel.Gravity);
                impact = new Vector3(
                    from.x + (velocity.x + wind.x * windCarry) * seconds,
                    ground,
                    from.z + (velocity.z + wind.z * windCarry) * seconds);
                ground = GroundAt(map, impact.x, impact.z);
            }
            impact.y = ground;
            return impact;
        }

        static float GroundAt(HeightMap map, float x, float z)
        {
            return map == null ? 0f : Mathf.Max(map.Height(x, z), Island.WaterLevel);
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
        public int PartsGranted;
        public bool Granted;
        /// <summary>The reward has been doubled by a watched ad; it cannot be doubled again.</summary>
        public bool Doubled;

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
            outcome.NoDamage = run.CleanFinish;
            outcome.Stars = 1 + (outcome.TimeGoalMet ? 1 : 0) + (outcome.NoDamage ? 1 : 0);
            return outcome;
        }
    }
}
