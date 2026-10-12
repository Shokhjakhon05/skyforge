using System.Collections.Generic;
using Skyforge;
using Skyforge.Core;
using Skyforge.UI;
using Skyforge.World;
using UnityEngine;
using UnityEngine.UI;

namespace Sniper
{
    /// <summary>
    /// The whole game: the list of contracts, the briefing, the shot, the result, the armoury.
    /// Owns the camera behind the scope and steps the mission.
    /// </summary>
    public sealed class SniperGame : MonoBehaviour
    {
        enum Phase { Menu, Briefing, Play, Result, Armory, Paused }

        const float WideFov = 46f, Step = 1f / 120f;
        static readonly float[] Zooms = { 1f, 2f, 4f, 6f, 8f, 10f, 12f, 14f, 16f, 20f };

        SniperSave _save;
        CameraRig _rig;
        Camera _cam;
        SceneLook _look;
        Effects _effects;
        Sfx _sfx;
        Music _music;
        SniperHud _hud;
        SniperAudio _audio;
        SniperAds _ads;
        bool _doubled, _breakDue;
        SniperWorld _world;
        SniperSim _sim;
        MissionDef _mission;
        RifleDef _rifle;
        Phase _phase;
        GameObject _screen;
        string _menuPlace;

        float _yaw, _pitch, _breath = 1f, _recoil, _accumulator, _ending, _swayClock;
        int _zoomIndex, _finger = -1;
        bool _holding, _firePressed, _reloadPressed;
        Vector3 _lastMouse;
        // The round being followed to its mark.
        bool _following;
        float _afterShot;
        Vector3 _followPoint, _followFrom;
        Transform _bullet;
        MissionResult _result;
        /// <summary>Real seconds of slowed time left after a head shot.</summary>
        float _slow;
        float _punch;
        bool _wasReloading, _dailyRun;
        int _streak, _streakBonus;
        readonly List<string> _news = new List<string>();

        public bool SkipIntro;
        public Camera AutomationCamera => _cam;
        public SniperSim Sim => _sim;

        void Start()
        {
            _save = SniperSave.Load();
            Loc.SetLanguage(string.IsNullOrEmpty(_save.Language) ? DeviceLanguage() : _save.Language);
            _rig = new CameraRig(transform, null);
            _cam = _rig.Camera;
            _cam.nearClipPlane = 0.3f;
            _cam.farClipPlane = 1400f;
            _sfx = new Sfx(transform);
            _music = Music.Create(transform);
            _look = new SceneLook(transform, _cam, null);
            _look.SetQuality(SceneLook.Recommend());
            // The people shot at stand two hundred metres off: shadows have to reach that far or the whole scene goes flat.
            var pipeline = UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (pipeline != null)
            {
                pipeline.shadowDistance = 340f;
                pipeline.shadowCascadeCount = 3;
            }
            _effects = new Effects(transform);
            UiKit.Tap = () => _sfx.Play(Sound.Tap, 0.7f);
            AudioListener.volume = _save.Muted ? 0f : 1f;
            _audio = new SniperAudio(transform);
            _ads = new SniperAds();
            _music.Level = 0.3f;
            _hud = new SniperHud(transform);
            _hud.FirePressed += () => _firePressed = true;
            _hud.ReloadPressed += () => _reloadPressed = true;
            _hud.ZoomInPressed += () => SetZoom(_zoomIndex + 1);
            _hud.ZoomOutPressed += () => SetZoom(_zoomIndex - 1);
            _hud.BreathPressed += () => _holding = !_holding && _breath > 0.15f;
            _hud.PausePressed += Pause;
            _hud.Visible = false;
            ShowMenu();
            if (ShotDirector.OutputDir == null) SniperSplash.Create(transform, _sfx);
        }

