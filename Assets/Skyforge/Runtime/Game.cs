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
        }

        /// <summary>The simulation always advances in these steps, whatever the frame rate.</summary>
        const float StepSeconds = 1f / 120f;

        Island _island;
        FlightSim _sim;
        DroneView _drone;
        RingCourse _course;
        BeaconsView _beacons;
        CameraRig _camera;
        TouchSticks _sticks;
        Hud _hud;
        MenuScreen _menu;
        ResultScreen _result;
        SettingsScreen _settings;
        DroneAudio _audio;
        Sfx _sfx;
        SceneLook _look;
        Weather _weather;

        SaveStore _store;
        SaveData _save;
        MissionDefinition _mission;
        MissionRun _run;

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
            _mission = MissionCatalog.FirstContact(_island);

            _sim = new FlightSim(_island, DroneSpec.Sparrow());
            _sim.Mode = (FlightMode)Mathf.Clamp(_save.FlightModeIndex, 0, 1);
            _previous = _sim.State;
            _drone = DroneView.Create(transform);
            _course = new RingCourse(_island, transform);
            _beacons = new BeaconsView(_island, transform);
            _camera = new CameraRig(transform, _island.Map);
            _camera.View = (CameraView)Mathf.Clamp(_save.CameraViewIndex, 0, 1);
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

            _menu = new MenuScreen(transform);
            _menu.StartPressed += () => { Click(); StartMission(); };
            _menu.FreeFlightPressed += () => { Click(); StartFreeFlight(); };
            _menu.SettingsPressed += () => { Click(); ShowSettings(); };
            _menu.WeatherPressed += NextFreeFlightWeather;

            _result = new ResultScreen(transform);
            _result.RetryPressed += () => { Click(); StartMission(); };
            _result.ContinuePressed += () => { Click(); ShowMenu(); };

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
            ResetFlight();
            _beacons.Hide();
            _course.Visible = false;
            _hud.Visible = false;
            _result.Visible = false;
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            _menu.Show(_mission, _save);
        }

        void StartMission()
        {
            _phase = Phase.Mission;
            ResetFlight();
            _run = new MissionRun(_mission, _sim.Spec);
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            _beacons.Show(_mission);
            _beacons.Refresh(0);
            _course.Visible = false;
            EnterFlight(true);
        }

        void StartFreeFlight()
        {
            _phase = Phase.FreeFlight;
            ResetFlight();
            _run = null;
            ApplyWeather(WeatherCatalog.Get(_save.FreeFlightWeather));
            _beacons.Hide();
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
            _shownHeadlineStamp = _shownSublineStamp = -1;
            _drone.Visible = _camera.View == CameraView.Chase;
        }

        void ShowResult()
        {
            _phase = Phase.Result;
            MissionOutcome outcome = MissionOutcome.From(_run);
            Progress.Grant(_save, _mission, outcome);
            if (!_store.Save(_save)) Debug.LogError("[Skyforge] Progress could not be saved.");

            ResetFlight();
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
                    _run.Step(_sim.State, simEvent, _load, _sim.LastTouchdownSpeed, StepSeconds);
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
            _drone.Visible = _camera.View == CameraView.Chase;
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
                _camera.Orbit(_island.Spawn, Time.unscaledTime);
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
            int scansDone = ScansDone(), scansTotal = ScansTotal();

            // Texts are rebuilt only when a digit changes, to keep garbage out of the flight loop.
            int percent = Mathf.FloorToInt(_run.ObjectiveProgress * 100f);
            int headlineStamp = _run.ObjectiveIndex * 1000 + percent;
            if (headlineStamp != _shownHeadlineStamp)
            {
                _shownHeadlineStamp = headlineStamp;
                string text = "";
                if (objective != null)
                {
                    text = objective.Type == ObjectiveType.Scan
                        ? percent > 0
                            ? Loc.T("objective.scanning", percent)
                            : Loc.T(objective.TextKey, scansDone + 1, scansTotal)
                        : Loc.T(objective.TextKey);
                }
                _hud.SetHeadline(text);
            }

            int sublineStamp = Mathf.FloorToInt(_run.Elapsed);
            if (sublineStamp != _shownSublineStamp)
            {
                _shownSublineStamp = sublineStamp;
                _hud.SetSubline(Loc.T("hud.timer", Format.Time(_run.Elapsed), Format.Time(_mission.TimeGoalSeconds)));
            }

            bool lowBattery = _run.Battery <= MissionRun.LowBattery;
            bool lowSignal = _run.Signal <= MissionRun.LowSignal;
            _hud.SetGauges(_run.Battery, lowBattery, _run.Signal, lowSignal, _run.Hull);

            bool hasTarget = objective != null && objective.Type != ObjectiveType.TakeOff && _freezeTimer <= 0f;
            _hud.UpdateMarker(_camera.Camera, hasTarget ? objective.Position : Vector3.zero, hasTarget);

            // One beep when a gauge first drops into the red, not a siren for the rest of the flight.
            bool warning = lowBattery || lowSignal;
            if (warning && !_warned) _sfx.Play(Sound.Warning);
            _warned = warning;

            if (_run.Signal <= 0f)
                _hud.SetHint(Loc.T("warn.signal_lost", Mathf.CeilToInt(_run.SignalCountdown)));
            else if (lowSignal) _hud.SetHint(Loc.T("warn.signal"));
            else if (lowBattery) _hud.SetHint(Loc.T("warn.battery"));
            else if (!_hasFlown) _hud.SetHint(Loc.T("hint.takeoff"));
            else if (objective != null && objective.Type == ObjectiveType.Scan && scansDone == 0 && percent == 0)
                _hud.SetHint(Loc.T("hint.scan"));
            else _hud.SetHint("");
        }

        int ScansTotal()
        {
            int count = 0;
            foreach (ObjectiveDefinition objective in _mission.Objectives)
                if (objective.Type == ObjectiveType.Scan) count++;
            return count;
        }

        int ScansDone()
        {
            int count = 0;
            for (int i = 0; i < _run.ObjectiveIndex && i < _mission.Objectives.Count; i++)
                if (_mission.Objectives[i].Type == ObjectiveType.Scan) count++;
            return count;
        }

        void ObjectiveCompleted(int index)
        {
            if (_mission.Objectives[index].Type != ObjectiveType.Scan) return;
            _beacons.Refresh(ScansDone());
            _sfx.Play(Sound.Scan);
            ShowMessage(Loc.T("msg.scanned"), 1.5f);
        }

        void MissionEnded()
        {
            string message;
            switch (_run.FailReason)
            {
                case FailReason.Destroyed: message = Loc.T("msg.crash"); break;
                case FailReason.BatteryEmpty: message = Loc.T("msg.battery"); break;
                case FailReason.SignalLost: message = Loc.T("msg.signal"); break;
                default: message = Loc.T("msg.complete"); break;
            }
            _sfx.Play(_run.Status == MissionStatus.Succeeded ? Sound.Success : Sound.Crash);
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
            QualityLevel level = _save.Quality == 0 ? SceneLook.Recommend() : (QualityLevel)_save.Quality;
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
            _menu.Show(_mission, _save);
        }

        void ToggleCamera()
        {
            _camera.View = _camera.View == CameraView.Chase ? CameraView.Fpv : CameraView.Chase;
            _drone.Visible = _camera.View == CameraView.Chase;
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
        public void AutomationHideSettings() => _settings.Visible = false;

        public void AutomationFreeFlight(string weatherId)
        {
            _save.FreeFlightWeather = weatherId;
            StartFreeFlight();
        }

        public void AutomationSetCamera(CameraView view)
        {
            _camera.View = view;
            _drone.Visible = view == CameraView.Chase;
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
            _hud.SetCameraLabel(Loc.T(_camera.View == CameraView.Chase ? "hud.camera.chase" : "hud.camera.fpv"));
            _hud.SetModeLabel(Loc.T(_sim.Mode == FlightMode.Assist ? "hud.mode.assist" : "hud.mode.sport"));
        }
    }
}
