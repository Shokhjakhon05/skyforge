using Skyforge.Core;
using Skyforge.Drone;
using Skyforge.UI;
using Skyforge.World;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// Stage 1 free flight: one drone over one island with a ring course to give the flying a
    /// purpose. Owns the simulation clock and passes the interpolated pose to everything visible.
    /// </summary>
    public sealed class FlightScene : MonoBehaviour
    {
        /// <summary>The simulation always advances in these steps, whatever the frame rate.</summary>
        const float StepSeconds = 1f / 120f;
        const string BestTimeKey = "rings.best";

        Island _island;
        FlightSim _sim;
        DroneView _drone;
        RingCourse _course;
        CameraRig _camera;
        TouchSticks _sticks;
        Hud _hud;
        DroneAudio _audio;

        FlightState _previous;
        float _accumulator;
        bool _sticksSwapped;
        bool _hasFlown;
        float _crashTimer;
        float _messageTimer;
        bool _lapRunning;
        float _lapTime;
        float _bestTime;
        int _shownCourseStamp = -1;

        void Start()
        {
            _island = Island.Generate(7);
            IslandView.Build(_island, transform);

            _sim = new FlightSim(_island, DroneSpec.Sparrow());
            _previous = _sim.State;
            _drone = DroneView.Create(transform);
            _course = new RingCourse(_island, transform);
            _camera = new CameraRig(transform, _island.Map);
            _sticks = new TouchSticks();
            _audio = new DroneAudio(transform);

            _hud = new Hud(transform);
            _hud.CameraPressed += ToggleCamera;
            _hud.ModePressed += ToggleMode;
            _hud.SticksPressed += () => _sticksSwapped = !_sticksSwapped;
            _hud.ResetPressed += Respawn;

            _bestTime = PlayerPrefs.GetFloat(BestTimeKey, 0f);
            RefreshLabels();
        }

        void Update()
        {
            float deltaTime = Mathf.Min(Time.deltaTime, 0.1f);
            _sticks.Update();
            FlightInput input = ReadInput();

            if (_crashTimer > 0f)
            {
                _crashTimer -= deltaTime;
                if (_crashTimer <= 0f) Respawn();
            }
            else
            {
                _accumulator += deltaTime;
                while (_accumulator >= StepSeconds)
                {
                    _accumulator -= StepSeconds;
                    _previous = _sim.State;
                    SimEvent simEvent = _sim.Step(input, StepSeconds);
                    if (simEvent == SimEvent.TookOff) _hasFlown = true;
                    if (simEvent == SimEvent.Crashed)
                    {
                        Crash();
                        break;
                    }
                    if (_lapRunning) _lapTime += StepSeconds;
                    if (_course.Check(_sim.State.Position)) RingPassed();
                }
            }

            Present(deltaTime);
        }

        FlightInput ReadInput()
        {
            Vector2 move = _sticksSwapped ? _sticks.Right.Value : _sticks.Left.Value;
            Vector2 lift = _sticksSwapped ? _sticks.Left.Value : _sticks.Right.Value;
            return new FlightInput { Move = move, Throttle = lift.y, Yaw = lift.x };
        }

        void Present(float deltaTime)
        {
            FlightState now = _sim.State;
            float blend = _crashTimer > 0f ? 1f : Mathf.Clamp01(_accumulator / StepSeconds);
            Vector3 position = Vector3.Lerp(_previous.Position, now.Position, blend);
            float yaw = Mathf.LerpAngle(_previous.YawDeg, now.YawDeg, blend);
            Vector2 tilt = Vector2.Lerp(_previous.TiltDeg, now.TiltDeg, blend);

            DroneSpec spec = _sim.Spec;
            float speed = new Vector2(now.Velocity.x, now.Velocity.z).magnitude;
            float speedShare = Mathf.Clamp01(speed / spec.MaxSpeed);
            bool flying = !now.Grounded && _crashTimer <= 0f;
            float load = flying
                ? Mathf.Clamp01(0.45f + tilt.magnitude / spec.MaxTiltDeg * 0.35f + now.Velocity.y / spec.MaxClimb * 0.3f)
                : 0f;

            _drone.Apply(position, yaw, tilt, load, deltaTime);
            _camera.Update(position, yaw, tilt, speedShare, deltaTime);
            _audio.Update(load, speedShare, _crashTimer <= 0f, deltaTime);

            _hud.SetTelemetry(speed * 3.6f, _sim.AltitudeAboveGround);
            // Rebuilt only when a digit changes, to keep per-frame garbage out of the flight loop.
            int courseStamp = _course.NextIndex * 100000 + Mathf.FloorToInt(_lapTime);
            if (courseStamp != _shownCourseStamp)
            {
                _shownCourseStamp = courseStamp;
                _hud.SetCourse(Loc.T("hud.rings", _course.NextIndex, _course.Count, FormatTime(_lapTime)));
            }
            _hud.UpdateMarker(_camera.Camera, _course.NextCenter, !_course.Finished);
            _hud.UpdateSticks(_sticks,
                Loc.T(_sticksSwapped ? "hint.lift" : "hint.move"),
                Loc.T(_sticksSwapped ? "hint.move" : "hint.lift"));

            if (_messageTimer > 0f)
            {
                _messageTimer -= deltaTime;
                if (_messageTimer <= 0f)
                {
                    _hud.SetMessage("");
                    if (_course.Finished) RestartCourse();
                }
            }

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
            bool record = _bestTime <= 0f || _lapTime < _bestTime;
            if (record)
            {
                _bestTime = _lapTime;
                PlayerPrefs.SetFloat(BestTimeKey, _bestTime);
                PlayerPrefs.Save();
            }
            ShowMessage(Loc.T(record ? "msg.record" : "msg.finished", FormatTime(_lapTime)), 5f);
            RefreshLabels();
        }

        void Crash()
        {
            _crashTimer = 1.6f;
            _previous = _sim.State;
            ShowMessage(Loc.T("msg.crash"), 1.6f);
        }

        void Respawn()
        {
            _crashTimer = 0f;
            _accumulator = 0f;
            _sim.Reset();
            _previous = _sim.State;
            _camera.Snap();
            RestartCourse();
        }

        void RestartCourse()
        {
            _course.Restart();
            _lapRunning = false;
            _lapTime = 0f;
        }

        void ShowMessage(string text, float seconds)
        {
            _hud.SetMessage(text);
            _messageTimer = seconds;
        }

        void ToggleCamera()
        {
            _camera.View = _camera.View == CameraView.Chase ? CameraView.Fpv : CameraView.Chase;
            _drone.Visible = _camera.View == CameraView.Chase;
            RefreshLabels();
        }

        void ToggleMode()
        {
            _sim.Mode = _sim.Mode == FlightMode.Assist ? FlightMode.Sport : FlightMode.Assist;
            RefreshLabels();
        }

        void RefreshLabels()
        {
            _hud.SetCameraLabel(Loc.T(_camera.View == CameraView.Chase ? "hud.camera.chase" : "hud.camera.fpv"));
            _hud.SetModeLabel(Loc.T(_sim.Mode == FlightMode.Assist ? "hud.mode.assist" : "hud.mode.sport"));
            _hud.SetBest(_bestTime > 0f ? Loc.T("hud.best", FormatTime(_bestTime)) : "");
        }

        static string FormatTime(float seconds)
        {
            int whole = Mathf.FloorToInt(seconds);
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }
    }
}
