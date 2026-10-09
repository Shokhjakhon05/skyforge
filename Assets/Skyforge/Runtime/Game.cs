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
        DroneAudio _audio;

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

        void Start()
        {
            _store = new SaveStore(Path.Combine(Application.persistentDataPath, "save.json"));
            _save = _store.Load(out LoadResult loadResult);
            Debug.Log("[Skyforge] Save: " + loadResult);
            if (_save.RingBest <= 0f) _save.RingBest = PlayerPrefs.GetFloat("rings.best", 0f);

            _island = Island.Generate(7);
            IslandView.Build(_island, transform);
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

            _hud = new Hud(transform);
            _hud.CameraPressed += ToggleCamera;
            _hud.ModePressed += ToggleMode;
            _hud.SticksPressed += ToggleSticks;
            _hud.MenuPressed += ShowMenu;

            _menu = new MenuScreen(transform);
            _menu.StartPressed += StartMission;
            _menu.FreeFlightPressed += StartFreeFlight;

            _result = new ResultScreen(transform);
            _result.RetryPressed += StartMission;
            _result.ContinuePressed += ShowMenu;

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
            _menu.Show(_mission, _save);
        }

        void StartMission()
        {
            _phase = Phase.Mission;
            ResetFlight();
            _run = new MissionRun(_mission, _sim.Spec);
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
        }

        void Simulate(FlightInput input, float deltaTime)
        {
            _accumulator += deltaTime;
            while (_accumulator >= StepSeconds)
            {
                _accumulator -= StepSeconds;
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
            _store.Save(_save);
            RefreshLabels();
        }

        void RefreshLabels()
        {
            _hud.SetCameraLabel(Loc.T(_camera.View == CameraView.Chase ? "hud.camera.chase" : "hud.camera.fpv"));
            _hud.SetModeLabel(Loc.T(_sim.Mode == FlightMode.Assist ? "hud.mode.assist" : "hud.mode.sport"));
        }
    }
}
