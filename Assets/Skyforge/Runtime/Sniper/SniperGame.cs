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
            _effects = new Effects(transform);
            UiKit.Tap = () => _sfx.Play(Sound.Tap, 0.7f);
            AudioListener.volume = _save.Muted ? 0f : 1f;
            _audio = new SniperAudio(transform);
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
            string weather = location.Light == "dusk" ? "sunset" : location.Light == "night" ? "night" : location.Light == "overcast" ? "overcast" : WeatherCatalog.Clear;
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
            if (drag.sqrMagnitude <= 0f) return;
            // A finger's width moves the cross the same distance on the glass at any magnification.
            float degreesPerPixel = _cam.fieldOfView / Mathf.Max(1, Screen.height) * 1.15f;
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
                    _effects.Blood(e.Position, e.Position - _mission.Perch);
                    _audio.Play(e.Zone == Zone.Head ? "hit_head" : "hit_body", 0.9f);
                    _audio.PlayLater("body_fall", 0.55f, 0.6f);
                    if (def.Role == Role.Civilian)
                    {
                        _hud.Message(Loc.T("sn.civilian"), Loc.T("sn.civilian.sub"), UiKit.Bad);
                        _sfx.Play(Sound.Warning);
                        break;
                    }
                    bool head = e.Zone == Zone.Head;
                    string name = string.IsNullOrEmpty(def.NameKey) ? "" : Loc.T(def.NameKey);
                    _hud.Message(Loc.T(head ? "sn.headshot" : "sn.down"), def.Bounty > 0 ? name + "   +$" + (def.Bounty + (head ? 60 : 0)) : name, head ? UiKit.Accent : Color.white);
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
                case SimEventType.Miss:
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
                // Targets are named for the shooter; anyone else has to be looked at to be told apart.
                bool aimed = off < Mathf.Max(0.5f, _cam.fieldOfView * 0.1f);
                if (def.Role == Role.Target)
                    _hud.Tag(used++, screen, "<color=#ff5a40>" + Loc.T(def.NameKey).ToUpperInvariant() + "</color>" + (def.Bounty > 0 && aimed ? "\n<size=20>$" + def.Bounty + "</size>" : ""));
                else if (aimed && scoped)
                    _hud.Tag(used++, screen, def.Role == Role.Civilian ? "<color=#6fe08a>" + Loc.T("sn.tag.civilian") + "</color>" : "<color=#ffb040>" + Loc.T("sn.tag.guard") + "</color>");
            }
            _hud.EndTags(used);
            _hud.SetHint(Lesson());
            Vector3 wind = _mission.Wind;
            _hud.SetData(Loc.T("sn.data", Mathf.RoundToInt(Mathf.Min(range, 999f)), wind.magnitude.ToString("0.0"), wind.magnitude < 0.1f ? "" : wind.x > 0f ? ">>" : "<<"));
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
                Press(s, Loc.T(location.NameKey).ToUpperInvariant(), id == _menuPlace ? UiKit.Lit : UiKit.SolidPanelColor, 28, new Vector2(0f, 0f), new Vector2(70f + tab * 330f, 520f), new Vector2(316f, 74f), () =>
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
            Press(s, Loc.Language.ToUpperInvariant(), UiKit.SolidPanelColor, 28, new Vector2(1f, 0f), new Vector2(-450f, 60f), new Vector2(120f, 96f), NextLanguage);
            Press(s, _save.Muted ? Loc.T("sn.sound.off") : Loc.T("sn.sound.on"), UiKit.SolidPanelColor, 26, new Vector2(1f, 0f), new Vector2(-590f, 60f), new Vector2(220f, 96f), () =>
            {
                _save.Muted = !_save.Muted;
                AudioListener.volume = _save.Muted ? 0f : 1f;
                _save.Store();
                ShowMenu();
            });
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
                Vector3 chest = _sim.Actor(subject).Position + new Vector3(0f, 1.25f, 0f);
                Vector3 line = chest - mission.Perch;
                Vector3 side = Vector3.Cross(Vector3.up, line).normalized;
                // Looked at a little to one side, so he stands in the right half of the screen, clear of the orders.
                _cam.transform.position = mission.Perch;
                _cam.transform.rotation = Quaternion.LookRotation(line - side * line.magnitude * 0.019f, Vector3.up);
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
            Words(p, (daily ? Loc.T("sn.daily") + "  ·  " : "") + Loc.T("sn.contract", mission.Order) + "  ·  " + Loc.T(Campaign.Location(mission.LocationId).NameKey), 26, new Vector2(0f, 1f), new Vector2(46f, -40f), new Vector2(880f, 34f), TextAnchor.UpperLeft, daily ? UiKit.Accent : UiKit.Cyan);
            Words(p, Loc.T(mission.TitleKey), 60, new Vector2(0f, 1f), new Vector2(46f, -78f), new Vector2(880f, 76f), TextAnchor.UpperLeft, Color.white);
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
            Press(p, Loc.T("sn.back"), UiKit.SolidPanelColor, 32, new Vector2(0f, 0f), new Vector2(46f, 40f), new Vector2(260f, 96f), ShowMenu);

            if (subject < 0) return;
            ActorDef who = _sim.Def(subject);
            Image frame = UiKit.Box(s, "Frame", new Color(1f, 0.35f, 0.25f, 0.9f), UiKit.Brackets);
            frame.type = Image.Type.Sliced;
            frame.raycastTarget = false;
            UiKit.Place(frame.rectTransform, new Vector2(1f, 0.5f), new Vector2(-150f, 20f), new Vector2(520f, 640f));
            Words(s, Loc.T("sn.brief.subject"), 26, new Vector2(1f, 0.5f), new Vector2(-150f, 366f), new Vector2(520f, 34f), TextAnchor.MiddleLeft, new Color(1f, 0.35f, 0.25f));
            Words(s, Loc.T(who.NameKey).ToUpperInvariant(), 44, new Vector2(1f, 0.5f), new Vector2(-150f, -336f), new Vector2(520f, 54f), TextAnchor.MiddleLeft, Color.white);
            if (who.Bounty > 0) Words(s, "$" + who.Bounty.ToString("N0"), 34, new Vector2(1f, 0.5f), new Vector2(-150f, -386f), new Vector2(520f, 44f), TextAnchor.MiddleLeft, UiKit.Accent);
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
            _hud.Message(Loc.T(_mission.TitleKey), Loc.T("sn.objective", _sim.TargetsLeft), Color.white);
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
            Press(s, Loc.T("sn.menu"), UiKit.SolidPanelColor, 34, new Vector2(0.5f, 0.5f), new Vector2(0f, -170f), new Vector2(460f, 100f), ShowMenu);
        }

        bool _dailyPaid;
        static string Today => System.DateTime.Now.ToString("yyyy-MM-dd");

        void Finish()
        {
            _phase = Phase.Result;
            _dailyPaid = false;
            _menuPlace = _mission.LocationId;
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
                if (_dailyRun && _save.DailyDone != Today)
                {
                    _result.Cash *= 2;
                    _save.DailyDone = Today;
                    _dailyPaid = true;
                }
                _save.Cash += _result.Cash;
            }
            _save.Kills += _result.Kills;
            _save.Headshots += _result.Headshots;
            _save.Store();
            _sfx.Play(_result.Won ? Sound.Success : Sound.Crash, 0.8f);
            ShowResult();
        }

        void ShowResult()
        {
            MissionResult r = _result;
            RectTransform s = Screen_("Result", true);
            Image panel = UiKit.Box(s, "Panel", UiKit.ModalColor, UiKit.Chamfer);
            panel.type = Image.Type.Sliced;
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(980f, 720f));
            Transform p = panel.transform;
            Words(p, Loc.T(r.Won ? "sn.won" : "sn.lost"), 76, new Vector2(0.5f, 1f), new Vector2(0f, -40f), new Vector2(900f, 90f), TextAnchor.UpperCenter, r.Won ? UiKit.Accent : UiKit.Bad);
            Words(p, r.Won ? Loc.T(_mission.TitleKey) : Loc.T("sn.why." + r.Why.ToString().ToLowerInvariant()), 30, new Vector2(0.5f, 1f), new Vector2(0f, -140f), new Vector2(900f, 40f), TextAnchor.UpperCenter, new Color(1f, 1f, 1f, 0.8f));
            if (r.Won) Stars(p, r.Stars, new Vector2(0.5f, 1f), new Vector2(0f, -250f), 96f);
            string lines = Loc.T("sn.result.kills", r.Kills, r.Headshots) + "\n" + Loc.T("sn.result.shots", r.Shots, Mathf.RoundToInt(r.Seconds));
            if (r.Won)
                lines += "\n" + (r.SecondMet ? "+  " : "–  ") + Loc.T("sn.brief.star2." + _mission.Second) + "\n" + (r.ThirdMet ? "+  " : "–  ") + Loc.T("sn.brief.star3", Mathf.RoundToInt(_mission.Par));
            Words(p, lines, 28, new Vector2(0.5f, 0.5f), new Vector2(0f, -40f), new Vector2(860f, 200f), TextAnchor.MiddleCenter, Color.white);
            if (r.Won && _dailyPaid) Words(p, Loc.T("sn.daily.paid"), 26, new Vector2(0.5f, 0f), new Vector2(0f, 236f), new Vector2(860f, 34f), TextAnchor.MiddleCenter, UiKit.Cyan);
            if (r.Won) Words(p, "+ $" + r.Cash.ToString("N0"), 56, new Vector2(0.5f, 0f), new Vector2(0f, 170f), new Vector2(860f, 70f), TextAnchor.MiddleCenter, UiKit.Accent);
            MissionDef next = null;
            foreach (MissionDef mission in Campaign.Missions)
                if (mission.Order == _mission.Order + 1) next = mission;
            if (r.Won && next != null) Press(p, Loc.T("sn.next"), UiKit.Accent, 36, new Vector2(1f, 0f), new Vector2(-40f, 40f), new Vector2(300f, 96f), () => ShowBriefing(next));
            Press(p, Loc.T("sn.retry"), r.Won && next != null ? UiKit.SolidPanelColor : UiKit.Accent, 32, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(280f, 96f), () => { _dailyRun = false; StartMission(); });
            Press(p, Loc.T("sn.menu"), UiKit.SolidPanelColor, 32, new Vector2(0f, 0f), new Vector2(40f, 40f), new Vector2(280f, 96f), ShowMenu);
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
                var at = new Vector2(70f, -150f - row * 112f);
                Image line = UiKit.Box(s, "Rifle", inHand ? new Color(0.10f, 0.22f, 0.30f, 0.9f) : UiKit.SolidPanelColor, UiKit.Chamfer);
                line.type = Image.Type.Sliced;
                UiKit.Place(line.rectTransform, new Vector2(0f, 1f), at, new Vector2(1040f, 100f));
                Words(line.transform, Loc.T(rifle.NameKey), 34, new Vector2(0f, 0.5f), new Vector2(24f, 18f), new Vector2(500f, 40f), TextAnchor.MiddleLeft, Color.white);
                Words(line.transform, Loc.T("sn.rifle.stats", Mathf.RoundToInt(rifle.Damage), Mathf.RoundToInt(rifle.Zoom), rifle.Magazine) + (rifle.Suppressed ? "  ·  " + Loc.T("sn.suppressed") : "") + (rifle.Piercing ? "  ·  " + Loc.T("sn.piercing") : ""),
                    22, new Vector2(0f, 0.5f), new Vector2(24f, -22f), new Vector2(700f, 30f), TextAnchor.MiddleLeft, new Color(1f, 1f, 1f, 0.7f));
                string label = inHand ? Loc.T("sn.inhand") : owned != null ? Loc.T("sn.take") : !open ? Loc.T("sn.need", rifle.MissionsRequired) : "$" + rifle.Price.ToString("N0");
                Color color = inHand ? UiKit.Faint : owned != null ? UiKit.Lit : open && _save.Cash >= rifle.Price ? UiKit.Accent : UiKit.Faint;
                Press(line.transform, label, color, 26, new Vector2(1f, 0.5f), new Vector2(-16f, 0f), new Vector2(280f, 72f), () => TakeRifle(picked));
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