        static string DeviceLanguage()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian: return "ru";
                default: return "en";
            }
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _save != null) _save.Store();
        }

        // ---------------------------------------------------------------- the place

        void LoadMission(MissionDef mission)
        {
            _mission = mission;
            LocationDef location = Campaign.Location(mission.LocationId);
            if (_world != null) _world.Destroy();
            _effects.ClearAll();
            _world = new SniperWorld(transform, location);
            _rifle = _save.Fitted();
            _sim = new SniperSim(mission, null, _rifle, 1 + _save.Completed);
            foreach (Cover cover in _world.Covers) _sim.AddCover(cover);
            _world.Populate(_sim);
            string weather = location.Light == "dusk" ? "sunset" : location.Light == "night" ? "moonlit" : location.Light == "overcast" ? "overcast" : WeatherCatalog.Clear;
            _look.Apply(WeatherCatalog.Get(weather));
            _audio.Ambience(location.Ambience, location.AmbienceVolume);
            Vector3 to = mission.LookAt - mission.Perch;
            _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            _zoomIndex = 0;
            _breath = 1f;
            _holding = false;
            _recoil = 0f;
            _accumulator = 0f;
            _ending = 0f;
            _following = false;
            _afterShot = 0f;
            _result = null;
            _streak = 0;
            _streakBonus = 0;
            _slow = 0f;
            _punch = 0f;
            Time.timeScale = 1f;
            PlaceCamera(0f);
        }

        void SetZoom(int index)
        {
            int most = 0;
            float limit = _rifle != null ? _rifle.Zoom : 6f;
            for (int i = 0; i < Zooms.Length; i++)
                if (Zooms[i] <= limit + 0.01f) most = i;
            _zoomIndex = Mathf.Clamp(index, 0, most);
        }

        float Zoom => Zooms[_zoomIndex];

        void PlaceCamera(float deltaTime)
        {
            if (_mission == null) return;
            _swayClock += deltaTime;
            float amplitude = 0.38f * (1f - (_rifle != null ? _rifle.Stability : 0f)) * (_holding ? 0.1f : 1f) * (Zoom > 1f ? 1f : 0.3f);
            float swayX = (Mathf.PerlinNoise(_swayClock * 0.42f, 3.1f) - 0.5f) * 2f * amplitude + Mathf.Sin(_swayClock * 1.9f) * amplitude * 0.25f;
            float swayY = (Mathf.PerlinNoise(7.7f, _swayClock * 0.36f) - 0.5f) * 2f * amplitude + Mathf.Sin(_swayClock * 2.6f) * amplitude * 0.3f;
            Transform t = _cam.transform;
            t.position = _mission.Perch;
            t.rotation = Quaternion.Euler(_pitch + swayY - _recoil, _yaw + swayX, 0f);
            _cam.fieldOfView = WideFov / Zoom * (1f + _punch * 0.07f);
        }

        // ---------------------------------------------------------------- the shot

        void Update()
        {
            float real = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            _hud.Update(real);
            _effects.Update(Mathf.Min(Time.deltaTime, 0.1f), _cam.transform);
            _look.Update();
            _audio.Update(real);
            if (_world != null && _phase != Phase.Paused) _world.Move(Mathf.Min(Time.deltaTime, 0.1f));
            if (_phase != Phase.Play || _sim == null) return;

            // A head shot is given a breath of slowed time to be seen in.
            if (_slow > 0f && !_following)
            {
                _slow -= real;
                Time.timeScale = _slow > 0f ? 0.22f : 1f;
            }
            _punch = Mathf.MoveTowards(_punch, 0f, real * 6f);
            bool reloading = _sim.ReloadLeft > 0f;
            if (reloading && !_wasReloading) _audio.Play("reload", 0.8f);
            _wasReloading = reloading;

            ReadAim();
            if (_holding)
            {
                _breath -= real / 5f;
                if (_breath <= 0f) _holding = false;
            }
            else _breath = Mathf.Min(1f, _breath + real / 7f);
            _recoil = Mathf.MoveTowards(_recoil, 0f, real * 5f);

            if (_reloadPressed)
            {
                _reloadPressed = false;
                _sim.Reload();
            }
            if (_firePressed)
            {
                _firePressed = false;
                Fire();
            }

            _accumulator += Mathf.Min(Time.deltaTime, 0.1f);
            while (_accumulator >= Step)
            {
                _accumulator -= Step;
                _sim.Events.Clear();
                _sim.Step(Step);
                foreach (SimEvent e in _sim.Events) Handle(e);
            }
            _world.Present(_sim);

            if (_following) Follow(real);
            else PlaceCamera(real);
            PresentHud();

            if (_sim.Outcome != Outcome.Running && !_following)
            {
                _ending += real;
                if (_ending > 1.4f) Finish();
            }
        }

        void ReadAim()
        {
            if (_following) return;
            Vector2 drag = Vector2.zero;
            UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    if (_finger < 0 && (events == null || !events.IsPointerOverGameObject(touch.fingerId))) _finger = touch.fingerId;
                    continue;
                }
                if (touch.fingerId != _finger) continue;
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled) _finger = -1;
                else drag += touch.deltaPosition;
            }
            if (Input.touchCount == 0)
            {
                _finger = -1;
                if (Input.GetMouseButtonDown(0)) _lastMouse = Input.mousePosition;
                else if (Input.GetMouseButton(0) && (events == null || !events.IsPointerOverGameObject()))
                {
                    drag = (Vector2)(Input.mousePosition - _lastMouse);
                    _lastMouse = Input.mousePosition;
                }
            }
            if (_save.Gyro && SystemInfo.supportsGyroscope)
            {
                if (!Input.gyro.enabled) Input.gyro.enabled = true;
                Vector3 turn = Input.gyro.rotationRateUnbiased;
                float gain = Mathf.Rad2Deg * Time.unscaledDeltaTime * Mathf.Clamp(_cam.fieldOfView / 20f, 0.08f, 1f);
                _yaw -= turn.y * gain;
                _pitch = Mathf.Clamp(_pitch - turn.x * gain, -20f, 60f);
            }
            if (drag.sqrMagnitude <= 0f) return;
            // A finger's width moves the cross the same distance on the glass at any magnification.
            float degreesPerPixel = _cam.fieldOfView / Mathf.Max(1, Screen.height) * 1.15f * _save.Sensitivity;
            _yaw += drag.x * degreesPerPixel;
            _pitch = Mathf.Clamp(_pitch - drag.y * degreesPerPixel, -20f, 60f);
        }

        void Fire()
        {
            if (_following) return;
            Vector3 direction = _cam.transform.forward;
            if (!_sim.Fire(_mission.Perch, direction))
            {
                if (_sim.InMagazine == 0) _sim.Reload();
                _sfx.Play(Sound.Warning, 0.4f);
                return;
            }
            _audio.Play(_rifle.Suppressed ? "shot_quiet" : "shot", 1f, Random.Range(0.96f, 1.04f));
            if (_sim.InMagazine > 0) _audio.PlayLater("bolt", 0.45f, 0.7f);
            _recoil = 1.5f;
            _punch = 1f;
            Buzz();
            // The last round of the job is followed to its mark.
            if (_sim.TargetsLeft != 1 || SkipIntro && !AutomationFollow) return;
            for (int i = 0; i < _sim.ActorCount; i++)
            {
                if (_sim.Def(i).Role != Role.Target || _sim.Actor(i).Mood == Mood.Dead) continue;
                Vector3 chest = _sim.Actor(i).Position + Vector3.up * 1.3f;
                Vector3 along = chest - _mission.Perch;
                float miss = Vector3.Cross(direction, along).magnitude;
                if (miss > 1.6f) return;
                _following = true;
                _followPoint = chest;
                _afterShot = 0f;
                Time.timeScale = 0.1f;
                _hud.SetScope(false);
            }
        }

        public bool AutomationFollow;

        void Follow(float real)
        {
            if (_bullet == null)
            {
                var builder = new MeshBuilder();
                builder.Cylinder(Vector3.zero, 0.03f, 0.008f, 0.16f, 8, Palette.Yellow);
                var obj = new GameObject("Bullet");
                obj.transform.SetParent(transform, false);
                obj.AddComponent<MeshFilter>().sharedMesh = builder.Build("Bullet");
                obj.AddComponent<MeshRenderer>().sharedMaterial = GameMaterials.PaletteLit;
                _bullet = obj.transform;
            }
            Transform t = _cam.transform;
            if (_sim.Bullets.Count > 0 && _afterShot <= 0f)
            {
                Bullet b = _sim.Bullets[_sim.Bullets.Count - 1];
                Vector3 forward = b.Velocity.normalized, right = Vector3.Cross(Vector3.up, forward).normalized;
                _bullet.gameObject.SetActive(true);
                _bullet.SetPositionAndRotation(b.Position, Quaternion.FromToRotation(Vector3.up, forward));
                _followFrom = b.Position - forward * 1.7f + right * 0.42f + Vector3.up * 0.16f;
                t.position = _followFrom;
                t.rotation = Quaternion.LookRotation(b.Position + forward * 6f - _followFrom, Vector3.up);
                _cam.fieldOfView = 44f;
                return;
            }
            // It has landed: stay a moment on what it did.
            _bullet.gameObject.SetActive(false);
            _afterShot += real;
            Time.timeScale = 0.3f;
            Vector3 aside = _followPoint + (_followFrom - _followPoint).normalized * 7f + Vector3.up * 1.2f;
            t.position = Vector3.Lerp(t.position, aside, real * 3f);
            t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(_followPoint - t.position, Vector3.up), real * 6f);
            _cam.fieldOfView = Mathf.Lerp(_cam.fieldOfView, 30f, real * 3f);
            if (_afterShot < 1.5f) return;
            _following = false;
            Time.timeScale = 1f;
        }

        void Handle(SimEvent e)
        {
            switch (e.Type)
            {
                case SimEventType.Kill:
                {
                    ActorDef def = _sim.Def(e.Actor);
                    _followPoint = e.Position;
                    if (def.Kind == Kind.Board)
                    {
                        // Steel rings; nothing bleeds.
                        _effects.Dust(e.Position, 0.5f, Vector3.up * 0.6f);
                        _audio.Play("miss_metal", 1f, e.Zone == Zone.Head ? 1.35f : 1.05f);
                    }
                    else
                    {
                        _effects.Blood(e.Position, e.Position - _mission.Perch);
                        _audio.Play(e.Zone == Zone.Head ? "hit_head" : "hit_body", 0.9f);
                        _audio.PlayLater("body_fall", 0.55f, 0.6f);
                    }
                    if (def.Role == Role.Civilian)
                    {
                        _hud.Message(Loc.T(def.Kind == Kind.Man ? "sn.civilian" : WhyKey(LostWhy.Civilian)), def.Kind == Kind.Man ? Loc.T("sn.civilian.sub") : null, UiKit.Bad);
                        _sfx.Play(Sound.Warning);
                        break;
                    }
                    bool head = e.Zone == Zone.Head;
                    string name = string.IsNullOrEmpty(def.NameKey) ? "" : Loc.T(def.NameKey);
                    int metres = Mathf.RoundToInt(Vector3.Distance(e.Position, _mission.Perch));
                    _streak = head ? _streak + 1 : 0;
                    int extra = head ? 60 : 0;
                    if (_streak >= 2)
                    {
                        extra += 40 * _streak;
                        _streakBonus += 40 * _streak;
                    }
                    bool record = metres > _save.LongestShot + 0.5f && _save.Kills + _sim.Kills > 3;
                    if (metres > _save.LongestShot) _save.LongestShot = metres;
                    string title = _streak >= 2 ? Loc.T("sn.streak", _streak) : Loc.T(head ? "sn.headshot" : "sn.down");
                    string sub = name + "   " + metres + " " + Loc.T("sn.m") + (def.Bounty + extra > 0 ? "   +$" + (def.Bounty + extra) : "") + (record ? "   " + Loc.T("sn.record") : "");
                    _hud.Message(title, sub, head ? UiKit.Accent : Color.white);
                    _sfx.Play(Sound.Success, head ? 0.6f : 0.35f);
                    if (head && !_following) _slow = 0.5f;
                    Buzz();
                    break;
                }
                case SimEventType.Hit:
                    _effects.Blood(e.Position, e.Position - _mission.Perch);
                    _audio.Play("hit_body", 0.8f);
                    _hud.Message(Loc.T("sn.hit"), null, Color.white);
                    break;
                case SimEventType.Explosion:
                    _world.Blow(e.Actor);
                    _effects.Explosion(e.Position, 8f, false);
                    _sfx.Play(Sound.Explosion, 1f);
                    _hud.Message(Loc.T("sn.boom"), null, UiKit.Accent);
                    _recoil = Mathf.Max(_recoil, 0.6f);
                    Buzz();
                    break;
                case SimEventType.Miss:
                    _streak = 0;
                    _followPoint = e.Position;
                    _effects.Dust(e.Position + Vector3.up * 0.1f, 0.7f, Vector3.up * 1.2f);
                    _audio.Play(e.Position.y > 0.25f ? "miss_metal" : "miss_ground", 0.7f, Random.Range(0.9f, 1.15f));
                    break;
                case SimEventType.Alarm:
                    _hud.Message(Loc.T("sn.alarm"), Loc.T("sn.alarm.sub"), UiKit.Bad);
                    _sfx.Play(Sound.Warning);
                    break;
                case SimEventType.EnemyShot:
                    _effects.MuzzleFlash(e.Position, 1.6f);
                    _effects.Tracer(e.Position, Vector3.Lerp(e.Position, _mission.Perch, 0.85f) + Random.insideUnitSphere * 2f);
                    _audio.Play("shot_far", 0.5f, Random.Range(0.92f, 1.08f));
                    break;
                case SimEventType.PlayerHit:
                    _hud.Hurt();
                    _audio.Play("hit_body", 1f, 0.8f);
                    _recoil = Mathf.Max(_recoil, 0.8f);
                    Buzz();
                    break;
                case SimEventType.Escaped:
                    _hud.Message(Loc.T("sn.escaped"), Loc.T(_sim.Def(e.Actor).NameKey), UiKit.Bad);
                    break;
                case SimEventType.AllyDown:
                    _hud.Message(Loc.T("sn.allydown"), null, UiKit.Bad);
                    break;
                case SimEventType.Reloaded:
                    _audio.Play("bolt", 0.7f);
                    break;
            }
        }

        static void Buzz()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        void PresentHud()
        {
            bool scoped = Zoom > 1f && !_following;
            _hud.SetScope(scoped);
            _hud.SetObjective(Loc.T("sn.objective", _sim.TargetsLeft));
            if (_mission.Seconds > 0f)
            {
                int left = Mathf.Max(0, Mathf.CeilToInt(_mission.Seconds - _sim.Elapsed));
                _hud.SetClock(left / 60 + ":" + (left % 60).ToString("00"));
            }
            else _hud.SetClock("");
            _hud.SetAmmo(_sim.ReloadLeft > 0f ? Loc.T("sn.reloading") : _sim.InMagazine + "  |  " + _sim.Reserve);
            _hud.SetZoom("x" + Mathf.RoundToInt(Zoom));
            _hud.SetBreath(_breath, _holding);
            _hud.SetHealth(_sim.Health, _sim.Alarm || _sim.Health < 1f);

            // What is under the cross, and how far.
            Vector3 origin = _mission.Perch, forward = _cam.transform.forward;
            float range = forward.y < -0.001f ? origin.y / -forward.y : 999f;
            int used = 0;
            for (int i = 0; i < _sim.ActorCount; i++)
            {
                ActorState state = _sim.Actor(i);
                if (state.Mood == Mood.Dead || state.Mood == Mood.Gone) continue;
                ActorDef def = _sim.Def(i);
                Vector3 head = state.Position + new Vector3(0f, 1.95f, 0f);
                Vector3 to = head - origin;
                float off = Vector3.Angle(forward, to);
                if (off < 1.2f) range = Mathf.Min(range, to.magnitude);
                if (_following) continue;
                Vector3 screen = _cam.WorldToScreenPoint(head);
                if (screen.z <= 0f || screen.x < 0f || screen.x > Screen.width || screen.y < 0f || screen.y > Screen.height) continue;
                // A man is named when the cross is laid on him. A target also carries a small red mark, so the square can be read at a glance.
                // "Pro" takes both away: the briefing said what he looks like.
                if (_save.Pro) continue;
                bool aimed = off < Mathf.Max(0.5f, _cam.fieldOfView * 0.1f);
                if (def.Role == Role.Target)
                    _hud.Tag(used++, screen, aimed ? "<color=#ff5a40>" + Loc.T(def.NameKey).ToUpperInvariant() + "</color>" + (def.Bounty > 0 ? "\n<size=20>$" + def.Bounty + "</size>" : "") : "<color=#ff5a40><size=30>v</size></color>");
                else if (aimed && scoped)
                    _hud.Tag(used++, screen, def.Role == Role.Civilian ? "<color=#6fe08a>" + Loc.T("sn.tag.civilian") + "</color>" : def.Role == Role.Ally ? "<color=#60a0ff>" + Loc.T("sn.tag.ally") + "</color>" : "<color=#ffb040>" + Loc.T("sn.tag.guard") + "</color>");
            }
            _hud.EndTags(used);
            _hud.SetHint(Lesson());
            Vector3 wind = _mission.Wind;
            _hud.SetData(_save.Pro ? Loc.T("sn.data.pro", wind.magnitude.ToString("0.0"), wind.magnitude < 0.1f ? "" : wind.x > 0f ? ">>" : "<<")
                : Loc.T("sn.data", Mathf.RoundToInt(Mathf.Min(range, 999f)), wind.magnitude.ToString("0.0"), wind.magnitude < 0.1f ? "" : wind.x > 0f ? ">>" : "<<"));
            _hud.SetButtons(!_following || _afterShot > 0f);
        }

        /// <summary>The first contract of a new player: one line that says what to do next with the rifle.</summary>
        string Lesson()
        {
            if (_save.Completed > 0 || _mission.Order != 1 || _following || _sim.Outcome != Outcome.Running) return "";
            if (_sim.Shots > 0 && _sim.TargetsLeft == 0) return "";
            if (_zoomIndex < 2) return Loc.T("sn.lesson.zoom");
            float nearest = 999f;
            for (int i = 0; i < _sim.ActorCount; i++)
            {
                if (_sim.Def(i).Role != Role.Target || _sim.Actor(i).Mood == Mood.Dead) continue;
                Vector3 head = _sim.Actor(i).Position + new Vector3(0f, SniperSim.HeadHeight, 0f);
                nearest = Mathf.Min(nearest, Vector3.Angle(_cam.transform.forward, head - _mission.Perch));
            }
            if (nearest > 0.9f) return Loc.T("sn.lesson.aim");
            if (!_holding && _breath > 0.3f) return Loc.T("sn.lesson.breath");
            return Loc.T("sn.lesson.fire");
        }

        // ---------------------------------------------------------------- screens

        void Clear()
        {
            if (_screen != null) Destroy(_screen);
            _screen = null;
        }

        RectTransform Screen_(string name, bool dim)
        {
            Clear();
            Canvas canvas = UiKit.CreateCanvas(transform, name, 20);
            _screen = canvas.gameObject;
            if (dim)
            {
                Image back = UiKit.Box(canvas.transform, "Dim", new Color(0.01f, 0.02f, 0.04f, 0.72f));
                UiKit.Stretch(back.rectTransform);
            }
            return UiKit.SafeArea(canvas.transform);
        }

        Text Words(Transform parent, string text, int size, Vector2 anchor, Vector2 at, Vector2 box, TextAnchor align, Color color, bool wrap = false)
        {
            Text label = UiKit.Label(parent, size, align, wrap);
            label.text = text;
            label.color = color;
            UiKit.Place(label.rectTransform, anchor, at, box);
            return label;
        }

        void Press(Transform parent, string text, Color color, int size, Vector2 anchor, Vector2 at, Vector2 box, System.Action action)
        {
            Text label = UiKit.Button(parent, "Button", color, size, () => action());
            label.text = text;
            if (color.r > 0.8f && color.g > 0.4f && color.b < 0.4f) label.color = new Color(0.1f, 0.05f, 0f);
            UiKit.Place((RectTransform)label.transform.parent, anchor, at, box);
        }

        void Stars(Transform parent, int stars, Vector2 anchor, Vector2 at, float size)
        {
            for (int i = 0; i < 3; i++)
            {
                Image star = UiKit.Box(parent, "Star", i < stars ? UiKit.Accent : new Color(1f, 1f, 1f, 0.18f), UiKit.Star);
                star.raycastTarget = false;
                UiKit.Place(star.rectTransform, anchor, at + new Vector2((i - 1) * size * 1.1f, 0f), new Vector2(size, size));
            }
        }

        bool Unlocked(MissionDef mission)
        {
            if (mission.Order <= 1) return true;
            foreach (MissionDef other in Campaign.Missions)
                if (other.Order == mission.Order - 1) return (_save.Record(other.Id)?.Stars ?? 0) > 0;
            return true;
        }

        void ShowMenu()
        {
            _phase = Phase.Menu;
            _dailyRun = false;
            Time.timeScale = 1f;
            _hud.Visible = false;
            _music.Level = 0.3f;
            _music.Menu(0);
            // The chosen place stands behind the menu; the first time, the place of the next open contract.
            if (_menuPlace == null)
            {
                foreach (MissionDef mission in Campaign.Missions)
                    if (Unlocked(mission)) _menuPlace = mission.LocationId;
            }
            MissionDef backdrop = null;
            foreach (MissionDef mission in Campaign.Missions)
                if (mission.LocationId == _menuPlace && backdrop == null) backdrop = mission;
            LoadMission(backdrop);
            _zoomIndex = 0;
            LocationDef place = Campaign.Location(backdrop.LocationId);
            _cam.transform.position = place.MenuEye;
            _cam.transform.rotation = Quaternion.LookRotation(place.MenuLook - place.MenuEye, Vector3.up);
            _cam.fieldOfView = 46f;

            RectTransform s = Screen_("Menu", false);
            Image shade = UiKit.Box(s, "Shade", new Color(0.01f, 0.02f, 0.04f, 0.55f), UiKit.Gradient);
            shade.raycastTarget = false;
            UiKit.Stretch(shade.rectTransform);
            Words(s, Loc.T("sn.title"), 96, new Vector2(0f, 1f), new Vector2(70f, -50f), new Vector2(1200f, 110f), TextAnchor.UpperLeft, Color.white);
            Words(s, Loc.T("sn.tagline"), 28, new Vector2(0f, 1f), new Vector2(74f, -160f), new Vector2(1200f, 40f), TextAnchor.UpperLeft, UiKit.Cyan);
            Words(s, "$ " + _save.Cash.ToString("N0"), 44, new Vector2(1f, 1f), new Vector2(-70f, -56f), new Vector2(600f, 60f), TextAnchor.UpperRight, UiKit.Accent);
            Words(s, Loc.T("sn.stars", _save.Stars, Campaign.Missions.Count * 3), 26, new Vector2(1f, 1f), new Vector2(-70f, -116f), new Vector2(600f, 36f), TextAnchor.UpperRight, Color.white);

            int tab = 0;
            foreach (LocationDef location in Campaign.Locations)
            {
                string id = location.Id;
                Press(s, Loc.T(location.NameKey).ToUpperInvariant(), id == _menuPlace ? UiKit.Lit : UiKit.SolidPanelColor, 24, new Vector2(0f, 0f), new Vector2(70f + tab * 298f, 520f), new Vector2(288f, 70f), () =>
                {
                    _menuPlace = id;
                    ShowMenu();
                });
                tab++;
            }

            int shown = 0;
            foreach (MissionDef mission in Campaign.Missions)
            {
                if (mission.LocationId != _menuPlace) continue;
                if (shown >= 6) break;
                bool open = Unlocked(mission);
                MissionDef picked = mission;
                var at = new Vector2(70f + shown * 300f, 250f);
                Text card = UiKit.Button(s, "Mission", open ? UiKit.SolidPanelColor : new Color(0.03f, 0.04f, 0.06f, 0.7f), 30, () =>
                {
                    if (open) ShowBriefing(picked);
                    else _sfx.Play(Sound.Warning, 0.5f);
                });
                var rect = (RectTransform)card.transform.parent;
                UiKit.Place(rect, new Vector2(0f, 0f), at, new Vector2(288f, 250f));
                card.text = "";
                Words(rect, mission.Order.ToString("00"), 54, new Vector2(0f, 1f), new Vector2(22f, -14f), new Vector2(200f, 64f), TextAnchor.UpperLeft, open ? UiKit.Accent : new Color(1f, 1f, 1f, 0.3f));
                Words(rect, open ? Loc.T(mission.TitleKey) : Loc.T("sn.locked"), 28, new Vector2(0f, 1f), new Vector2(22f, -86f), new Vector2(250f, 80f), TextAnchor.UpperLeft, open ? Color.white : new Color(1f, 1f, 1f, 0.4f), true);
                Words(rect, "$" + mission.Reward, 24, new Vector2(1f, 1f), new Vector2(-22f, -30f), new Vector2(200f, 30f), TextAnchor.UpperRight, new Color(1f, 1f, 1f, 0.7f));
                Stars(rect, _save.Record(mission.Id)?.Stars ?? 0, new Vector2(0.5f, 0f), new Vector2(0f, 26f), 44f);
                shown++;
            }
            Press(s, Loc.T("sn.armory"), UiKit.Lit, 34, new Vector2(1f, 0f), new Vector2(-70f, 60f), new Vector2(360f, 96f), ShowArmory);
            MissionDef daily = DailyContract();
            if (daily != null)
            {
                bool done = _save.DailyDone == Today;
                Press(s, done ? Loc.T("sn.daily.done") : Loc.T("sn.daily") + "  x2   " + Loc.T(daily.TitleKey), done ? UiKit.Faint : UiKit.Accent, 26, new Vector2(1f, 1f), new Vector2(-70f, -170f), new Vector2(600f, 76f), () =>
                {
                    if (done) _sfx.Play(Sound.Warning, 0.5f);
                    else ShowBriefing(daily, true);
                });
            }
            if (_save.DayStamp != Today) ShowDayPay();
            Press(s, Loc.T("sn.settings"), UiKit.SolidPanelColor, 26, new Vector2(1f, 0f), new Vector2(-450f, 60f), new Vector2(250f, 96f), ShowSettings);
            Press(s, Loc.T("sn.career"), UiKit.SolidPanelColor, 26, new Vector2(1f, 0f), new Vector2(-720f, 60f), new Vector2(250f, 96f), ShowCareer);
            bool hunting = _save.Completed >= 6;
            Press(s, hunting ? Loc.T("sn.hunt") + "  " + _save.HuntLevel : Loc.T("sn.hunt.locked"), hunting ? UiKit.Accent : UiKit.Faint, 26, new Vector2(1f, 0f), new Vector2(-990f, 60f), new Vector2(330f, 96f), () =>
            {
                if (hunting) StartHunt();
                else _sfx.Play(Sound.Warning, 0.5f);
            });
            Press(s, Loc.T("sn.range") + "  " + _save.RangeLevel, UiKit.SolidPanelColor, 26, new Vector2(0f, 0f), new Vector2(70f, 60f), new Vector2(250f, 96f), StartRange);
            bool wild = _save.Completed >= 3;
            Press(s, wild ? Loc.T("sn.safari") + "  " + _save.SafariLevel : Loc.T("sn.safari.locked"), wild ? UiKit.SolidPanelColor : UiKit.Faint, 26, new Vector2(0f, 0f), new Vector2(330f, 60f), new Vector2(250f, 96f), () =>
            {
                if (wild) StartSafari();
                else _sfx.Play(Sound.Warning, 0.5f);
            });
        }

        static bool Endless(MissionDef mission) => mission.Id == Hunt.Id || mission.Id == Range.Id || mission.Id == Safari.Id;

        int EndlessLevel(MissionDef mission) => mission.Id == Range.Id ? _save.RangeLevel : mission.Id == Safari.Id ? _save.SafariLevel : _save.HuntLevel;

        string MissionTitle(MissionDef mission) => Endless(mission) ? Loc.T(mission.Id == Hunt.Id ? "sn.hunt" : mission.TitleKey) + " " + EndlessLevel(mission) : Loc.T(mission.TitleKey);

        void StartRange() => ShowBriefing(Range.Make(_save.RangeLevel, _save.Ranges * 7 + _save.RangeLevel), false);

        void StartSafari() => ShowBriefing(Safari.Make(_save.SafariLevel, _save.Safaris * 11 + _save.SafariLevel), false);

        System.Action Again(MissionDef mission) => mission.Id == Range.Id ? StartRange : mission.Id == Safari.Id ? (System.Action)StartSafari : StartHunt;

        /// <summary>A contract made up on the spot, in a place the player has already worked.</summary>
        void StartHunt()
        {
            var places = new List<string>();
            foreach (MissionDef mission in Campaign.Missions)
                if ((_save.Record(mission.Id)?.Stars ?? 0) > 0 && !places.Contains(mission.LocationId)) places.Add(mission.LocationId);
            if (places.Count == 0) places.Add(Campaign.Missions[0].LocationId);
            string place = places[(_save.HuntLevel * 7 + _save.Hunts) % places.Count];
            MissionDef hunt = Hunt.Make(place, _save.HuntLevel, _save.Hunts * 13 + _save.HuntLevel);
            if (hunt == null) return;
            ShowBriefing(hunt, false);
        }

        void ShowSettings()
        {
            RectTransform s = Screen_("Settings", true);
            Backing(s);
            Words(s, Loc.T("sn.settings"), 64, new Vector2(0.5f, 1f), new Vector2(0f, -70f), new Vector2(1200f, 80f), TextAnchor.MiddleCenter, Color.white);
            float y = 190f;
            void Line(string label, string value, System.Action less, System.Action more)
            {
                Words(s, label, 32, new Vector2(0.5f, 0.5f), new Vector2(-330f, y), new Vector2(500f, 44f), TextAnchor.MiddleLeft, Color.white);
                Press(s, less != null ? "–" : value, UiKit.SolidPanelColor, less != null ? 44 : 30, new Vector2(0.5f, 0.5f), new Vector2(less != null ? 150f : 330f, y), less != null ? new Vector2(110f, 84f) : new Vector2(470f, 84f), less ?? more);
                if (less == null) return;
                Words(s, value, 32, new Vector2(0.5f, 0.5f), new Vector2(330f, y), new Vector2(240f, 44f), TextAnchor.MiddleCenter, UiKit.Cyan);
                Press(s, "+", UiKit.SolidPanelColor, 44, new Vector2(0.5f, 0.5f), new Vector2(510f, y), new Vector2(110f, 84f), more);
            }
            Line(Loc.T("sn.set.sense"), Mathf.RoundToInt(_save.Sensitivity * 100f) + "%", () => Sense(-0.1f), () => Sense(0.1f));
            y -= 110f;
            Line(Loc.T("sn.set.gyro"), Loc.T(_save.Gyro ? "sn.on" : "sn.off"), null, () =>
            {
                _save.Gyro = !_save.Gyro;
                _save.Store();
                ShowSettings();
            });
            y -= 110f;
            Line(Loc.T("sn.set.sound"), Loc.T(_save.Muted ? "sn.off" : "sn.on"), null, () =>
            {
                _save.Muted = !_save.Muted;
                AudioListener.volume = _save.Muted ? 0f : 1f;
                _save.Store();
                ShowSettings();
            });
            y -= 110f;
            Line(Loc.T("sn.set.language"), Loc.Language.ToUpperInvariant(), null, () =>
            {
                int index = System.Array.IndexOf(Loc.Languages, Loc.Language);
                _save.Language = Loc.Languages[(index + 1) % Loc.Languages.Length];
                Loc.SetLanguage(_save.Language);
                _save.Store();
                ShowSettings();
            });
            Press(s, Loc.T("sn.back"), UiKit.Accent, 34, new Vector2(0.5f, 0f), new Vector2(0f, 70f), new Vector2(360f, 96f), _phase == Phase.Paused ? (System.Action)Pause_ : ShowMenu);
        }

        /// <summary>A dark sheet behind a screen full of small print, so the place behind it does not read through.</summary>
        static void Backing(RectTransform screen)
        {
            Image sheet = UiKit.Box(screen, "Backing", new Color(0.02f, 0.03f, 0.05f, 0.93f));
            sheet.raycastTarget = false;
            sheet.rectTransform.anchorMin = Vector2.zero;
            sheet.rectTransform.anchorMax = Vector2.one;
            sheet.rectTransform.offsetMin = new Vector2(30f, 24f);
            sheet.rectTransform.offsetMax = new Vector2(-30f, -20f);
        }

        void Sense(float by)
        {
            _save.Sensitivity = Mathf.Clamp(Mathf.Round((_save.Sensitivity + by) * 10f) / 10f, 0.4f, 2.5f);
            _save.Store();
            ShowSettings();
        }

        /// <summary>The career in numbers, what has been earned for it, and whose work the game is built from.</summary>
        void ShowCareer()
        {
            RectTransform s = Screen_("Career", true);
            Backing(s);
            Words(s, Loc.T("sn.career"), 60, new Vector2(0f, 1f), new Vector2(70f, -36f), new Vector2(900f, 76f), TextAnchor.UpperLeft, Color.white);
            int accuracy = _save.Shots > 0 ? Mathf.Clamp(Mathf.RoundToInt(100f * _save.Kills / _save.Shots), 0, 100) : 0;
            string stats = Loc.T("sn.stat.contracts", _save.Completed, Campaign.Missions.Count) + "\n" + Loc.T("sn.stat.stars", _save.Stars, Campaign.Missions.Count * 3) + "\n" +
                Loc.T("sn.stat.kills", _save.Kills, _save.Headshots) + "\n" + Loc.T("sn.stat.accuracy", accuracy) + "\n" +
                Loc.T("sn.stat.far", Mathf.RoundToInt(_save.LongestShot)) + "\n" + Loc.T("sn.stat.hunts", _save.Hunts) + "\n" + Loc.T("sn.stat.days", _save.Streak);
            Words(s, stats, 30, new Vector2(0f, 1f), new Vector2(74f, -140f), new Vector2(760f, 330f), TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.9f));
            int row = 0;
            foreach (Feat feat in Feats.All)
            {
                bool done = _save.Feats.Contains(feat.Id);
                int have = Mathf.Min(feat.Goal, FeatValue(feat.Kind));
                var at = new Vector2(-70f - (row % 2) * 480f, -140f - (row / 2) * 62f);
                Words(s, (done ? "+  " : "") + Loc.T("sn.feat." + feat.Kind, feat.Goal) + (done ? "" : "   " + have + "/" + feat.Goal) + "   $" + feat.Cash.ToString("N0"), 24, new Vector2(1f, 1f), at, new Vector2(460f, 54f),
                    TextAnchor.MiddleLeft, done ? UiKit.Accent : new Color(1f, 1f, 1f, 0.75f));
                row++;
            }
            var credits = Resources.Load<TextAsset>("sniper_credits");
            Words(s, credits != null ? credits.text : "", 19, new Vector2(0f, 0f), new Vector2(74f, 60f), new Vector2(1380f, 330f), TextAnchor.LowerLeft, new Color(1f, 1f, 1f, 0.6f), true);
            Press(s, Loc.T("sn.back"), UiKit.Accent, 32, new Vector2(1f, 0f), new Vector2(-70f, 50f), new Vector2(300f, 96f), ShowMenu);
        }

        /// <summary>One contract a day pays double: today's is picked by the date among those already won.</summary>
        MissionDef DailyContract()
        {
            var won = new List<MissionDef>();
            foreach (MissionDef mission in Campaign.Missions)
                if ((_save.Record(mission.Id)?.Stars ?? 0) > 0) won.Add(mission);
            if (won.Count < 2) return null;
            int seed = 17;
            foreach (char c in Today) seed = seed * 31 + c;
            return won[Mathf.Abs(seed) % won.Count];
        }

        /// <summary>Pay for turning up: more for each day in a row, up to a week.</summary>
        void ShowDayPay()
        {
            string yesterday = System.DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
            _save.Streak = _save.DayStamp == yesterday ? Mathf.Min(7, _save.Streak + 1) : 1;
            _save.DayStamp = Today;
            int pay = 150 * _save.Streak;
            _save.Cash += pay;
            _save.Store();
            if (SkipIntro) return;
            Canvas canvas = UiKit.CreateCanvas(_screen.transform, "DayPay", 30);
            Image back = UiKit.Box(canvas.transform, "Dim", new Color(0.01f, 0.02f, 0.04f, 0.8f));
            UiKit.Stretch(back.rectTransform);
            Image panel = UiKit.Box(canvas.transform, "Panel", UiKit.ModalColor, UiKit.Chamfer);
            panel.type = Image.Type.Sliced;
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(760f, 460f));
            Words(panel.transform, Loc.T("sn.day", _save.Streak), 56, new Vector2(0.5f, 1f), new Vector2(0f, -50f), new Vector2(700f, 70f), TextAnchor.MiddleCenter, Color.white);
            Words(panel.transform, "+ $" + pay.ToString("N0"), 76, new Vector2(0.5f, 0.5f), new Vector2(0f, 20f), new Vector2(700f, 90f), TextAnchor.MiddleCenter, UiKit.Accent);
            Words(panel.transform, Loc.T("sn.day.note"), 26, new Vector2(0.5f, 0.5f), new Vector2(0f, -56f), new Vector2(700f, 36f), TextAnchor.MiddleCenter, new Color(1f, 1f, 1f, 0.7f));
            Press(panel.transform, Loc.T("sn.day.take"), UiKit.Accent, 36, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(340f, 96f), ShowMenu);
            _sfx.Play(Sound.Success, 0.7f);
        }

        void NextLanguage()
        {
            int index = System.Array.IndexOf(Loc.Languages, Loc.Language);
            _save.Language = Loc.Languages[(index + 1) % Loc.Languages.Length];
            Loc.SetLanguage(_save.Language);
            _save.Store();
            ShowMenu();
        }

        void ShowBriefing(MissionDef mission) => ShowBriefing(mission, false);

        void ShowBriefing(MissionDef mission, bool daily)
        {
            _phase = Phase.Briefing;
            _dailyRun = daily;
            LoadMission(mission);
            // The surveillance picture: the first target as the rifle will see him.
            int subject = -1;
            for (int i = 0; i < _sim.ActorCount && subject < 0; i++)
                if (_sim.Def(i).Role == Role.Target) subject = i;
            if (subject >= 0)
            {
                Vector3 chest = _sim.Actor(subject).Position + new Vector3(0f, _sim.Def(subject).Kind == Kind.Beast ? _sim.Def(subject).Shoulder * 0.8f : 1.25f, 0f);
                Vector3 line = chest - mission.Perch;
                Vector3 side = Vector3.Cross(Vector3.up, line).normalized;
                // Looked at a little to one side, so he stands in the right half of the screen, clear of the orders.
                _cam.transform.position = mission.Perch;
                _cam.transform.rotation = Quaternion.LookRotation(line - side * line.magnitude * 0.049f, Vector3.up);
                _cam.fieldOfView = 4.4f;
            }
            RectTransform s = Screen_("Briefing", false);
            Image shade = UiKit.Box(s, "Shade", new Color(0.01f, 0.02f, 0.04f, 0.35f));
            shade.raycastTarget = false;
            UiKit.Stretch(shade.rectTransform);
            Image panel = UiKit.Box(s, "Panel", UiKit.ModalColor, UiKit.Chamfer);
            panel.type = Image.Type.Sliced;
            UiKit.Place(panel.rectTransform, new Vector2(0f, 0.5f), new Vector2(60f, 0f), new Vector2(980f, 760f));
            Transform p = panel.transform;
            Words(p, (daily ? Loc.T("sn.daily") + "  ·  " : "") + (Endless(mission) ? Loc.T("sn.level", EndlessLevel(mission)) : Loc.T("sn.contract", mission.Order)) + "  ·  " + Loc.T(Campaign.Location(mission.LocationId).NameKey), 26, new Vector2(0f, 1f), new Vector2(46f, -40f), new Vector2(880f, 34f), TextAnchor.UpperLeft, daily ? UiKit.Accent : UiKit.Cyan);
            Words(p, MissionTitle(mission), 60, new Vector2(0f, 1f), new Vector2(46f, -78f), new Vector2(880f, 76f), TextAnchor.UpperLeft, Color.white);
            Words(p, Loc.T(mission.BriefKey), 29, new Vector2(0f, 1f), new Vector2(46f, -176f), new Vector2(880f, 240f), TextAnchor.UpperLeft, new Color(1f, 1f, 1f, 0.85f), true);
            int targets = 0, bounty = 0;
            foreach (ActorDef actor in mission.Actors)
            {
                if (actor.Role != Role.Target) continue;
                targets++;
                bounty += actor.Bounty;
            }
            RifleDef rifle = _save.Fitted();
            string lines = Loc.T("sn.brief.targets", targets) + "   ·   " + Loc.T("sn.brief.pay", mission.Reward + bounty) + "\n" +
                Loc.T("sn.brief.rounds", mission.Rounds) + "   ·   " + Loc.T(rifle.NameKey) + (rifle.Suppressed ? "  (" + Loc.T("sn.suppressed") + ")" : "") + "\n" +
                Loc.T("sn.brief.star2." + mission.Second) + "\n" + Loc.T("sn.brief.star3", Mathf.RoundToInt(mission.Par));
            Words(p, lines, 27, new Vector2(0f, 0f), new Vector2(46f, 156f), new Vector2(880f, 170f), TextAnchor.LowerLeft, UiKit.Accent);
            Press(p, Loc.T("sn.start"), UiKit.Accent, 40, new Vector2(1f, 0f), new Vector2(-46f, 40f), new Vector2(380f, 96f), StartMission);
            Press(p, Loc.T("sn.back"), UiKit.SolidPanelColor, 32, new Vector2(0f, 0f), new Vector2(46f, 40f), new Vector2(240f, 96f), ShowMenu);
            Press(p, Loc.T(_save.Pro ? "sn.pro.on" : "sn.pro.off"), _save.Pro ? UiKit.Bad : UiKit.SolidPanelColor, 24, new Vector2(0f, 0f), new Vector2(300f, 40f), new Vector2(250f, 96f), () =>
            {
                _save.Pro = !_save.Pro;
                _save.Store();
                ShowBriefing(mission, daily);
            });

            if (subject < 0) return;
            ActorDef who = _sim.Def(subject);
            Image frame = UiKit.Box(s, "Frame", new Color(1f, 0.35f, 0.25f, 0.9f), UiKit.Brackets);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            UiKit.Place(frame.rectTransform, new Vector2(1f, 0.5f), new Vector2(-150f, 20f), new Vector2(520f, 640f));
            Words(s, Loc.T("sn.brief.subject"), 26, new Vector2(1f, 0.5f), new Vector2(-150f, 366f), new Vector2(520f, 34f), TextAnchor.MiddleLeft, new Color(1f, 0.35f, 0.25f));
            Words(s, Loc.T(who.NameKey).ToUpperInvariant(), 44, new Vector2(1f, 0.5f), new Vector2(-150f, -336f), new Vector2(520f, 54f), TextAnchor.MiddleLeft, Color.white);
            Words(s, Loc.T(Feats.LookKey(who.Model)) + (who.Health > 150f ? "  ·  " + Loc.T("sn.look.armour") : ""), 26, new Vector2(1f, 0.5f), new Vector2(-150f, -382f), new Vector2(520f, 36f), TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.85f));
            if (who.Bounty > 0) Words(s, "$" + who.Bounty.ToString("N0"), 34, new Vector2(1f, 0.5f), new Vector2(-150f, -424f), new Vector2(520f, 44f), TextAnchor.MiddleLeft, UiKit.Accent);
        }

        void StartMission()
        {
            Clear();
            _music.Level = 0.14f;
            LoadMission(_mission);
            _phase = Phase.Play;
            _hud.Visible = true;
            _music.Battle(_mission.Order);
            SetZoom(1);
            _hud.Message(MissionTitle(_mission), _save.Completed > 0 ? Loc.T("sn.tip." + (1 + (_save.Shots + _mission.Order) % 6)) : Loc.T("sn.objective", _sim.TargetsLeft), Color.white);
        }

        void Pause()
        {
            if (_phase != Phase.Play || _following) return;
            _phase = Phase.Paused;
            Time.timeScale = 0f;
            RectTransform s = Screen_("Pause", true);
            Words(s, Loc.T("sn.paused"), 72, new Vector2(0.5f, 0.5f), new Vector2(0f, 220f), new Vector2(1200f, 90f), TextAnchor.MiddleCenter, Color.white);
            Press(s, Loc.T("sn.resume"), UiKit.Accent, 38, new Vector2(0.5f, 0.5f), new Vector2(0f, 70f), new Vector2(460f, 100f), () =>
            {
                Clear();
                Time.timeScale = 1f;
                _phase = Phase.Play;
            });
            Press(s, Loc.T("sn.retry"), UiKit.SolidPanelColor, 34, new Vector2(0.5f, 0.5f), new Vector2(0f, -50f), new Vector2(460f, 100f), StartMission);
            Press(s, Loc.T("sn.settings"), UiKit.SolidPanelColor, 34, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(460f, 100f), ShowSettings);
            Press(s, Loc.T("sn.menu"), UiKit.SolidPanelColor, 34, new Vector2(0.5f, 0.5f), new Vector2(0f, -290f), new Vector2(460f, 100f), ShowMenu);
        }

        void Pause_()
        {
            _phase = Phase.Play;
            Pause();
        }

        bool _dailyPaid;
        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        void Finish()
        {
            _phase = Phase.Result;
            _dailyPaid = false;
            if (_mission.Id != Range.Id && _mission.Id != Safari.Id) _menuPlace = _mission.LocationId;
            Time.timeScale = 1f;
            _hud.Visible = false;
            _result = _sim.Grade();
            if (_result.Won)
            {
                MissionRecord record = _save.Record(_mission.Id);
                if (record == null)
                {
                    record = new MissionRecord { Id = _mission.Id };
                    _save.Missions.Add(record);
                }
                record.Stars = Mathf.Max(record.Stars, _result.Stars);
                if (record.Best <= 0f || _result.Seconds < record.Best) record.Best = _result.Seconds;
                _result.Cash += _streakBonus;
                if (_save.Pro) _result.Cash = Mathf.RoundToInt(_result.Cash * 1.5f);
                if (Endless(_mission))
                {
                    _save.Missions.Remove(record);
                    if (_mission.Id == Range.Id) { _save.Ranges++; _save.RangeLevel++; }
                    else if (_mission.Id == Safari.Id) { _save.Safaris++; _save.SafariLevel++; }
                    else { _save.Hunts++; _save.HuntLevel++; }
                }
                if (_dailyRun && _save.DailyDone != Today)
                {
                    _result.Cash *= 2;
                    _save.DailyDone = Today;
                    _dailyPaid = true;
                }
                _save.Cash += _result.Cash;
            }
            _doubled = false;
            _save.Finished++;
            // A break every third contract, never in the first three.
            _breakDue = _save.Completed >= 3 && _save.Finished % 3 == 0;
            _save.Kills += _result.Kills;
            _save.Headshots += _result.Headshots;
            _save.Shots += _result.Shots;
            _save.Blasts += _sim.Blasts;
            _news.Clear();
            foreach (Feat feat in Feats.All)
            {
                if (_save.Feats.Contains(feat.Id) || FeatValue(feat.Kind) < feat.Goal) continue;
                _save.Feats.Add(feat.Id);
                _save.Cash += feat.Cash;
                _news.Add(Loc.T("sn.feat.done", Loc.T("sn.feat." + feat.Kind, feat.Goal), feat.Cash));
            }
            _save.Store();
            _sfx.Play(_result.Won ? Sound.Success : Sound.Crash, 0.8f);
            ShowResult();
        }

        int FeatValue(string kind)
        {
            switch (kind)
            {
                case "kills": return _save.Kills;
                case "heads": return _save.Headshots;
                case "far": return Mathf.RoundToInt(_save.LongestShot);
                case "stars": return _save.Stars;
                case "blasts": return _save.Blasts;
                case "ranges": return _save.Ranges;
                case "safaris": return _save.Safaris;
                default: return _save.Hunts;
            }
        }

        void ShowResult()
        {
            MissionResult r = _result;
            RectTransform s = Screen_("Result", true);
            Image panel = UiKit.Box(s, "Panel", UiKit.ModalColor, UiKit.Chamfer);
            panel.type = Image.Type.Sliced;
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 800f));
            Transform p = panel.transform;
            Words(p, Loc.T(r.Won ? "sn.won" : "sn.lost"), 76, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 90f), TextAnchor.UpperCenter, r.Won ? UiKit.Accent : UiKit.Bad);
            Words(p, r.Won ? MissionTitle(_mission) : Loc.T(WhyKey(r.Why)), 30, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 40f), TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.8f));
            if (r.Won) Stars(p, r.Stars, new Vector2(0.5f, 1f), new Vector2(0f, -250f), 96f);
            string lines = Loc.T("sn.result.kills", r.Kills, r.Headshots) + "\n" + Loc.T("sn.result.shots", r.Shots, Mathf.RoundToInt(r.Seconds));
            if (r.Won)
                lines += "\n" + (r.SecondMet ? "+  " : "–  ") + Loc.T("sn.brief.star2." + _mission.Second) + "\n" + (r.ThirdMet ? "+  " : "–  ") + Loc.T("sn.brief.star3", Mathf.RoundToInt(_mission.Par));
            if (r.Won && _save.Pro) lines += "\n" + Loc.T("sn.pro.paid");
            foreach (string line in _news) lines += "\n" + line;
            Words(p, lines, _news.Count > 1 ? 24 : 27, new Vector2(0.5f, 0.5f), new Vector2(0f, 10f), new Vector2(880f, 230f), TextAnchor.MiddleCenter, Color.white);
            if (r.Won && _dailyPaid) Words(p, Loc.T("sn.daily.paid"), 26, new Vector2(0.5f, 0f), new Vector2(0f, 312f), new Vector2(860f, 34f), TextAnchor.MiddleCenter, UiKit.Cyan);
            if (r.Won) Words(p, "+ $" + r.Cash.ToString("N0"), 56, new Vector2(0.5f, 0f), new Vector2(0f, 240f), new Vector2(860f, 70f), TextAnchor.MiddleCenter, UiKit.Accent);
            MissionDef next = null;
            foreach (MissionDef mission in Campaign.Missions)
                if (mission.Order == _mission.Order + 1) next = mission;
            if (Endless(_mission)) next = null;
            if (r.Won && Endless(_mission)) Press(p, Loc.T("sn.next"), UiKit.Accent, 36, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 96f), () => Leave(Again(_mission)));
            if (r.Won && !_doubled && _ads.RewardedReady)
                Press(p, Loc.T("sn.ad.double", r.Cash.ToString("N0")), UiKit.Good, 26, new Vector2(0.5f, 0f), new Vector2(0f, 150f), new Vector2(520f, 76f), () => _ads.ShowRewarded(watched =>
                {
                    if (!watched || _doubled) return;
                    _doubled = true;
                    _save.Cash += r.Cash;
                    r.Cash *= 2;
                    _save.Store();
                    ShowResult();
                }));
            if (r.Won && next != null) Press(p, Loc.T("sn.next"), UiKit.Accent, 36, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 96f), () => Leave(() => ShowBriefing(next)));
            Press(p, Loc.T("sn.retry"), r.Won && next != null ? UiKit.SolidPanelColor : UiKit.Accent, 32, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(280f, 96f), () => Leave(() => { _dailyRun = false; StartMission(); }));
            Press(p, Loc.T("sn.menu"), UiKit.SolidPanelColor, 32, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(280f, 96f), () => Leave(ShowMenu));
        }

        /// <summary>Why it was lost, in the words of the place: a blue board is not a bystander, and a hind is neither.</summary>
        string WhyKey(LostWhy why)
        {
            if (_mission.Id == Range.Id && why == LostWhy.Civilian) return "sn.why.friend";
            if (_mission.Id == Safari.Id && why == LostWhy.Civilian) return "sn.why.hind";
            if (_mission.Id == Safari.Id && why == LostWhy.AllyDown) return "sn.why.herd";
            return "sn.why." + why.ToString().ToLowerInvariant();
        }

        /// <summary>Out of the result screen, by way of an advertising break when one is due.</summary>
        void Leave(System.Action then)
        {
            if (!_breakDue)
            {
                then();
                return;
            }
            _breakDue = false;
            _ads.ShowInterstitial(then);
        }

        void ShowArmory()
        {
            _phase = Phase.Armory;
            RectTransform s = Screen_("Armory", true);
            Words(s, Loc.T("sn.armory"), 64, new Vector2(0f, 1f), new Vector2(70f, -40f), new Vector2(900f, 80f), TextAnchor.UpperLeft, Color.white);
            Words(s, "$ " + _save.Cash.ToString("N0"), 44, new Vector2(1f, 1f), new Vector2(-70f, -50f), new Vector2(600f, 60f), TextAnchor.UpperRight, UiKit.Accent);
            int row = 0;
            foreach (RifleDef rifle in Armory.All)
            {
                RifleDef picked = rifle;
                RifleRecord owned = _save.Owned(rifle.Id);
                bool inHand = _save.Rifle == rifle.Id, open = _save.Completed >= rifle.MissionsRequired;
                var at = new Vector2(70f, -140f - row * 98f);
                Image line = UiKit.Box(s, "Rifle", inHand ? new Color(0.10f, 0.22f, 0.30f, 0.9f) : UiKit.SolidPanelColor, UiKit.Chamfer);
                line.type = Image.Type.Sliced;
                UiKit.Place(line.rectTransform, new Vector2(0f, 1f), at, new Vector2(1040f, 90f));
                Words(line.transform, Loc.T(rifle.NameKey), 32, new Vector2(0f, 0.5f), new Vector2(24f, 16f), new Vector2(500f, 40f), TextAnchor.MiddleLeft, Color.white);
                Words(line.transform, Loc.T("sn.rifle.stats", Mathf.RoundToInt(rifle.Damage), Mathf.RoundToInt(rifle.Zoom), rifle.Magazine) + (rifle.Suppressed ? "  ·  " + Loc.T("sn.suppressed") : "") + (rifle.Piercing ? "  ·  " + Loc.T("sn.piercing") : ""),
                    22, new Vector2(0f, 0.5f), new Vector2(24f, -20f), new Vector2(700f, 30f), TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));
                string label = inHand ? Loc.T("sn.inhand") : owned != null ? Loc.T("sn.take") : !open ? Loc.T("sn.need", rifle.MissionsRequired) : "$" + rifle.Price.ToString("N0");
                Color color = inHand ? UiKit.Faint : owned != null ? UiKit.Lit : open && _save.Cash >= rifle.Price ? UiKit.Accent : UiKit.Faint;
                Press(line.transform, label, color, 26, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(280f, 66f), () => TakeRifle(picked));
                row++;
            }

            RifleRecord hand = _save.Owned(_save.Rifle);
            RifleDef b = Armory.Get(_save.Rifle);
            Words(s, Loc.T("sn.fit", Loc.T(b.NameKey)), 30, new Vector2(1f, 1f), new Vector2(-70f, -150f), new Vector2(640f, 40f), TextAnchor.UpperRight, UiKit.Cyan);
            UpgradeKind[] kinds = { UpgradeKind.Scope, UpgradeKind.Barrel, UpgradeKind.Stock, UpgradeKind.Magazine, UpgradeKind.Bolt };
            for (int i = 0; i < kinds.Length; i++)
            {
                UpgradeKind kind = kinds[i];
                int level = Level(hand, kind), price = Armory.UpgradePrice(b, level);
                var at = new Vector2(-70f, -200f - i * 104f);
                Image line = UiKit.Box(s, "Upgrade", UiKit.SolidPanelColor, UiKit.Chamfer);
                line.type = Image.Type.Sliced;
                UiKit.Place(line.rectTransform, new Vector2(1f, 1f), at, new Vector2(640f, 92f));
                Words(line.transform, Loc.T("sn.up." + kind.ToString().ToLowerInvariant()) + "   " + level + "/" + Armory.MaxLevel, 28, new Vector2(0f, 0.5f), new Vector2(22f, 14f), new Vector2(380f, 36f), TextAnchor.MiddleLeft, Color.white);
                Words(line.transform, Loc.T("sn.up." + kind.ToString().ToLowerInvariant() + ".note"), 20, new Vector2(0f, 0.5f), new Vector2(22f, -20f), new Vector2(380f, 28f), TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.65f));
                Press(line.transform, price > 0 ? "$" + price.ToString("N0") : Loc.T("sn.max"), price > 0 && _save.Cash >= price ? UiKit.Accent : UiKit.Faint, 26,
                    new Vector2(1f, 0.5f), new Vector2(-14f, 0f), new Vector2(210f, 66f), () => Upgrade(kind));
            }
            Press(s, Loc.T("sn.back"), UiKit.SolidPanelColor, 32, new Vector2(1f, 0f), new Vector2(-70f, 50f), new Vector2(300f, 96f), ShowMenu);
        }

        static int Level(RifleRecord r, UpgradeKind kind)
        {
            if (r == null) return 0;
            switch (kind)
            {
                case UpgradeKind.Scope: return r.Scope;
                case UpgradeKind.Barrel: return r.Barrel;
                case UpgradeKind.Stock: return r.Stock;
                case UpgradeKind.Magazine: return r.Magazine;
                default: return r.Bolt;
            }
        }

        void TakeRifle(RifleDef rifle)
        {
            if (_save.Owned(rifle.Id) == null)
            {
                if (_save.Completed < rifle.MissionsRequired || _save.Cash < rifle.Price)
                {
                    _sfx.Play(Sound.Warning, 0.5f);
                    return;
                }
                _save.Cash -= rifle.Price;
                _save.Rifles.Add(new RifleRecord { Id = rifle.Id });
                _sfx.Play(Sound.Success, 0.7f);
            }
            _save.Rifle = rifle.Id;
            _save.Store();
            ShowArmory();
        }

        void Upgrade(UpgradeKind kind)
        {
            RifleRecord r = _save.Owned(_save.Rifle);
            int price = Armory.UpgradePrice(Armory.Get(_save.Rifle), Level(r, kind));
            if (r == null || price <= 0 || _save.Cash < price)
            {
                _sfx.Play(Sound.Warning, 0.5f);
                return;
            }
            _save.Cash -= price;
            switch (kind)
            {
                case UpgradeKind.Scope: r.Scope++; break;
                case UpgradeKind.Barrel: r.Barrel++; break;
                case UpgradeKind.Stock: r.Stock++; break;
                case UpgradeKind.Magazine: r.Magazine++; break;
                default: r.Bolt++; break;
            }
            _sfx.Play(Sound.Success, 0.7f);
            _save.Store();
            ShowArmory();
        }

        // ---------------------------------------------------------------- for the screenshot run

        public void AutomationMenu(string place = null)
        {
            if (place != null) _menuPlace = place;
            ShowMenu();
        }

        public void AutomationWorld(string missionId) => LoadMission(Campaign.Mission(missionId));
        public Vector3 AutomationPerch => _mission.Perch;
        public void AutomationBriefing(string id) => ShowBriefing(Campaign.Mission(id));

        public void AutomationPlay(string id)
        {
            _mission = Campaign.Mission(id);
            StartMission();
        }

        public void AutomationZoom(int index) => SetZoom(index);

        /// <summary>Lays the cross on a point, with the hold-over the range asks for.</summary>
        public void AutomationAim(Vector3 point, bool holdOver)
        {
            Vector3 from = _mission.Perch;
            if (holdOver)
            {
                float time = (point - from).magnitude / _rifle.MuzzleSpeed;
                point += Vector3.up * (0.5f * SniperSim.Gravity * time * time) - _mission.Wind * (0.5f * SniperSim.WindPush * time * time);
            }
            Vector3 to = point - from;
            _yaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            _pitch = -Mathf.Atan2(to.y, new Vector2(to.x, to.z).magnitude) * Mathf.Rad2Deg;
            _holding = true;
            _breath = 1f;
            _swayClock = 0f;
        }

        public Vector3 AutomationHead(int actor) => _sim.Actor(actor).Position + new Vector3(0f, SniperSim.HeadHeight, 0f);
        public void AutomationFire() => _firePressed = true;
        public void AutomationArmory() => ShowArmory();
        public void AutomationCareer() => ShowCareer();
        public void AutomationSettings() => ShowSettings();
        public void AutomationHunt() { _save.HuntLevel = 6; StartHunt(); }
        public void AutomationRange(int level, bool play) { _save.RangeLevel = level; StartRange(); if (play) StartMission(); }
        public void AutomationSafari(int level, bool play) { _save.SafariLevel = level; StartSafari(); if (play) StartMission(); }
        public SniperSplash AutomationSplash(float at)
        {
            SniperSplash splash = SniperSplash.Create(transform, _sfx);
            splash.Freeze(at);
            return splash;
        }
        public void AutomationWin(int contracts)
        {
            for (int i = 0; i < contracts && i < Campaign.Missions.Count; i++)
                if (_save.Record(Campaign.Missions[i].Id) == null) _save.Missions.Add(new MissionRecord { Id = Campaign.Missions[i].Id, Stars = 2 });
        }
        public void AutomationCash(int cash) { _save.Cash = cash; }
        public void AutomationLanguage(string code) { Loc.SetLanguage(code); }
        public bool AutomationPlaying => _phase == Phase.Play;
        public void AutomationLook(Vector3 from, Vector3 at, float fov)
        {
            _phase = Phase.Menu;
            Clear();
            _hud.Visible = false;
            _cam.transform.position = from;
            _cam.transform.rotation = Quaternion.LookRotation(at - from, Vector3.up);
            _cam.fieldOfView = fov;
        }
    }
}
