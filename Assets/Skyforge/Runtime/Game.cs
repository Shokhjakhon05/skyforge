using System.Collections.Generic;
using System.IO;
using Skyforge.Core;
using Skyforge.Drone;
using Skyforge.UI;
using Skyforge.World;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// The whole game loop: menu, a mission or free flight, the result, and back. Owns the
    /// simulation clock, the save file, and hands the drone's interpolated pose to everything visible.
    /// </summary>
    public sealed class Game : MonoBehaviour
    {
        enum Phase
        {
            Menu,
            Mission,
            FreeFlight,
            Result,
            Hangar,
        }

        /// <summary>The simulation always advances in these steps, whatever the frame rate.</summary>
        const float StepSeconds = 1f / 120f;

        Island _island;
        FlightSim _sim;
        DroneView _drone;
        RingCourse _course;
        MissionWorld _world;
        CameraRig _camera;
        TouchSticks _sticks;
        Hud _hud;
        MenuScreen _menu;
        ResultScreen _result;
        SettingsScreen _settings;
        HangarScreen _hangar;
        DroneDefinition _shownDrone;
        DroneAudio _audio;
        Sfx _sfx;
        SceneLook _look;
        Weather _weather;

        SaveStore _store;
        SaveData _save;
        List<MissionDefinition> _missions;
        int _missionIndex;
        MissionDefinition _mission;
        MissionRun _run;
        float _zoom = 1f;
        bool _actionPressed;
        bool _advanceOnContinue;

        Phase _phase;
        FlightState _previous;
        float _accumulator;
        float _load;
        bool _hasFlown;
        /// <summary>While positive the simulation is frozen: a crash being shown, or a mission ending.</summary>
        float _freezeTimer;
        float _messageTimer;
        bool _lapRunning;
        float _lapTime;
        int _shownHeadlineStamp = -1;
        int _shownSublineStamp = -1;
        float _simTime;
        bool _warned;
        float _fpsTimer;
        int _fpsFrames;

        public Island Island => _island;

        void Start()
        {
            _store = new SaveStore(Path.Combine(Application.persistentDataPath, "save.json"));
            _save = _store.Load(out LoadResult loadResult);
            Debug.Log("[Skyforge] Save: " + loadResult);
            if (_save.RingBest <= 0f) _save.RingBest = PlayerPrefs.GetFloat("rings.best", 0f);

            _island = Island.Generate(7);
            Material sea = IslandView.Build(_island, transform);
            _missions = MissionCatalog.Load(_island);
            _missionIndex = FirstUnfinishedMission();
            _mission = _missions[_missionIndex];

            _sim = CreateSim(0f);
            _previous = _sim.State;
            ShowDroneModel(Garage.Selected(_save));
            _course = new RingCourse(_island, transform);
            BeaconsView.Build(_island, transform);
            _world = new MissionWorld(transform, _island.Map);
            _camera = new CameraRig(transform, _island.Map);
            _camera.View = (CameraView)Mathf.Clamp(_save.CameraViewIndex, 0, 2);
            _sticks = new TouchSticks();
            _audio = new DroneAudio(transform);
            _sfx = new Sfx(transform);
            _look = new SceneLook(transform, _camera.Camera, sea);
            ApplyQuality();
            AudioListener.volume = _save.Muted ? 0f : 1f;

            _hud = new Hud(transform);
            _hud.CameraPressed += ToggleCamera;
            _hud.ModePressed += ToggleMode;
            _hud.SticksPressed += ToggleSticks;
            _hud.MenuPressed += () => { Click(); ShowMenu(); };
            _hud.ZoomPressed += ToggleZoom;
            _hud.ActionPressed += () => _actionPressed = true;

            _menu = new MenuScreen(transform);
            _menu.StartPressed += () => { Click(); StartMission(); };
            _menu.FreeFlightPressed += () => { Click(); StartFreeFlight(); };
            _menu.SettingsPressed += () => { Click(); ShowSettings(); };
            _menu.WeatherPressed += NextFreeFlightWeather;
            _menu.PreviousPressed += () => SelectMission(_missionIndex - 1);
            _menu.NextPressed += () => SelectMission(_missionIndex + 1);
            _menu.HangarPressed += () => { Click(); ShowHangar(); };

            _hangar = new HangarScreen(transform);
            _hangar.Viewed += ShowDroneModel;
            _hangar.Changed += () => _store.Save(_save);
            _hangar.Pressed += refused => _sfx.Play(refused ? Sound.Warning : Sound.Click);
            _hangar.BackPressed += () => { Click(); ShowMenu(); };
            _hangar.Visible = false;

            _result = new ResultScreen(transform);
            _result.RetryPressed += () => { Click(); StartMission(); };
            _result.ContinuePressed += () =>
            {
                Click();
                // After a first completion the menu moves on to the mission that just opened.
                if (_advanceOnContinue && _missionIndex + 1 < _missions.Count)
                {
                    _missionIndex++;
                    _mission = _missions[_missionIndex];
                }
                _advanceOnContinue = false;
                ShowMenu();
            };

            _settings = new SettingsScreen(transform);
            _settings.QualityPressed += SetQuality;
            _settings.FpsPressed += ToggleFps;
            _settings.SoundPressed += ToggleSound;
            _settings.ClosePressed += () => { Click(); _settings.Visible = false; };
            _settings.Visible = false;

            RefreshLabels();
            ShowMenu();
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _store != null) _store.Save(_save);
        }

        // ---------------------------------------------------------------- phases

        void ShowMenu()
        {
            _phase = Phase.Menu;
            _hangar.Visible = false;
            ShowDroneModel(Garage.Selected(_save));
            ResetFlight();
            _world.Hide();
            _course.Visible = false;
            _hud.Visible = false;
            _result.Visible = false;
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            ShowMenuScreen();
        }

        void ShowHangar()
        {
            _phase = Phase.Hangar;
            _menu.Visible = false;
            _hangar.Show(_save);
        }

        /// <summary>Swaps the drone standing on the pad for another model from the catalog.</summary>
        void ShowDroneModel(DroneDefinition drone)
        {
            if (_shownDrone == drone && _drone != null) return;
            if (_drone != null) Destroy(_drone.gameObject);
            _drone = DroneView.Create(transform, drone.Look);
            _shownDrone = drone;
        }

        void ShowMenuScreen()
        {
            _menu.Show(_mission, _save, _missionIndex + 1, _missions.Count, IsLocked(_missionIndex));
        }

        /// <summary>A mission opens when the one before it has been completed.</summary>
        bool IsLocked(int index)
        {
            if (index <= 0) return false;
            MissionRecord previous = _save.Find(_missions[index - 1].Id);
            return previous == null || previous.Completions == 0;
        }

        int FirstUnfinishedMission()
        {
            for (int i = 0; i < _missions.Count; i++)
            {
                MissionRecord record = _save.Find(_missions[i].Id);
                if (record == null || record.Completions == 0) return i;
            }
            return _missions.Count - 1;
        }

        void SelectMission(int index)
        {
            Click();
            _missionIndex = (index + _missions.Count) % _missions.Count;
            _mission = _missions[_missionIndex];
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            ShowMenuScreen();
        }

        FlightSim CreateSim(float payload)
        {
            var sim = new FlightSim(_island, Garage.SelectedSpec(_save).WithPayload(payload));
            sim.Mode = (FlightMode)Mathf.Clamp(_save.FlightModeIndex, 0, 1);
            return sim;
        }

        void StartMission()
        {
            if (IsLocked(_missionIndex)) return;
            _phase = Phase.Mission;
            _sim = CreateSim(_mission.Payload);
            ResetFlight();
            _run = new MissionRun(_mission, _sim.Spec, _island.Map);
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            _world.Show(_mission);
            _course.Visible = false;
            _hud.DetectionVisible = _mission.HasDetectors;
            EnterFlight(true);
        }

        void StartFreeFlight()
        {
            _phase = Phase.FreeFlight;
            _sim = CreateSim(0f);
            ResetFlight();
            _run = null;
            ApplyWeather(WeatherCatalog.Get(_save.FreeFlightWeather));
            _world.Hide();
            _course.Visible = true;
            RestartCourse();
            EnterFlight(false);
        }

        void EnterFlight(bool gauges)
        {
            _menu.Visible = false;
            _result.Visible = false;
            _hud.Visible = true;
            _hud.GaugesVisible = gauges;
            _hud.SetMessage("");
            _hud.SetAction(null);
            _zoom = 1f;
            _camera.Zoom = 1f;
            _actionPressed = false;
            RefreshLabels();
            _shownHeadlineStamp = _shownSublineStamp = -1;
            _drone.Visible = _camera.View != CameraView.Fpv;
        }

        void ShowResult()
        {
            _phase = Phase.Result;
            MissionOutcome outcome = MissionOutcome.From(_run);
            Progress.Grant(_save, _mission, outcome);
            _advanceOnContinue = outcome.Succeeded && _save.Find(_mission.Id).Completions == 1;
            if (!_store.Save(_save)) Debug.LogError("[Skyforge] Progress could not be saved.");

            ResetFlight();
            _world.Hide();
            _hud.Visible = false;
            _result.Show(_mission, outcome, _save);
        }

        void ResetFlight()
        {
            _freezeTimer = 0f;
            _messageTimer = 0f;
            _accumulator = 0f;
            _load = 0f;
            _simTime = 0f;
            _warned = false;
            _sim.Wind = Vector3.zero;
            _sim.Reset();
            _previous = _sim.State;
            _camera.Snap();
            _drone.Visible = true;
        }

        // ---------------------------------------------------------------- frame

        void Update()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            bool flying = _phase == Phase.Mission || _phase == Phase.FreeFlight;

            if (flying)
            {
                _sticks.Update();
                if (_freezeTimer > 0f)
                {
                    _freezeTimer -= deltaTime;
                    if (_freezeTimer <= 0f) AfterFreeze();
                }
                else
                {
                    Simulate(ReadInput(), deltaTime);
                }
            }

            // AfterFreeze may have left the flight.
            flying = _phase == Phase.Mission || _phase == Phase.FreeFlight;
            Present(deltaTime, flying);
            _look.Update();
            CountFrames();
        }

        void CountFrames()
        {
            _fpsFrames++;
            _fpsTimer += Time.unscaledDeltaTime;
            if (_fpsTimer < 0.5f) return;
            _hud.SetFps(_save.ShowFps ? Loc.T("hud.fps", Mathf.RoundToInt(_fpsFrames / _fpsTimer)) : "");
            _fpsFrames = 0;
            _fpsTimer = 0f;
        }

        void Simulate(FlightInput input, float deltaTime)
        {
            _accumulator += deltaTime;
            while (_accumulator >= StepSeconds)
            {
                _accumulator -= StepSeconds;
                _simTime += StepSeconds;
                _sim.Wind = _weather.WindAt(_simTime);
                _previous = _sim.State;
                SimEvent simEvent = _sim.Step(input, StepSeconds);
                if (simEvent == SimEvent.TookOff) _hasFlown = true;
                _load = ComputeLoad(_sim.State);

                if (_phase == Phase.Mission)
                {
                    int before = _run.ObjectiveIndex;
                    ActionResult action = _run.Step(_sim.State, simEvent, _load, _sim.LastTouchdownSpeed, StepSeconds,
                        _actionPressed, _sim.Wind, _zoom);
                    _actionPressed = false;
                    ActionTaken(action);
                    if (_run.ShotLanded) ShotLanded();
                    if (_run.ObjectiveIndex != before) ObjectiveCompleted(before);
                    if (_run.Status != MissionStatus.InProgress)
                    {
                        MissionEnded();
                        return;
                    }
                }
                else
                {
                    if (simEvent == SimEvent.Crashed)
                    {
                        _sfx.Play(Sound.Crash);
                        Freeze(Loc.T("msg.crash"), 1.6f);
                        return;
                    }
                    if (_lapRunning) _lapTime += StepSeconds;
                    if (_course.Check(_sim.State.Position)) RingPassed();
                }
            }
        }

        float ComputeLoad(FlightState state)
        {
            if (state.Grounded) return 0f;
            DroneSpec spec = _sim.Spec;
            return Mathf.Clamp01(0.45f + state.TiltDeg.magnitude / spec.MaxTiltDeg * 0.35f +
                                 state.Velocity.y / spec.MaxClimb * 0.3f);
        }

        FlightInput ReadInput()
        {
            Vector2 move = _save.SticksSwapped ? _sticks.Right.Value : _sticks.Left.Value;
            Vector2 lift = _save.SticksSwapped ? _sticks.Left.Value : _sticks.Right.Value;
            return new FlightInput { Move = move, Throttle = lift.y, Yaw = lift.x };
        }

        void Freeze(string message, float seconds)
        {
            _freezeTimer = seconds;
            _previous = _sim.State;
            _accumulator = 0f;
            _load = 0f;
            ShowMessage(message, seconds);
        }

        void AfterFreeze()
        {
            if (_phase == Phase.Mission)
            {
                ShowResult();
                return;
            }
            ResetFlight();
            RestartCourse();
            _drone.Visible = _camera.View != CameraView.Fpv;
        }

        void Present(float deltaTime, bool flying)
        {
            FlightState now = _sim.State;
            float blend = _freezeTimer > 0f || !flying ? 1f : Mathf.Clamp01(_accumulator / StepSeconds);
            Vector3 position = Vector3.Lerp(_previous.Position, now.Position, blend);
            float yaw = Mathf.LerpAngle(_previous.YawDeg, now.YawDeg, blend);
            Vector2 tilt = Vector2.Lerp(_previous.TiltDeg, now.TiltDeg, blend);
            float speed = new Vector2(now.Velocity.x, now.Velocity.z).magnitude;
            float speedShare = Mathf.Clamp01(speed / _sim.Spec.MaxSpeed);
            bool motorsOn = flying && _freezeTimer <= 0f;

            _drone.Apply(position, yaw, tilt, motorsOn ? _load : 0f, motorsOn ? deltaTime : 0f);
            _audio.Update(_load, speedShare, motorsOn, deltaTime);

            if (!flying)
            {
                // The drone sits beside whichever panel is open: menu on the left, hangar on the right.
                float side = _phase == Phase.Hangar ? 2.6f : _phase == Phase.Menu ? -2.6f : 0f;
                _camera.Orbit(_island.Spawn, Time.unscaledTime, side, _phase == Phase.Hangar ? 7.5f : 11f);
                return;
            }

            _camera.Update(position, yaw, tilt, speedShare, deltaTime);
            _hud.SetTelemetry(speed * 3.6f, _sim.AltitudeAboveGround);
            _hud.UpdateSticks(_sticks,
                Loc.T(_save.SticksSwapped ? "hint.lift" : "hint.move"),
                Loc.T(_save.SticksSwapped ? "hint.move" : "hint.lift"));

            if (_messageTimer > 0f)
            {
                _messageTimer -= deltaTime;
                if (_messageTimer <= 0f)
                {
                    _hud.SetMessage("");
                    if (_phase == Phase.FreeFlight && _course.Finished) RestartCourse();
                }
            }

            if (_phase == Phase.Mission) PresentMission();
            else PresentFreeFlight();
        }

        // ---------------------------------------------------------------- mission

        void PresentMission()
        {
            ObjectiveDefinition objective = _run.CurrentObjective;
            FlightState now = _sim.State;
            _world.Update(_run, now, Time.deltaTime);

            // Texts are rebuilt only when a number in them changes, to keep garbage out of the flight loop.
            int percent = Mathf.FloorToInt(_run.ObjectiveProgress * 100f);
            int detail = 0;
            if (objective != null && objective.Type == ObjectiveType.Find)
                detail = Mathf.FloorToInt(_run.ScannerReading(now.Position) * 100f);
            if (objective != null && objective.Type == ObjectiveType.Mark) detail = _run.ThreatsLeft;
            int headlineStamp = _run.ObjectiveIndex * 100000 + percent * 200 + detail;
            if (headlineStamp != _shownHeadlineStamp)
            {
                _shownHeadlineStamp = headlineStamp;
                _hud.SetHeadline(objective != null ? Headline(objective, percent, detail) : "");
            }

            int sublineStamp = _run.ObjectiveIndex * 100000 + Mathf.FloorToInt(_run.Elapsed);
            if (sublineStamp != _shownSublineStamp)
            {
                _shownSublineStamp = sublineStamp;
                int step = Mathf.Min(_run.ObjectiveIndex + 1, _mission.Objectives.Count);
                _hud.SetSubline(Loc.T("progress.step", step, _mission.Objectives.Count) + "   " +
                                Loc.T("hud.timer", Format.Time(_run.Elapsed), Format.Time(_mission.TimeGoalSeconds)));
            }

            bool lowBattery = _run.Battery <= MissionRun.LowBattery;
            bool lowSignal = _run.Signal <= MissionRun.LowSignal;
            _hud.SetGauges(_run.Battery, lowBattery, _run.Signal, lowSignal, _run.Hull, _run.Detection, _run.BeingDetected);

            bool live = objective != null && _freezeTimer <= 0f;
            bool hasTarget = live && TryGetMarker(objective, now.Position, out Vector3 marker);
            _hud.UpdateMarker(_camera.Camera, hasTarget ? MarkerPosition(objective, now.Position) : Vector3.zero,
                hasTarget, _run.TargetFramed);

            string action = null;
            if (live && objective.Type == ObjectiveType.Photo) action = Loc.T("hud.action.photo");
            if (live && objective.Type == ObjectiveType.Strike && _run.ShotsLeft > 0 && !_run.Shot.Active)
                action = Loc.T("hud.action.fire");
            _hud.SetAction(action);

            // One beep when something first goes wrong, not a siren for the rest of the flight.
            bool warning = lowBattery || lowSignal || _run.BeingDetected;
            if (warning && !_warned) _sfx.Play(Sound.Warning);
            _warned = warning;

            _hud.SetHint(Hint(objective, now, lowBattery, lowSignal, percent));
        }

        string Headline(ObjectiveDefinition objective, int percent, int detail)
        {
            switch (objective.Type)
            {
                case ObjectiveType.Scan:
                    return percent > 0 ? Loc.T("objective.scanning", percent) : Loc.T(objective.TextKey);
                case ObjectiveType.Observe:
                    return percent > 0 ? Loc.T("objective.observing", percent) : Loc.T(objective.TextKey);
                case ObjectiveType.Follow:
                    return percent > 0
                        ? Loc.T("objective.following", percent)
                        : Loc.T(objective.TextKey, Mathf.RoundToInt(objective.MinDistance), Mathf.RoundToInt(objective.MaxDistance));
                case ObjectiveType.Find:
                    return Loc.T(objective.TextKey, detail);
                case ObjectiveType.Strike:
                    return Loc.T(objective.TextKey, Mathf.RoundToInt(objective.MinAltitude));
                case ObjectiveType.Mark:
                    return _run.TargetFramed && percent > 0
                        ? Loc.T("objective.marking", percent)
                        : Loc.T(objective.TextKey, detail);
                default:
                    return Loc.T(objective.TextKey);
            }
        }

        string Hint(ObjectiveDefinition objective, FlightState now, bool lowBattery, bool lowSignal, int percent)
        {
            if (_run.Signal <= 0f) return Loc.T("warn.signal_lost", Mathf.CeilToInt(_run.SignalCountdown));
            if (_run.BeingDetected) return Loc.T("warn.detected");
            if (lowSignal) return Loc.T("warn.signal");
            if (lowBattery) return Loc.T("warn.battery");
            if (!_hasFlown) return Loc.T("hint.takeoff");
            if (objective == null) return "";

            if (objective.Type == ObjectiveType.Follow && !_run.TargetFramed)
            {
                Vector3 target = _run.TargetPosition(objective);
                float gap = new Vector2(now.Position.x - target.x, now.Position.z - target.z).magnitude;
                return Loc.T(gap < objective.MinDistance ? "warn.too_close" : "warn.too_far");
            }
            if (objective.Type == ObjectiveType.Scan && percent == 0 && _mission.Order == 1) return Loc.T("hint.scan");
            if ((objective.Type == ObjectiveType.Observe || objective.Type == ObjectiveType.Photo ||
                 objective.Type == ObjectiveType.Mark) && _run.TargetFramed) return Loc.T("hud.framed");
            return "";
        }

        /// <summary>Find has no marker by design; everything else points at where to go.</summary>
        bool TryGetMarker(ObjectiveDefinition objective, Vector3 drone, out Vector3 position)
        {
            position = MarkerPosition(objective, drone);
            return objective.Type != ObjectiveType.TakeOff && objective.Type != ObjectiveType.Find;
        }

        Vector3 MarkerPosition(ObjectiveDefinition objective, Vector3 drone)
        {
            if (objective.Type != ObjectiveType.Mark) return _run.TargetPosition(objective);

            // Point at the nearest threat that still has to be marked.
            Vector3 nearest = drone;
            float best = float.MaxValue;
            for (int i = 0; i < objective.Targets.Count; i++)
            {
                if (_run.IsMarked(i)) continue;
                float distance = Vector3.Distance(drone, objective.Targets[i]);
                if (distance >= best) continue;
                best = distance;
                nearest = objective.Targets[i];
            }
            return nearest;
        }

        void ActionTaken(ActionResult action)
        {
            switch (action)
            {
                case ActionResult.PhotoTaken:
                    _sfx.Play(Sound.Scan);
                    break;
                case ActionResult.PhotoMissed:
                    _sfx.Play(Sound.Warning);
                    ShowMessage(Loc.T("msg.photo_missed"), 1.2f);
                    break;
                case ActionResult.Released:
                    _sfx.Play(Sound.Click);
                    ShowMessage(Loc.T("msg.released"), 1f);
                    break;
                case ActionResult.ReleaseRefused:
                    _sfx.Play(Sound.Warning);
                    ShowMessage(Loc.T("msg.too_low"), 1.2f);
                    break;
            }
        }

        void ShotLanded()
        {
            _sfx.Play(Sound.Crash);
            ShowMessage(Loc.T(_run.ShotHit ? "msg.hit" : "msg.miss"), 1.5f);
        }

        void ObjectiveCompleted(int index)
        {
            if (_run.Status != MissionStatus.InProgress) return;
            ObjectiveType type = _mission.Objectives[index].Type;
            if (type == ObjectiveType.TakeOff || type == ObjectiveType.Strike) return;
            _sfx.Play(Sound.Scan);
            string key = type == ObjectiveType.Photo ? "msg.photo" : type == ObjectiveType.Mark ? "msg.marked" : "msg.objective";
            ShowMessage(Loc.T(key), 1.2f);
        }

        void MissionEnded()
        {
            string message;
            switch (_run.FailReason)
            {
                case FailReason.None: message = Loc.T("msg.complete"); break;
                case FailReason.Destroyed: message = Loc.T("msg.crash"); break;
                case FailReason.BatteryEmpty: message = Loc.T("msg.battery"); break;
                case FailReason.SignalLost: message = Loc.T("msg.signal"); break;
                case FailReason.Detected: message = Loc.T("msg.detected"); break;
                case FailReason.Missed: message = Loc.T("msg.miss"); break;
                default: message = Loc.T("fail." + _run.FailReason); break;
            }
            _sfx.Play(_run.Status == MissionStatus.Succeeded ? Sound.Success : Sound.Crash);
            _hud.SetAction(null);
            Freeze(message, 1.8f);
        }

        // ---------------------------------------------------------------- free flight

        void PresentFreeFlight()
        {
            int stamp = _course.NextIndex * 100000 + Mathf.FloorToInt(_lapTime);
            if (stamp != _shownHeadlineStamp)
            {
                _shownHeadlineStamp = stamp;
                _hud.SetHeadline(Loc.T("hud.rings", _course.NextIndex, _course.Count, Format.Time(_lapTime)));
            }
            if (_shownSublineStamp != 0)
            {
                _shownSublineStamp = 0;
                _hud.SetSubline(_save.RingBest > 0f ? Loc.T("hud.best", Format.Time(_save.RingBest)) : "");
            }

            _hud.UpdateMarker(_camera.Camera, _course.NextCenter, !_course.Finished && _freezeTimer <= 0f);

            if (!_hasFlown) _hud.SetHint(Loc.T("hint.takeoff"));
            else if (_course.NextIndex == 0 && !_lapRunning) _hud.SetHint(Loc.T("hint.rings"));
            else _hud.SetHint("");
        }

        void RingPassed()
        {
            if (_course.NextIndex == 1)
            {
                _lapRunning = true;
                _lapTime = 0f;
            }
            if (!_course.Finished) return;

            _lapRunning = false;
            bool record = _save.RingBest <= 0f || _lapTime < _save.RingBest;
            if (record)
            {
                _save.RingBest = _lapTime;
                _store.Save(_save);
                _shownSublineStamp = -1;
            }
            _sfx.Play(Sound.Success);
            ShowMessage(Loc.T(record ? "msg.record" : "msg.finished", Format.Time(_lapTime)), 5f);
        }

        void RestartCourse()
        {
            _course.Restart();
            _lapRunning = false;
            _lapTime = 0f;
        }

        // ---------------------------------------------------------------- shared

        void ShowMessage(string text, float seconds)
        {
            _hud.SetMessage(text);
            _messageTimer = seconds;
        }

        void Click() => _sfx.Play(Sound.Click);

        void ApplyWeather(Weather weather)
        {
            _weather = weather;
            _look.Apply(weather);
            _hud.SetWind(weather.HasWind ? Loc.T("hud.wind", Mathf.RoundToInt(weather.WindSpeed)) : "");
        }

        void ApplyQuality()
        {
            GraphicsTier level = _save.Quality == 0 ? SceneLook.Recommend() : (GraphicsTier)_save.Quality;
            _look.SetQuality(level);
        }

        void ShowSettings() => _settings.Show(_save, SettingsScreen.QualityKey((int)_look.Quality));

        void SetQuality(int level)
        {
            Click();
            _save.Quality = Mathf.Clamp(level, 0, 4);
            ApplyQuality();
            _store.Save(_save);
            ShowSettings();
        }

        void ToggleFps()
        {
            Click();
            _save.ShowFps = !_save.ShowFps;
            _store.Save(_save);
            ShowSettings();
        }

        void ToggleSound()
        {
            _save.Muted = !_save.Muted;
            AudioListener.volume = _save.Muted ? 0f : 1f;
            Click();
            _store.Save(_save);
            ShowSettings();
        }

        void NextFreeFlightWeather()
        {
            Click();
            _save.FreeFlightWeather = WeatherCatalog.Next(_save.FreeFlightWeather);
            _store.Save(_save);
            ShowMenuScreen();
        }

        void ToggleZoom()
        {
            Click();
            _zoom = _zoom >= _sim.Spec.MaxZoom ? 1f : _zoom * 2f;
            _camera.Zoom = _zoom;
            RefreshLabels();
        }

        void ToggleCamera()
        {
            _camera.View = (CameraView)(((int)_camera.View + 1) % 3);
            _drone.Visible = _camera.View != CameraView.Fpv;
            _save.CameraViewIndex = (int)_camera.View;
            SaveSettings();
        }

        void ToggleMode()
        {
            _sim.Mode = _sim.Mode == FlightMode.Assist ? FlightMode.Sport : FlightMode.Assist;
            _save.FlightModeIndex = (int)_sim.Mode;
            SaveSettings();
        }

        void ToggleSticks()
        {
            _save.SticksSwapped = !_save.SticksSwapped;
            SaveSettings();
        }

        void SaveSettings()
        {
            Click();
            _store.Save(_save);
            RefreshLabels();
        }

        // ---------------------------------------------------------------- automation
        // Used by ShotDirector to stage screenshots during the CI build; the game itself never calls these.

        public void AutomationStartMission() => StartMission();
        public void AutomationShowMenu() => ShowMenu();
        public void AutomationShowSettings() => ShowSettings();

        public void AutomationShowHangar(string droneId, int tab, int credits)
        {
            _save.Credits = credits;
            ShowHangar();
            _hangar.Show(_save, droneId, tab);
        }

        /// <summary>Flies the next flights with the given drone, owned or not.</summary>
        public void AutomationUseDrone(string droneId)
        {
            if (_save.FindDrone(droneId) == null) _save.Drones.Add(DroneRecord.New(droneId));
            _save.SelectedDrone = droneId;
            ShowDroneModel(Garage.Selected(_save));
        }
        public void AutomationHideSettings() => _settings.Visible = false;

        public void AutomationFreeFlight(string weatherId)
        {
            _save.FreeFlightWeather = weatherId;
            StartFreeFlight();
        }

        public void AutomationSelectMission(string id)
        {
            for (int i = 0; i < _missions.Count; i++)
                if (_missions[i].Id == id) _missionIndex = i;
            _mission = _missions[_missionIndex];
            // Shots are staged on any mission, whatever the campaign progress of the build machine.
            _save.GetOrAdd(_missions[Mathf.Max(0, _missionIndex - 1)].Id).Completions = 1;
        }

        public void AutomationSetCamera(CameraView view)
        {
            _camera.View = view;
            _drone.Visible = view != CameraView.Fpv;
            _camera.Snap();
        }

        /// <summary>Puts the drone in the air at a position, hovering.</summary>
        public void AutomationPlace(Vector3 position, float yawDeg)
        {
            _sim.State = new FlightState { Position = position, YawDeg = yawDeg };
            _previous = _sim.State;
            _hasFlown = true;
            _camera.Snap();
        }

        public void AutomationShowResult()
        {
            var outcome = new MissionOutcome
            {
                Succeeded = true, Stars = 2, Time = 164f, TimeGoalMet = true, NoDamage = false, CreditsGranted = 200,
            };
            _hud.Visible = false;
            _phase = Phase.Result;
            _result.Show(_mission, outcome, _save);
        }

        public Camera AutomationCamera => _camera.Camera;

        void RefreshLabels()
        {
            _hud.SetCameraLabel(Loc.T(_camera.View == CameraView.Chase ? "hud.camera.chase"
                : _camera.View == CameraView.Fpv ? "hud.camera.fpv" : "hud.camera.top"));
            _hud.SetZoomLabel(Loc.T("hud.zoom", Mathf.RoundToInt(_zoom)));
            _hud.SetModeLabel(Loc.T(_sim.Mode == FlightMode.Assist ? "hud.mode.assist" : "hud.mode.sport"));
        }
    }
}
