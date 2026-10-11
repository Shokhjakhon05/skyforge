using System.Collections.Generic;
using Skyforge.Core;
using Skyforge.UI;
using Skyforge.World;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// The field side of the game: the free sight and what it aims at, loot and chains of kills,
    /// the vehicles that drive, the sniper's rifle, and the instructor who talks a new player
    /// through the first flights.
    /// </summary>
    public sealed partial class Game
    {
        FieldHud _field;
        PickupsView _pickups;
        float _sniperYaw, _sniperPitch, _dustClock;
        int _shownScopeStamp = -1;
        bool _firstBlood;

        // The instructor.
        bool _lessonsAsked;
        string _lesson;
        int _lessonStep;
        float _lessonClock, _lessonYaw;
        Vector3 _lessonFrom;

        void BuildField()
        {
            _field = new FieldHud(transform);
            _field.Visible = false;
        }

        /// <summary>The aircraft or vehicle a mission dictates, or null when the player's choice flies.</summary>
        DroneDefinition FieldDrone()
        {
            if (_mission == null || _phase != Phase.Mission && _launching == null) return null;
            string mode = (_launching ?? _mission).Mode;
            return mode == "armour" ? Garage.Vehicle(_save) : mode == "sniper" ? DroneCatalog.Overwatch : null;
        }

        MissionDefinition _launching;

        bool SniperMode => _phase == Phase.Mission && _run != null && _run.Sniper;
        bool Driving => _sim != null && _sim.Spec.Ground;

        Vector3 SniperAim => Quaternion.Euler(_sniperPitch, _sniperYaw, 0f) * Vector3.forward;

        /// <summary>One step of whatever is being flown, driven or simply held in place.</summary>
        SimEvent StepAircraft(FlightInput input)
        {
            if (!SniperMode) return _sim.Step(input, StepSeconds);
            // The sniper's aircraft hangs on its perch: nothing moves but the rifle.
            _sim.State = new FlightState { Position = _mission.Perch, YawDeg = _sniperYaw };
            return SimEvent.None;
        }

        /// <summary>The sticks move the rifle, finer the higher the magnification.</summary>
        void SniperInput()
        {
            Vector2 stick = _sticks.Left.Value + _sticks.Right.Value;
            float rate = 34f / Mathf.Max(1f, _zoom) * Time.unscaledDeltaTime;
            _sniperYaw += stick.x * Mathf.Abs(stick.x) * rate;
            _sniperPitch = Mathf.Clamp(_sniperPitch - stick.y * Mathf.Abs(stick.y) * rate, -5f, 60f);
            if (!_actionPressed) return;
            _actionPressed = false;
            if (!_run.SniperFire(_mission.Perch, SniperAim)) _sfx.Play(Sound.Warning, 0.5f);
        }

        /// <summary>Called when a mission has been put together and is about to begin.</summary>
        void FieldLaunched()
        {
            _field.Reset();
            _pickups?.Clear();
            _firstBlood = false;
            _shownScopeStamp = -1;
            if (_run.Sniper)
            {
                _sim.State = new FlightState { Position = _mission.Perch, YawDeg = _mission.PerchYawDeg };
                _previous = _sim.State;
                _hasFlown = true;
                _sniperYaw = _mission.PerchYawDeg;
                // Start with the square in the glass.
                Vector3 to = _introPointOrCentre() - _mission.Perch;
                _sniperPitch = Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
                _zoom = 2f;
                _camera.Zoom = 1f;
                _drone.Visible = false;
                RefreshLabels();
            }
            StartLesson();
        }

        Vector3 _introPointOrCentre()
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            foreach (TargetDefinition target in _mission.Targets)
            {
                sum += target.Position;
                count++;
            }
            if (count == 0) return _mission.Perch + Vector3.forward * 100f;
            sum /= count;
            sum.y = _island.Map.Height(sum.x, sum.z) + 1f;
            return sum;
        }

        /// <summary>Where the middle of the picture meets the ground: a rocket fired without a lock goes there.</summary>
        Vector3 SightPoint()
        {
            Transform eye = _camera.Camera.transform;
            Vector3 origin = eye.position, direction = eye.forward;
            // A hostile near the line of sight takes the point, so a shot at a tank lands on the tank.
            float step = 4f;
            for (float d = 6f; d < 520f; d += step)
            {
                Vector3 p = origin + direction * d;
                if (p.y <= Mathf.Max(_island.Map.Height(p.x, p.z), Island.WaterLevel)) return p;
                step = Mathf.Min(12f, step * 1.08f);
            }
            return origin + direction * 400f;
        }

        void FieldEvent(CombatEvent e, float distance)
        {
            switch (e.Type)
            {
                case CombatEventType.TargetDestroyed:
                {
                    TargetDefinition definition = _mission.Targets[e.Target];
                    // The wreck throws its pieces, shakes the camera by how near it was, and cooks off a moment later.
                    _effects.Debris(e.Position, e.Size);
                    _camera.Shake(Mathf.Clamp(70f / Mathf.Max(20f, distance), 0.15f, 1f));
                    if (definition.Kind.Health >= 40f && _cookOffs.Count < 12)
                    {
                        int blasts = definition.Kind.Health >= 120f ? 3 : 1;
                        for (int i = 0; i < blasts; i++)
                            _cookOffs.Add(new Vector4(e.Position.x + Random.Range(-2.5f, 2.5f), e.Position.y + 0.5f, e.Position.z + Random.Range(-2.5f, 2.5f), 0.45f + i * 0.4f + Random.Range(0f, 0.3f)));
                    }
                    if (definition.Friendly) break;
                    _field.Rise(e.Position, "+" + Mathf.RoundToInt(definition.Kind.Health * 0.5f) + " XP", UiKit.Cyan);
                    if (!_firstBlood)
                    {
                        _firstBlood = true;
                        _field.Banner(Loc.T("chain.first"), Loc.T(definition.Kind.NameKey), UiKit.Accent);
                    }
                    break;
                }
                case CombatEventType.TargetHit:
                    _field.Rise(e.Position, Loc.T("field.hit"), new Color(1f, 1f, 1f, 0.9f));
                    break;
                case CombatEventType.ChainKill:
                {
                    int chain = Mathf.Clamp((int)e.Size, 2, 5);
                    _field.Banner(Loc.T("chain." + chain), Loc.T("chain.bonus", ChainBonus((int)e.Size)), chain >= 4 ? UiKit.Bad : UiKit.Accent);
                    _sfx.Play(Sound.Success, 0.7f);
                    Buzz();
                    break;
                }
                case CombatEventType.PickupDropped:
                    _effects.Dust(e.Position + Vector3.up, 2.5f, Vector3.up * 2f);
                    break;
                case CombatEventType.PickupTaken:
                {
                    var type = (PickupType)(int)e.Size;
                    Color color = type == PickupType.Repair ? UiKit.Good : type == PickupType.Ammo ? UiKit.Accent : type == PickupType.Part ? UiKit.Cyan : new Color(1f, 0.85f, 0.3f);
                    _field.Rise(_sim.State.Position, Loc.T("pickup." + type.ToString().ToLowerInvariant()), color);
                    _sfx.Play(Sound.Scan, 0.9f);
                    _shownAmmoStamp = -1;
                    Buzz();
                    break;
                }
                case CombatEventType.RifleShot:
                    _sfx.Play(Sound.Gun, 1f);
                    _camera.Shake(0.5f);
                    break;
                case CombatEventType.BulletHit:
                    _effects.Impact(e.Position);
                    if (e.Target < 0) _effects.Dust(e.Position, 1.6f, Vector3.up * 3f);
                    else _field.Rise(e.Position, Loc.T("field.hit"), Color.white);
                    break;
                case CombatEventType.IncomingFire:
                    _field.Hit(0.5f);
                    break;
            }
        }

        /// <summary>Credits a chain of kills adds to the reward.</summary>
        static int ChainBonus(int chain) => Mathf.Clamp(chain - 1, 1, 6) * 25;

        /// <summary>What the field added to the result: loot, the best chain, the sniper's marksmanship.</summary>
        void FieldResult(MissionOutcome outcome, List<string> lines)
        {
            if (_run == null || !outcome.Succeeded || _mission.Mode == "range") return;
            if (_run.PartsLooted > 0)
            {
                _save.Parts += _run.PartsLooted;
                lines.Add(Loc.T("result.blueprints", _run.PartsLooted));
            }
            int bonus = _run.Loot + (_run.BestChain >= 2 ? ChainBonus(_run.BestChain) * 2 : 0);
            if (_run.Sniper && _run.ShotsFired > 0 && _run.ShotsHit == _run.ShotsFired) bonus += 120;
            if (bonus <= 0) return;
            _save.Credits += bonus;
            _save.Stats.CreditsEarned += bonus;
            if (_run.Loot > 0) lines.Add(Loc.T("result.loot", _run.Loot, _run.PickupsTaken));
            if (_run.BestChain >= 2) lines.Add(Loc.T("result.chain", _run.BestChain, ChainBonus(_run.BestChain) * 2));
            if (_run.Sniper && _run.ShotsHit == _run.ShotsFired) lines.Add(Loc.T("result.clean", 120));
        }

        /// <summary>Every frame: the sight, the loot, the scope, the instructor.</summary>
        bool _fieldUsed;
        /// <summary>Explosions still to come from wrecks already made: where (xyz) and in how many seconds (w).</summary>
        readonly System.Collections.Generic.List<Vector4> _cookOffs = new System.Collections.Generic.List<Vector4>();

        void RunCookOffs(float deltaTime)
        {
            for (int i = _cookOffs.Count - 1; i >= 0; i--)
            {
                Vector4 c = _cookOffs[i];
                c.w -= deltaTime;
                if (c.w > 0f)
                {
                    _cookOffs[i] = c;
                    continue;
                }
                _cookOffs.RemoveAt(i);
                var at = new Vector3(c.x, c.y, c.z);
                _effects.Explosion(at, 5.5f, false);
                _sfx.Play(Sound.Explosion, Mathf.Clamp01(90f / Mathf.Max(30f, Vector3.Distance(at, _sim.State.Position))));
            }
        }

        void PresentField(float deltaTime)
        {
            bool flying = (_phase == Phase.Mission || _phase == Phase.FreeFlight) && _hud.Visible;
            _field.Visible = flying;
            if (!flying)
            {
                if (_pickups != null && _phase != Phase.Mission) _pickups.Clear();
                // Nothing from the last fight is left hanging on the glass for the next flight.
                if (_phase != Phase.Mission && _phase != Phase.FreeFlight && _fieldUsed)
                {
                    _field.Reset();
                    _cookOffs.Clear();
                    _fieldUsed = false;
                }
                return;
            }
            _fieldUsed = true;
            if (_cookOffs.Count > 0) RunCookOffs(deltaTime);
            bool mission = _phase == Phase.Mission && _run != null;
            bool busy = _introTimer > 0f || _freezeTimer > 0f || _photo || _killCam > 0f;
            _field.Update(_camera.Camera, mission ? _run.Hull : 1f, Time.unscaledDeltaTime);
            if (!mission)
            {
                _field.SetReticle(false, false);
                _field.SetScope(null, null);
                _field.SetLoot(0);
                RunLesson(deltaTime);
                return;
            }
            if (_pickups == null) _pickups = new PickupsView(_regionRoot);
            _pickups.Update(_run);
            _field.SetLoot(_run.Loot);
            PresentBoss();

            if (_run.Sniper)
            {
                _field.SetReticle(false, false);
                if (busy)
                {
                    _field.SetScope(null, null);
                    return;
                }
                Vector3 sight = SniperSight();
                int range = Mathf.RoundToInt(Vector3.Distance(_mission.Perch, sight) / 5f) * 5;
                Vector3 wind = _weather.WindAt(_simTime);
                int stamp = range + Mathf.RoundToInt(wind.magnitude * 10f) * 1000 + _run.Rounds * 100000 + Mathf.RoundToInt(_zoom) * 10000000;
                if (stamp != _shownScopeStamp)
                {
                    _shownScopeStamp = stamp;
                    _field.SetScope(Loc.T("scope.data", range, wind.magnitude.ToString("0.0"), Mathf.RoundToInt(_zoom)),
                        Loc.T("scope.rounds", _run.Rounds, _run.Remaining(_run.CurrentObjective)));
                }
                RunLesson(deltaTime);
                return;
            }
            _field.SetScope(null, null);

            bool armed = _run.IsCombat && !_run.Kamikaze && !_run.GuidedActive && !busy;
            _field.SetReticle(armed && _camera.View != CameraView.Gun, _run.LockedTarget >= 0);
            _run.FreeAim = armed ? SightPoint() : (Vector3?)null;

            if (Driving && !busy)
            {
                // Tracks throw dust.
                float speed = _sim.State.Velocity.magnitude;
                _dustClock -= deltaTime * speed;
                if (_dustClock <= 0f && speed > 2f)
                {
                    _dustClock = 2.2f;
                    _effects.Dust(_sim.State.Position - _sim.State.Velocity.normalized * 2.5f, 1.8f, Vector3.up * 1.5f);
                }
            }
            RunLesson(deltaTime);
        }

        /// <summary>Where the cross of the scope lies on the ground or on a man.</summary>
        Vector3 SniperSight()
        {
            Vector3 origin = _mission.Perch, direction = SniperAim;
            for (float d = 20f; d < 700f; d += 3f)
            {
                Vector3 p = origin + direction * d;
                if (p.y <= Mathf.Max(_island.Map.Height(p.x, p.z), Island.WaterLevel)) return p;
            }
            return origin + direction * 700f;
        }

        // ---------------------------------------------------------------- the instructor

        static readonly Dictionary<string, int> LessonSteps = new Dictionary<string, int>
        {
            { "fly", 4 }, { "fight", 4 }, { "drive", 3 }, { "snipe", 3 },
        };

        /// <summary>The heaviest thing still standing among the enemy, if it is heavy enough to be the point of the mission.</summary>
        void PresentBoss()
        {
            int boss = -1;
            float most = 259f;
            for (int i = 0; i < _mission.Targets.Count; i++)
            {
                TargetDefinition definition = _mission.Targets[i];
                if (definition.Friendly || definition.Kind.Health <= most) continue;
                most = definition.Kind.Health;
                boss = i;
            }
            if (boss < 0 || _run.Sniper)
            {
                _field.SetBoss(null, 0f);
                return;
            }
            TargetState state = _run.Target(boss);
            if (!state.Alive) _field.SetBoss(null, 0f);
            else _field.SetBoss(Loc.T(_mission.Targets[boss].Kind.NameKey).ToUpperInvariant(), state.Health / most);
        }

        bool _prologue;

        /// <summary>
        /// The very first launch does not open on a menu: the player is put in the air over an enemy
        /// position in the best aircraft there is, to see what the game becomes, and only then starts small.
        /// </summary>
        bool StartPrologue()
        {
            if (SkipIntro || _save.Lessons.Contains("prologue")) return false;
            _save.Lessons.Add("prologue");
            foreach (MissionRecord record in _save.Missions)
                if (record.Completions > 0) return false;
            if (_save.Stats.Flights > 0) return false;
            MissionDefinition op = Operations.Contract(_island, _region.Id, 40, 2, OperationKind.Strike);
            if (op == null) return false;
            _store.Save(_save);
            _prologue = true;
            StartOperation(op, -1, 0, "aegis");
            Vector3 at = _island.Spawn;
            foreach (TargetDefinition target in op.Targets)
            {
                if (target.Friendly) continue;
                at = target.Path.Count > 0 ? target.Path[0] : target.Position;
                break;
            }
            Vector3 from = at + new Vector3(0f, 0f, -190f);
            from.y = Mathf.Max(at.y, _island.Map.Height(from.x, from.z)) + 46f;
            _sim.State = new FlightState { Position = from, YawDeg = 0f, Velocity = new Vector3(0f, 0f, 14f) };
            _previous = _sim.State;
            _hasFlown = true;
            _camera.Snap();
            _field.Banner(Loc.T("prologue.title"), Loc.T("prologue.sub"), UiKit.Accent);
            return true;
        }

        void StartLesson()
        {
            _lesson = null;
            _field.SetCoach(null, null, null);
            if (_phase != Phase.Mission || _run == null || SkipIntro) return;
            bool prologue = _prologue;
            _prologue = false;
            string wanted = _run.Sniper ? "snipe" : Driving ? "drive" : _run.IsCombat && !_run.Kamikaze ? "fight" : "fly";
            if (wanted == "fight" && !_save.Lessons.Contains("fly") && !prologue) wanted = "fly";
            if (_save.Lessons.Contains(wanted)) return;
            // Somebody who has already flown a dozen missions does not need to be told how.
            if (!_lessonsAsked && (wanted == "fly" || wanted == "fight") && _save.Stats.Flights > 12 && _save.Stats.Kills > 5)
            {
                _save.Lessons.Add("fly");
                _save.Lessons.Add("fight");
                return;
            }
            _lesson = wanted;
            _lessonStep = 0;
            EnterLessonStep();
        }

        void EnterLessonStep()
        {
            _lessonClock = 0f;
            _lessonFrom = _sim.State.Position;
            _lessonYaw = _sim.State.YawDeg;
        }

        void RunLesson(float deltaTime)
        {
            if (_lesson == null) return;
            if (_phase != Phase.Mission || _run == null)
            {
                _lesson = null;
                _field.SetCoach(null, null, null);
                return;
            }
            if (_introTimer > 0f || _freezeTimer > 0f) return;
            _lessonClock += deltaTime;
            FlightState state = _sim.State;
            bool done = false;
            Vector2? point = null;
            bool swapped = _save.SticksSwapped;
            var liftStick = new Vector2(swapped ? 0.14f : 0.86f, 0.24f);
            var moveStick = new Vector2(swapped ? 0.86f : 0.14f, 0.24f);
            var fireButton = new Vector2(0.9f, 0.52f);
            switch (_lesson + _lessonStep)
            {
                case "fly0": point = liftStick; done = !state.Grounded && _sim.AltitudeAboveGround > 4f; break;
                case "fly1": point = moveStick; done = Vector3.Distance(state.Position, _lessonFrom) > 22f; break;
                case "fly2": point = liftStick; done = Mathf.Abs(Mathf.DeltaAngle(state.YawDeg, _lessonYaw)) > 50f; break;
                case "fly3": done = _lessonClock > 7f; break;
                case "fight0": done = _run.LockedTarget >= 0 || _lessonClock > 40f; break;
                case "fight1": point = fireButton; done = _run.Rockets < _sim.Spec.Rockets || _lessonClock > 30f; break;
                case "fight2": done = _lessonClock > 7f; break;
                case "fight3": done = _lessonClock > 8f || _run.PickupsTaken > 0; break;
                case "drive0": point = moveStick; done = Vector3.Distance(state.Position, _lessonFrom) > 15f; break;
                case "drive1": done = _run.LockedTarget >= 0 || _lessonClock > 45f; break;
                case "drive2": point = fireButton; done = _run.Rockets < _sim.Spec.Rockets || _lessonClock > 25f; break;
                case "snipe0": point = moveStick; done = _lessonClock > 6f; break;
                case "snipe1": done = _lessonClock > 7f; break;
                case "snipe2": point = fireButton; done = _run.ShotsFired > 0 || _lessonClock > 25f; break;
            }
            int steps = LessonSteps[_lesson];
            _field.SetCoach(Loc.T("coach.title", _lessonStep + 1, steps), Loc.T("coach." + _lesson + "." + _lessonStep), point);
            if (!done) return;
            _sfx.Play(Sound.Scan, 0.5f);
            _lessonStep++;
            EnterLessonStep();
            if (_lessonStep < steps) return;
            _save.Lessons.Add(_lesson);
            // The flying lesson is followed at once by the fighting one, if there is something to fight.
            string next = _lesson == "fly" && _run.IsCombat && !_run.Kamikaze && !_save.Lessons.Contains("fight") ? "fight" : null;
            _lesson = next;
            _lessonStep = 0;
            if (next == null) _field.SetCoach(null, null, null);
            _store.Save(_save);
        }

        /// <summary>Settings: hear the instructor again from the beginning.</summary>
        public void ResetLessons()
        {
            _lessonsAsked = true;
            _save.Lessons.Clear();
            _store.Save(_save);
        }

        // ---------------------------------------------------------------- for the screenshot run

        public Vector3 AutomationGround(bool sniper, int level)
        {
            MissionDefinition op = sniper ? Operations.SniperTask(_island, _region.Id, 3, level) : Operations.ArmourRaid(_island, _region.Id, 1, level);
            StartOperation(op);
            if (op == null) return _island.Spawn;
            foreach (TargetDefinition target in op.Targets)
                if (!target.Friendly) return target.Path.Count > 0 ? target.Path[0] : target.Position;
            return _island.Spawn;
        }

        public void AutomationSniperAim(Vector3 world)
        {
            Vector3 to = world - _mission.Perch;
            _sniperYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _sniperPitch = Mathf.Atan2(-to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
        }

        public void AutomationLesson(string lesson, int step)
        {
            _lesson = lesson;
            _lessonStep = step;
            if (lesson == null) _field.SetCoach(null, null, null);
            else EnterLessonStep();
        }

        public void AutomationHud(bool on) => _hud.Visible = on;

        public void AutomationZoom() => ToggleZoom();

        public void AutomationBanner(int chain) =>
            _field.Banner(Loc.T("chain." + Mathf.Clamp(chain, 2, 5)), Loc.T("chain.bonus", ChainBonus(chain)), chain >= 4 ? UiKit.Bad : UiKit.Accent);
    }
}
