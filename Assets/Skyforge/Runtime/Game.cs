using System;
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
    public sealed partial class Game : MonoBehaviour
    {
        enum Phase
        {
            Menu,
            Mission,
            FreeFlight,
            Result,
            Hangar,
            Globe,
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
        GlobeView _globe;
        GlobeScreen _globeScreen;
        TalkScreen _talk;
        DailyScreen _daily;
        AchievementsScreen _achievements;
        CachesView _caches;
        UnityEngine.UI.Text _toast;
        float _toastTimer;
        string _today;
        List<DailyOp> _dailyOps = new List<DailyOp>();
        int _dailySlot = -1;

        // An ad SDK and an analytics service plug in here; until then nothing is shown and nothing is sent.
        IAdsProvider _ads = new NoAds();
        IAnalytics _analytics = new LogAnalytics();
        MissionOutcome _lastOutcome;
        string _lastExtra;
        float _lastInterstitial = -100000f;
        int _globeIndex;
        Transform _regionRoot;
        RegionDefinition _region;
        Vector2 _touchStart;
        bool _touchMoved;
        DroneAudio _audio;
        Sfx _sfx;
        Music _music;
        SceneLook _look;
        Weather _weather;

        SaveStore _store;
        SaveData _save;
        List<MissionDefinition> _missions;
        int _missionIndex;
        MissionDefinition _mission;
        MissionRun _run;
        float _zoom = 1f;
        bool _actionPressed, _bombPressed;
        bool _autoLand;
        bool _abilityPressed, _supportPressed;
        /// <summary>Seconds flown fast and close to the ground in this mission, and the best landing made in it (0..3).</summary>
        float _lowSeconds;
        int _landingGrade;
        Vector2 _guide;
        /// <summary>The campaign is being flown at veteran difficulty.</summary>
        bool _veteran;
        /// <summary>The sector an operation is being fought for, or -1; the tower floor being climbed, or 0.</summary>
        int _operationSector = -1, _towerFloor;
        /// <summary>An aircraft being tried on the firing range without being owned, or null.</summary>
        string _trialDrone;
        /// <summary>Real seconds left of the camera's look at a decisive kill, and where it happened.</summary>
        float _killCam;
        Vector3 _killPoint;
        bool _photo, _radioedScatter;
        /// <summary>Closes whatever screens of the headquarters are open; set when they are built.</summary>
        Action _hideOperations;
        float _photoYaw, _photoPitch = 18f, _photoDistance = 9f, _hapticClock;
        int _shownSpecialStamp = -1;
        /// <summary>Seconds left of the fly-past that opens a mission; the drone waits on the pad meanwhile.</summary>
        float _introTimer;
        Vector3 _introPoint;
        /// <summary>Real seconds left of the slow motion that follows a decisive kill.</summary>
        float _slowMotion;
        float _radioTimer;
        bool _radioedHull, _radioedLock;
        readonly List<Vector4> _secondaries = new List<Vector4>();
        int _shownGunStamp = -1;
        const float AutoLandRange = 70f;
        Effects _effects;
        /// <summary>The freeze in progress ends with the next strike drone on the pad, not with the result.</summary>
        bool _respawnAfterFreeze;
        float _gunSoundClock, _noticeClock;
        int _shownAmmoStamp = -1, _lastLock = -1;
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
            Loc.SetLanguage(string.IsNullOrEmpty(_save.Language) ? DeviceLanguage() : _save.Language);
            _save.Sessions++;
            if (string.IsNullOrEmpty(_save.FirstDate)) _save.FirstDate = Daily.DateKey(DateTime.Now);
            _analytics.Event("session_start", null, _save.Sessions);

            _camera = new CameraRig(transform, null);
            _camera.View = (CameraView)Mathf.Clamp(_save.CameraViewIndex, 0, 3);
            _sticks = new TouchSticks();
            _audio = new DroneAudio(transform);
            _sfx = new Sfx(transform);
            _music = Music.Create(transform);
            _look = new SceneLook(transform, _camera.Camera, null);
            _effects = new Effects(transform);
            GlassBackdrop.Attach(_camera.Camera);
            UiKit.Tap = () => _sfx.Play(Sound.Tap, 0.7f);
            ApplyQuality();
            AudioListener.volume = _save.Muted ? 0f : 1f;

            LoadRegion(RegionCatalog.Get(_save.Region));
            ShowDroneModel(Garage.Selected(_save));

            _hud = new Hud(transform);
            BuildField();
            _hud.CameraPressed += ToggleCamera;
            _hud.ModePressed += ToggleMode;
            _hud.SticksPressed += ToggleSticks;
            _hud.MenuPressed += () => { Click(); ShowMenu(); };
            _hud.ZoomPressed += ToggleZoom;
            _hud.ActionPressed += () => _actionPressed = true;
            _hud.BombPressed += () => _bombPressed = true;
            _hud.LandPressed += ToggleAutoLand;
            _hud.AbilityPressed += () => _abilityPressed = true;
            _hud.SupportPressed += () => _supportPressed = true;
            _hud.PhotoPressed += TogglePhoto;

            _menu = new MenuScreen(transform);
            _menu.SetMap(TacticalMap.Build(_island), _island.Map.HalfSize);
            _menu.StartPressed += () => { Click(); StartMission(); };
            _menu.FreeFlightPressed += () => { Click(); StartFreeFlight(); };
            _menu.SettingsPressed += () => { Click(); ShowSettings(); };
            _menu.WeatherPressed += NextFreeFlightWeather;
            _menu.PreviousPressed += () => SelectMission(_missionIndex - 1);
            _menu.NextPressed += () => SelectMission(_missionIndex + 1);
            _menu.HangarPressed += () => { Click(); ShowHangar(); };
            _menu.VeteranPressed += () => { Click(); _veteran = !_veteran; ShowMenuScreen(); };

            _hangar = new HangarScreen(transform);
            _hangar.Viewed += ShowDroneModel;
            _hangar.Changed += () =>
            {
                Announce(Achievements.Collect(_save));
                _store.Save(_save);
            };
            _hangar.Pressed += refused => _sfx.Play(refused ? Sound.Warning : Sound.Click);
            _hangar.BackPressed += () => { Click(); ShowMenu(); };
            _hangar.Visible = false;

            _globe = new GlobeView(transform);
            _globeScreen = new GlobeScreen(transform);
            _globeScreen.PreviousPressed += () => SelectRegion(_globeIndex - 1);
            _globeScreen.NextPressed += () => SelectRegion(_globeIndex + 1);
            _globeScreen.DeployPressed += Deploy;
            _globeScreen.BackPressed += () => { Click(); ShowMenu(); };
            _globeScreen.Visible = false;
            _menu.GlobePressed += () => { Click(); ShowGlobe(); };

            _talk = new TalkScreen(transform);
            _talk.Chosen += Negotiate;
            _talk.ContinuePressed += () => { Click(); _talk.Visible = false; StartMission(); };
            _talk.BackPressed += () => { Click(); _talk.Visible = false; };
            _talk.Visible = false;

            _daily = new DailyScreen(transform);
            _daily.FlyPressed += FlyDaily;
            _daily.ClaimPressed += ClaimStreak;
            _daily.CratePressed += ClaimCrate;
            _daily.ClosePressed += () => { Click(); _daily.Visible = false; };
            _daily.Visible = false;
            _menu.DailyPressed += () => { Click(); ShowDaily(); };

            _achievements = new AchievementsScreen(transform);
            _achievements.ClosePressed += () => { Click(); _achievements.Visible = false; };
            _achievements.PagePressed += Click;
            _achievements.Visible = false;
            _menu.AchievementsPressed += () => { Click(); _achievements.Show(_save); };

            // Announcements that must be seen on any screen: achievements.
            Canvas toastCanvas = UiKit.CreateCanvas(transform, "Toast", 50);
            _toast = UiKit.Label(UiKit.SafeArea(toastCanvas.transform), 34, TextAnchor.LowerCenter);
            _toast.color = UiKit.Accent;
            UiKit.Place(_toast.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 26f), new Vector2(1600f, 50f));

            _result = new ResultScreen(transform);
            _result.RetryPressed += () => { Click(); StartMission(); };
            _result.ContinuePressed += () =>
            {
                Click();
                // An interstitial, when the rules allow one at all, goes here: between a success and the menu.
                float sinceLast = Time.realtimeSinceStartup - _lastInterstitial;
                if (_ads.InterstitialReady && AdRules.MayShowInterstitial(_save, _lastOutcome, sinceLast))
                {
                    _lastInterstitial = Time.realtimeSinceStartup;
                    _analytics.Event("interstitial_shown");
                    _ads.ShowInterstitial(ContinueAfterResult);
                }
                else
                {
                    ContinueAfterResult();
                }
            };
            _result.DoublePressed += DoubleReward;

            _settings = new SettingsScreen(transform);
            _settings.QualityPressed += SetQuality;
            _settings.FpsPressed += ToggleFps;
            _settings.SoundPressed += ToggleSound;
            _settings.LessonsPressed += () => { Click(); ResetLessons(); Toast(Loc.T("msg.lessons")); };
            _settings.LanguagePressed += SetLanguage;
            _settings.ClosePressed += () => { Click(); _settings.Visible = false; };
            _settings.Visible = false;

            BuildHeadquarters();
            RefreshLabels();
            // The mark and the name, once per launch; the story's next transmission waits behind them.
            _splash = SplashScreen.Create(transform);
            _splash.Finished = () => { _splash = null; _sfx.Play(Sound.Open, 0.6f); TellStory(); };
            ShowMenu();
        }

        SplashScreen _splash;

        /// <summary>The screenshot run has no time for the splash.</summary>
        public void AutomationSkipSplash()
        {
            if (_transmission != null) _transmission.Visible = false;
            if (_splash == null) return;
            _splash.Finished = null;
            Destroy(_splash.gameObject);
            _splash = null;
        }

        void OnApplicationPause(bool paused)
        {
            if (paused && _store != null) _store.Save(_save);
        }

        // ---------------------------------------------------------------- phases

        void ShowMenu()
        {
            _phase = Phase.Menu;
            _dailySlot = -1;
            _mission = AppliedMission();
            Announce(Achievements.Collect(_save));
            _hangar.Visible = false;
            _globeScreen.Visible = false;
            _daily.Visible = false;
            _achievements.Visible = false;
            ShowDroneModel(Garage.Selected(_save));
            ResetFlight();
            _world.Hide();
            _course.Visible = false;
            _hud.Visible = false;
            _result.Visible = false;
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            _music.Menu(RegionCatalog.IndexOf(_region.Id));
            ShowMenuScreen();
            if (_splash == null) TellStory();
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
            SkinDefinition skin = Skins.Worn(_save, drone.Id);
            _drone = skin != null ? DroneView.Create(transform, drone.Look, skin.Body, skin.Glow) : DroneView.Create(transform, drone.Look);
            _shownDrone = drone;
            _camera.Vehicle = drone.Spec.Ground;
            _camera.Reach = Mathf.Clamp(_drone.Length / 1.5f, 1f, drone.Spec.Ground ? 5f : 2.2f);
        }

        /// <summary>The selected mission as the player's negotiation choice, if any, has left it.</summary>
        MissionDefinition AppliedMission() => Negotiations.Apply(_missions[_missionIndex], _save);

        /// <summary>Records the player's answer in a negotiation and shows the reply.</summary>
        void Negotiate(int index)
        {
            Negotiation negotiation = Negotiations.For(_mission.Id);
            if (negotiation == null || !Negotiations.Choose(_save, negotiation, index))
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            Click();
            _store.Save(_save);
            _mission = AppliedMission();
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            ShowMenuScreen();
            _talk.Reply(negotiation, index);
        }

        void ContinueAfterResult()
        {
            // After a first completion the campaign moves on, and it moves to another theatre:
            // the next operation is the first unflown one in the next region that is open.
            if (_advanceOnContinue)
            {
                RegionDefinition next = NextTheatre();
                if (next != _region)
                {
                    _analytics.Event("region_rotate", next.Id);
                    LoadRegion(next);
                    _store.Save(_save);
                    Toast(Loc.T("msg.theatre", Loc.T(next.NameKey)));
                }
                else if (_missionIndex + 1 < _missions.Count)
                {
                    _missionIndex++;
                }
            }
            _advanceOnContinue = false;
            ShowMenu();
        }

        // ---------------------------------------------------------------- advertising

        /// <summary>What the doubling button should say, or null if doubling cannot be offered now.</summary>
        string DoubleLabel()
        {
            if (_lastOutcome == null || !AdRules.CanDouble(_lastOutcome)) return null;
            if (_save.NoAds) return Loc.T("ads.double_free");
            return _ads.RewardedReady ? Loc.T("ads.double") : null;
        }

        void DoubleReward()
        {
            if (_lastOutcome == null || !AdRules.CanDouble(_lastOutcome)) return;
            Click();
            if (_save.NoAds)
            {
                RewardWatched(true);
                return;
            }
            _analytics.Event("rewarded_ad_offer", "double");
            _ads.ShowRewarded(RewardWatched);
        }

        /// <summary>Runs when a rewarded ad for doubling ends; pays only if it was watched through.</summary>
        void RewardWatched(bool completed)
        {
            if (!completed)
            {
                Toast(Loc.T("ads.unavailable"));
                return;
            }
            int extra = AdRules.Double(_save, _lastOutcome);
            if (extra <= 0) return;
            _analytics.Event("rewarded_ad_complete", "double", extra);
            _store.Save(_save);
            _sfx.Play(Sound.Success);
            _result.Show(_mission, _lastOutcome, _save, Loc.T("ads.doubled", extra));
            _result.SetDouble(null);
        }

        void ClaimCrate()
        {
            if (AdRules.CratesLeft(_save, _today) <= 0)
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            Click();
            if (_save.NoAds)
            {
                CrateWatched(true);
                return;
            }
            _analytics.Event("rewarded_ad_offer", "crate");
            _ads.ShowRewarded(CrateWatched);
        }

        void CrateWatched(bool completed)
        {
            if (!completed)
            {
                Toast(Loc.T("ads.unavailable"));
                return;
            }
            int parts = AdRules.ClaimCrate(_save, _today);
            if (parts <= 0) return;
            _analytics.Event("rewarded_ad_complete", "crate", parts);
            _store.Save(_save);
            _sfx.Play(Sound.Success);
            Toast(Loc.T("ads.crate_claimed", parts));
            ShowDaily();
            ShowMenuScreen();
        }

        void Toast(string text)
        {
            _toast.text = text;
            _toastTimer = 4f;
        }

        void ShowMenuScreen()
        {
            bool veteran = _veteran && CanFlyVeteran(_missionIndex);
            _menu.SetVeteran(CanFlyVeteran(_missionIndex) ? Loc.T(veteran ? "menu.veteran.on" : "menu.veteran.off") : null, veteran);
            _menu.Show(veteran ? Operations.Veteran(_mission) : _mission, _save, _missionIndex + 1, _missions.Count, IsLocked(_missionIndex), Loc.T(_region.NameKey));
        }

        // ---------------------------------------------------------------- regions

        /// <summary>
        /// Replaces the island in the world with another region's: terrain, sea, trees, beacons,
        /// the ring course and the missions. Everything region-specific hangs under one root object.
        /// </summary>
        void LoadRegion(RegionDefinition region)
        {
            if (_regionRoot != null)
            {
                _world.Hide();
                _effects.ClearAll();
                Destroy(_regionRoot.gameObject);
            }
            _region = region;
            _save.Region = region.Id;
            _regionRoot = new GameObject("Region_" + region.Id).transform;
            _regionRoot.SetParent(transform, false);

            _island = Island.Generate(region.Seed, region.Biome);
            Material sea = IslandView.Build(_island, _regionRoot);
            WarScenery.Build(_island, _regionRoot, _effects);
            PropsView.Build(_island, _regionRoot);
            CityView.Build(_island, _regionRoot);
            _pickups = null;
            BeaconsView.Build(_island, _regionRoot);
            _course = new RingCourse(_island, _regionRoot);
            _world = new MissionWorld(_regionRoot, _island.Map, _effects);
            _caches = new CachesView(_island, _regionRoot, index => Caches.IsFound(_save, region.Id, index));
            _camera.SetMap(_island.Map);
            _look.SetSea(sea);
            if (_menu != null) _menu.SetMap(TacticalMap.Build(_island), _island.Map.HalfSize);

            _missions = MissionCatalog.Load(region, _island);
            _missionIndex = FirstUnfinishedMission();
            _mission = AppliedMission();
            _sim = CreateSim(0f);
            _previous = _sim.State;
            // The previous island's meshes and textures are no longer referenced by anything.
            Resources.UnloadUnusedAssets();
        }

        /// <summary>The next open region, going round the catalog, that still has an operation nobody has flown.</summary>
        RegionDefinition NextTheatre()
        {
            int count = RegionCatalog.All.Count, current = RegionCatalog.IndexOf(_region.Id);
            for (int step = 1; step <= count; step++)
            {
                RegionDefinition region = RegionCatalog.All[(current + step) % count];
                if (IsRegionLocked(region)) continue;
                foreach (string id in MissionCatalog.Ids(region))
                {
                    MissionRecord record = _save.Find(id);
                    if (record == null || record.Completions == 0) return region;
                }
            }
            return _region;
        }

        bool IsRegionLocked(RegionDefinition region) => Garage.TotalStars(_save) < region.StarsRequired;

        void ShowGlobe()
        {
            _phase = Phase.Globe;
            _menu.Visible = false;
            _look.ShowSpace();
            _globeIndex = RegionCatalog.IndexOf(_region.Id);
            _globe.Focus(_globeIndex, true);
            RefreshGlobe();
        }

        void SelectRegion(int index)
        {
            Click();
            int count = RegionCatalog.All.Count;
            _globeIndex = (index + count) % count;
            _globe.Focus(_globeIndex, false);
            RefreshGlobe();
        }

        void RefreshGlobe()
        {
            RegionDefinition region = RegionCatalog.All[_globeIndex];
            int completed = 0, stars = 0;
            List<string> ids = MissionCatalog.Ids(region);
            foreach (string id in ids)
            {
                MissionRecord record = _save.Find(id);
                if (record == null) continue;
                if (record.Completions > 0) completed++;
                stars += record.Stars;
            }
            _globe.Refresh(_globeIndex, IsRegionLocked);
            _globeScreen.Show(region, completed, ids.Count, stars, Garage.TotalStars(_save));
        }

        void Deploy()
        {
            RegionDefinition region = RegionCatalog.All[_globeIndex];
            if (IsRegionLocked(region))
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            Click();
            if (region != _region)
            {
                _analytics.Event("region_deploy", region.Id);
                LoadRegion(region);
                _store.Save(_save);
            }
            ShowMenu();
        }

        /// <summary>One finger turns the globe; a tap on a marker selects its region.</summary>
        void UpdateGlobe(float deltaTime)
        {
            _globe.Update(deltaTime);
            if (Input.touchCount != 1) return;
            Touch touch = Input.GetTouch(0);
            if (touch.phase == TouchPhase.Began)
            {
                _touchStart = touch.position;
                UnityEngine.EventSystems.EventSystem events = UnityEngine.EventSystems.EventSystem.current;
                // A touch that starts on the panel is the panel's business.
                _touchMoved = events != null && events.IsPointerOverGameObject(touch.fingerId);
                if (_touchMoved) _touchStart = new Vector2(-1f, -1f);
                return;
            }
            if (_touchStart.x < 0f) return;

            if (touch.phase == TouchPhase.Moved)
            {
                if ((touch.position - _touchStart).magnitude > Screen.height * 0.02f) _touchMoved = true;
                if (_touchMoved) _globe.Drag(touch.deltaPosition * (160f / Screen.height));
            }
            else if (touch.phase == TouchPhase.Ended && !_touchMoved)
            {
                int picked = _globe.Pick(_camera.Camera.ScreenPointToRay(touch.position));
                if (picked >= 0) SelectRegion(picked);
            }
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
            _mission = AppliedMission();
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            ShowMenuScreen();
        }

        /// <param name="strike">The mission is flown with one-way strike drones.</param>
        FlightSim CreateSim(float payload, bool strike = false)
        {
            DroneSpec spec = _trialDrone != null ? DroneCatalog.Get(_trialDrone).Spec : Garage.Spec(_save, FlyingDrone());
            spec = ArmouryRules.Outfit(spec, _save);
            if (strike) spec = spec.AsStrikeDrone();
            var sim = new FlightSim(_island, spec.WithPayload(payload));
            sim.Mode = (FlightMode)Mathf.Clamp(_save.FlightModeIndex, 0, 1);
            sim.Cushion = !strike;
            return sim;
        }

        void StartMission()
        {
            if (IsLocked(_missionIndex)) return;

            // The last mission of a region's major operation opens with a negotiation, once.
            Negotiation negotiation = Negotiations.For(_mission.Id);
            if (negotiation != null && Negotiations.Chosen(_save, _mission.Id) < 0)
            {
                _talk.Ask(negotiation, _save);
                return;
            }
            _mission = _dailySlot >= 0 && _dailySlot < _dailyOps.Count
                ? Daily.Apply(AppliedMission(), _dailyOps[_dailySlot].Modifier)
                : AppliedMission();
            MissionDefinition mission = _mission;
            if (_veteran && CanFlyVeteran(_missionIndex)) mission = Operations.Veteran(mission);
            _operationSector = -1;
            _towerFloor = 0;
            _trialDrone = null;
            Launch(mission);
        }

        /// <summary>The aircraft that is flying: the selected one, or one being tried on the range.</summary>
        DroneDefinition FlyingDrone() => _trialDrone != null ? DroneCatalog.Get(_trialDrone) : FieldDrone() ?? Garage.Selected(_save);

        /// <summary>Veteran difficulty opens for a mission once it has been completed the ordinary way.</summary>
        bool CanFlyVeteran(int index)
        {
            MissionRecord record = _save.Find(_missions[index].Id);
            return record != null && record.Completions > 0 && _missions[index].Targets.Count > 0;
        }

        /// <summary>Starts a generated operation: a contract, a sector battle, survival, a raid, the range.</summary>
        public void StartOperation(MissionDefinition operation, int sector = -1, int towerFloor = 0, string trialDrone = null)
        {
            if (operation == null)
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            _dailySlot = -1;
            _operationSector = sector;
            _towerFloor = towerFloor;
            _trialDrone = trialDrone;
            Launch(operation);
        }

        /// <summary>Puts a mission in the air: the aircraft, the run, the weather, the world, the music.</summary>
        void Launch(MissionDefinition mission)
        {
            _launching = mission;
            LaunchPrepared(mission);
            _launching = null;
        }

        void LaunchPrepared(MissionDefinition mission)
        {
            _mission = mission;
            // A one-way attack drone turns a mission of pure destruction into a kamikaze sortie;
            // anything that asks for a photograph, a delivery or a landing it cannot fly at all.
            DroneSpec chosen = FlyingDrone().Spec;
            if (chosen.OneWay > 0 && _mission.StrikeDrones == 0)
            {
                MissionDefinition sortie = MissionRun.AsOneWaySortie(_mission, chosen.OneWay);
                if (sortie == null)
                {
                    _sfx.Play(Sound.Warning);
                    Toast(Loc.T("msg.oneway_refused"));
                    return;
                }
                _mission = sortie;
            }
            // The reserve hangar's spare aircraft stand by, except on the range and for one-way drones.
            if (_mission.StrikeDrones == 0 && _mission.Targets.Count > 0 && _mission.Mode != "range" && _mission.Mode != "sniper" && BaseRules.Spares(_save) > 0)
            {
                _mission = _mission.Copy();
                _mission.Spares = BaseRules.Spares(_save);
            }
            _hideOperations?.Invoke();
            _phase = Phase.Mission;
            _analytics.Event("mission_start", _mission.Id);
            _sim = CreateSim(_mission.Payload, _mission.StrikeDrones > 0);
            ResetFlight();
            _run = new MissionRun(_mission, _sim.Spec, _island.Map) { NoReturn = true };
            ApplyWeather(WeatherCatalog.Get(_mission.WeatherId));
            _world.Show(_mission);
            _course.Visible = false;
            _hud.DetectionVisible = _mission.HasDetectors;
            EnterFlight(true);
            if (_mission.Targets.Count > 0) _music.Battle(_mission.Order + RegionCatalog.IndexOf(_region.Id));
            else _music.Menu(RegionCatalog.IndexOf(_region.Id) + 1);
            StartIntro();
            FieldLaunched();
        }

        /// <summary>
        /// Opens a combat mission with a slow pass around what is to be destroyed, the name of
        /// the operation across the screen. Skipped for the screenshot run.
        /// </summary>
        void StartIntro()
        {
            if (SkipIntro || _mission.Targets.Count == 0) return;
            int index = -1;
            for (int i = 0; i < _mission.Targets.Count && index < 0; i++)
                if (!_mission.Targets[i].Friendly) index = i;
            if (index < 0) return;
            TargetDefinition target = _mission.Targets[index];
            _introPoint = target.Path.Count > 0 ? target.Path[0] : target.Position;
            _introPoint.y = Mathf.Max(_island.Map.Height(_introPoint.x, _introPoint.z), Island.WaterLevel);
            _introTimer = 4.2f;
            _hud.SetLetterbox(true);
            _hud.SetMessage(Loc.T("brief.operation") + " «" + _mission.TitleText.ToUpperInvariant() + "»");
            _messageTimer = 0f;
            _sfx.Play(Sound.Open);
        }

        /// <summary>Set by the screenshot run, which needs the drone in the air at once.</summary>
        public bool SkipIntro;

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
            _music.Menu(RegionCatalog.IndexOf(_region.Id) + 1);
        }

        void EnterFlight(bool gauges)
        {
            _menu.Visible = false;
            _result.Visible = false;
            _hangar.Visible = false;
            _globeScreen.Visible = false;
            _talk.Visible = false;
            _daily.Visible = false;
            _achievements.Visible = false;
            _save.Stats.Flights++;
            // Whatever model the hangar was showing, the drone that flies is the selected one.
            ShowDroneModel(FlyingDrone());
            _hud.Visible = true;
            _hud.GaugesVisible = gauges;
            _hud.SetMessage("");
            _hud.SetAction(null);
            _zoom = 1f;
            _camera.Zoom = 1f;
            _actionPressed = _bombPressed = false;
            _respawnAfterFreeze = false;
            _shownAmmoStamp = -1;
            _lastLock = -1;
            _hud.SetSecondary(null);
            _hud.UpdateTargets(_camera.Camera, null, null, null);
            _hud.UpdateScope(null, null, null, Vector3.zero, 0f);
            RefreshLabels();
            _shownHeadlineStamp = _shownSublineStamp = -1;
            _drone.Visible = _camera.View != CameraView.Fpv && _camera.View != CameraView.Gun;
        }

        void ShowResult()
        {
            _phase = Phase.Result;
            MissionOutcome outcome = MissionOutcome.From(_run);
            bool campaign = _mission.Mode == "campaign" || _mission.Mode == "veteran";
            int waves = _mission.Mode == "survival" ? Mathf.Max(0, _run.ObjectiveIndex - 1) : 0;
            if (campaign) Progress.Grant(_save, _mission, outcome);
            else Progress.GrantOperation(_save, _mission, outcome, waves);
            _advanceOnContinue = _mission.Mode == "campaign" && outcome.Succeeded && _save.Find(_mission.Id).Completions == 1 && _dailySlot < 0;

            // A daily operation pays on top, once, if its twist was honoured.
            string extra = null;
            if (_dailySlot >= 0 && _dailySlot < _dailyOps.Count && outcome.Succeeded)
            {
                DailyOp op = _dailyOps[_dailySlot];
                if (!Daily.Satisfied(outcome, op.Modifier)) extra = Loc.T("daily.not_met");
                else if (Daily.Complete(_save, _today, _dailySlot, op)) extra = Loc.T("daily.completed", op.Credits, op.Parts);
            }

            // The career: experience, rank, season, tasks, mastery, the war over the map.
            _today = Daily.Today(_save, DateTime.Now);
            MissionReport report = MissionReport.From(_run, _mission, outcome, FlyingDrone().Id);
            report.Sector = _operationSector;
            if (_trialDrone != null) report.Mode = "range";
            CareerGain gain = Career.Apply(_save, report, _today);
            if (outcome.Succeeded && _towerFloor > _save.Career.TowerFloor) _save.Career.TowerFloor = _towerFloor;
            var lines = new List<string>();
            if (!string.IsNullOrEmpty(extra)) lines.Add(extra);
            if (_mission.Mode == "survival") lines.Add(Loc.T("result.waves", waves, _save.Career.SurvivalBest));
            // Flying it well pays a little on top: time spent fast and low, and a landing put down on the mark.
            int flair = outcome.Succeeded && _mission.Mode != "range" ? Mathf.Min(150, Mathf.RoundToInt(_lowSeconds * 3f)) + _landingGrade * 20 : 0;
            if (flair > 0)
            {
                _save.Credits += flair;
                _save.Stats.CreditsEarned += flair;
                lines.Add(Loc.T("result.flair", flair));
            }
            FieldResult(outcome, lines);
            if (gain.Xp > 0) lines.Add(Loc.T("reward.xp", gain.Xp) + "   " + Career.RankName(gain.RankAfter));
            lines.AddRange(gain.News);
            if (!gain.Rewards.Empty) lines.Add(gain.Rewards.Describe());
            extra = lines.Count > 0 ? string.Join("\n", lines) : null;
            _trialDrone = null;
            Announce(Achievements.Collect(_save));
            if (!_store.Save(_save)) Debug.LogError("[Skyforge] Progress could not be saved.");

            ResetFlight();
            _world.Hide();
            _hud.Visible = false;
            _lastOutcome = outcome;
            _lastExtra = extra;
            _analytics.Event(outcome.Succeeded ? "mission_complete" : "mission_fail", _mission.Id,
                outcome.Succeeded ? outcome.Stars : (int)outcome.FailReason);
            _result.Show(_mission, outcome, _save, extra);
            _result.SetDouble(DoubleLabel());
        }

        // ---------------------------------------------------------------- daily operations and achievements

        void ShowDaily()
        {
            _today = Daily.Today(_save, DateTime.Now);
            int stars = Garage.TotalStars(_save);
            var beaten = new List<KeyValuePair<string, string>>();
            var rewards = new List<int>();
            foreach (RegionDefinition region in RegionCatalog.All)
            {
                if (stars < region.StarsRequired) continue;
                foreach (KeyValuePair<string, int> mission in MissionCatalog.Summaries(region))
                {
                    MissionRecord record = _save.Find(mission.Key);
                    if (record == null || record.Completions == 0) continue;
                    beaten.Add(new KeyValuePair<string, string>(region.Id, mission.Key));
                    rewards.Add(mission.Value);
                }
            }
            _dailyOps = Daily.Ops(_today, beaten, rewards);

            var titles = new List<string>();
            foreach (DailyOp op in _dailyOps)
                titles.Add(Loc.T(RegionCatalog.Get(op.RegionId).NameKey) + "  ·  " + Loc.T("mission." + op.MissionId + ".title"));
            _daily.Show(_save, _today, _dailyOps, titles);

            int crates = AdRules.CratesLeft(_save, _today);
            bool canOffer = crates > 0 && (_save.NoAds || _ads.RewardedReady);
            _daily.SetCrate(canOffer ? Loc.T("ads.crate", AdRules.PartsPerCrate, crates) : null);
        }

        void ClaimStreak()
        {
            if (Daily.ClaimStreak(_save, _today) == 0)
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            _sfx.Play(Sound.Success);
            Announce(Achievements.Collect(_save));
            _store.Save(_save);
            ShowDaily();
            ShowMenuScreen();
        }

        void FlyDaily(int slot)
        {
            if (slot >= _dailyOps.Count || Daily.IsDone(_save, _today, slot))
            {
                _sfx.Play(Sound.Warning);
                return;
            }
            Click();
            DailyOp op = _dailyOps[slot];
            if (op.RegionId != _region.Id) LoadRegion(RegionCatalog.Get(op.RegionId));
            for (int i = 0; i < _missions.Count; i++)
                if (_missions[i].Id == op.MissionId) _missionIndex = i;
            _mission = AppliedMission();
            _dailySlot = slot;
            StartMission();
        }

        /// <summary>Tells the player about newly earned achievements, wherever they are in the game.</summary>
        void Announce(List<Achievement> earned)
        {
            if (earned.Count == 0) return;
            Achievement last = earned[earned.Count - 1];
            _toast.text = Loc.T("achievements.toast", Loc.T(last.NameKey, last.Goal));
            _toastTimer = 5f;
            _sfx.Play(Sound.Success);
            _store.Save(_save);
        }

        void ResetFlight()
        {
            _freezeTimer = 0f;
            _lowSeconds = 0f;
            _landingGrade = 0;
            _killCam = 0f;
            _radioedScatter = false;
            _shownSpecialStamp = -1;
            if (_photo)
            {
                _photo = false;
                if (_hud != null) _hud.SetPhoto(false);
            }
            _introTimer = 0f;
            _slowMotion = 0f;
            Time.timeScale = 1f;
            _secondaries.Clear();
            _radioTimer = 0f;
            _radioedHull = _radioedLock = false;
            if (_hud != null)
            {
                _hud.SetRadio(null);
                _hud.SetLetterbox(false);
            }
            _autoLand = false;
            if (_look.Scope) _look.SetScope(false);
            if (_hud != null) _hud.SetGunCamera(null);
            _respawnAfterFreeze = false;
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

            if (_phase == Phase.Globe) UpdateGlobe(deltaTime);

            if (flying)
            {
                _sticks.Update();
                if (_photo)
                {
                    UpdatePhoto();
                }
                else if (_freezeTimer > 0f)
                {
                    _freezeTimer -= deltaTime;
                    if (_freezeTimer <= 0f) AfterFreeze();
                }
                else if (_introTimer > 0f)
                {
                    // The opening fly-past; a touch anywhere cuts it short.
                    _introTimer -= Time.unscaledDeltaTime;
                    if (Input.touchCount > 0 || Input.GetMouseButtonDown(0)) _introTimer = Mathf.Min(_introTimer, 0.01f);
                    if (_introTimer <= 0f) EndIntro();
                }
                else
                {
                    Simulate(ReadInput(), deltaTime);
                }
            }
            UpdateSlowMotion();

            // AfterFreeze may have left the flight.
            flying = _phase == Phase.Mission || _phase == Phase.FreeFlight;
            Present(deltaTime, flying);
            PresentField(deltaTime);
            _caches.Update(deltaTime);
            _effects.Update(deltaTime, _camera.Camera.transform);
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toast.text = "";
            }
            _look.Update();
            if (_look.Lightning) _sfx.Play(Sound.Explosion, 0.35f);
            if (_phase == Phase.Mission && _weather != null && _weather.Id == "sunset" && _run != null)
                _look.Dusk(_run.Elapsed / 420f);
            CountFrames();
        }

        /// <summary>A decisive kill is shown at a third of the speed for a moment, then time runs on.</summary>
        void UpdateSlowMotion()
        {
            if (_slowMotion <= 0f || _photo) return;
            _slowMotion -= Time.unscaledDeltaTime;
            if (_slowMotion <= 0f || _phase != Phase.Mission)
            {
                _slowMotion = 0f;
                Time.timeScale = 1f;
                return;
            }
            // Eases back to full speed over the last third.
            Time.timeScale = Mathf.Lerp(1f, 0.3f, Mathf.Clamp01(_slowMotion / 0.35f));
        }

        void EndIntro()
        {
            _introTimer = 0f;
            _hud.SetMessage("");
            _hud.SetLetterbox(false);
            _camera.Snap();
            Radio("radio.cleared");
        }

        /// <summary>A line from the operator: a click of static and a caption that stays a few seconds.</summary>
        void Radio(string key)
        {
            _sfx.Play(Sound.Radio, 0.7f);
            _hud.SetRadio(Loc.T("radio.callsign") + ": " + Loc.T(key));
            _radioTimer = 4.5f;
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
                SimEvent simEvent = StepAircraft(input);
                if (simEvent == SimEvent.TookOff) _hasFlown = true;
                if (simEvent == SimEvent.Landed && !_sim.Spec.Ground) GradeLanding();
                // Flying fast and low is worth something at the end of a fight.
                if (_phase == Phase.Mission && !_sim.State.Grounded && !_sim.Spec.Ground && !_run.Sniper && _sim.AltitudeAboveGround < 6f &&
                    new Vector2(_sim.State.Velocity.x, _sim.State.Velocity.z).magnitude > _sim.Spec.MaxSpeed * 0.55f)
                    _lowSeconds += StepSeconds;
                if (simEvent == SimEvent.Crashed && (_phase != Phase.Mission || !_run.Kamikaze)) _save.Stats.Crashes++;
                _load = ComputeLoad(_sim.State);
                _save.Stats.DistanceMeters += new Vector2(_sim.State.Velocity.x, _sim.State.Velocity.z).magnitude * StepSeconds;

                int cache = Caches.TryCollect(_save, _region.Id, _island, _sim.State.Position);
                if (cache >= 0)
                {
                    _caches.Hide(cache);
                    _sfx.Play(Sound.Success);
                    ShowMessage(Loc.T("msg.cache", Caches.PartsEach), 2.5f);
                    Announce(Achievements.Collect(_save));
                    _store.Save(_save);
                }

                if (_phase == Phase.Mission)
                {
                    int before = _run.ObjectiveIndex;
                    if (_abilityPressed) _run.UseAbility();
                    if (_supportPressed) _run.CallSupport();
                    _run.Guide(_guide);
                    ActionResult action = _run.Step(_sim.State, simEvent, _load, _sim.LastTouchdownSpeed, StepSeconds,
                        _actionPressed, _sim.Wind, _zoom, _bombPressed);
                    _actionPressed = _bombPressed = _abilityPressed = _supportPressed = false;
                    ActionTaken(action);
                    CombatEvents();
                    if (_run.ShotLanded) ShotLanded();
                    if (_run.ObjectiveIndex != before) ObjectiveCompleted(before);
                    if (_run.Status != MissionStatus.InProgress)
                    {
                        MissionEnded();
                        return;
                    }
                    if (_run.NeedsRespawn)
                    {
                        // The aircraft is gone; show what happened, then hand over the next one.
                        _drone.Visible = false;
                        _respawnAfterFreeze = true;
                        Freeze(_run.Kamikaze ? Loc.T("msg.next_drone", _run.DronesLeft) : Loc.T("msg.spare", _run.SparesLeft), 1.5f);
                        return;
                    }
                }
                else
                {
                    if (simEvent == SimEvent.Crashed)
                    {
                        _sfx.Play(Sound.Explosion, 0.8f);
                        _effects.Explosion(_sim.State.Position, 4f);
                        _camera.Shake(0.6f);
                        _drone.Visible = false;
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
            if (SniperMode)
            {
                SniperInput();
                _guide = Vector2.zero;
                return default;
            }
            // While a hand-flown missile is in the air the sticks fly it and the aircraft holds where it is.
            if (_phase == Phase.Mission && _run != null && _run.GuidedActive)
            {
                _guide = new Vector2(move.x + lift.x, move.y + lift.y);
                return default;
            }
            _guide = Vector2.zero;
            _sim.AutoLanding = _autoLand;
            if (_autoLand)
            {
                // Any firm push on a stick takes the drone back from the automatic landing.
                if (move.magnitude > 0.35f || lift.magnitude > 0.35f || _sim.State.Grounded) _autoLand = false;
                else return FlightSim.LandingInput(_sim.State, _island.Spawn);
                _sim.AutoLanding = false;
            }
            return new FlightInput { Move = move, Throttle = lift.y, Yaw = lift.x };
        }

        /// <summary>The automatic landing is offered in the air near the pad, to a drone that is coming back.</summary>
        bool CanAutoLand()
        {
            if (_sim.State.Grounded || _freezeTimer > 0f || _sim.Spec.Ground || SniperMode) return false;
            if (_phase == Phase.Mission && _run.Kamikaze) return false;
            // An aircraft that cannot hover cannot be set down on a point.
            if (_sim.Spec.MinSpeed > 0f) return false;
            Vector3 offset = _sim.State.Position - _island.Spawn;
            return offset.x * offset.x + offset.z * offset.z < AutoLandRange * AutoLandRange;
        }

        void ToggleAutoLand()
        {
            if (!_autoLand && !CanAutoLand()) return;
            Click();
            _autoLand = !_autoLand;
        }

        void PresentLanding()
        {
            bool offered = CanAutoLand();
            if (!offered) _autoLand = false;
            _hud.SetLand(offered ? Loc.T(_autoLand ? "hud.landing" : "hud.land") : null, _autoLand);
        }

        void PresentGunCamera()
        {
            bool riding = _phase == Phase.Mission && _run.GuidedActive;
            bool gun = (_camera.View == CameraView.Gun || riding) && !_photo && _killCam <= 0f && !SniperMode;
            // At night the sight is thermal: what is warm glows.
            bool thermal = gun && _weather != null && _weather.Id == "night";
            if (gun != _look.Scope || thermal != _look.Thermal) _look.SetScope(gun, thermal);
            _world.SetThermal(thermal);
            if (riding)
            {
                _hud.SetGunCamera(Loc.T("hud.gun.missile"));
                return;
            }
            if (!gun)
            {
                _hud.SetGunCamera(null);
                return;
            }
            bool locked = _phase == Phase.Mission && _run.IsCombat && _run.LockedTarget >= 0;
            _camera.GunTarget = locked ? _run.Target(_run.LockedTarget).Position + Vector3.up * 1.2f : (Vector3?)null;
            int stamp = Mathf.RoundToInt(_sim.AltitudeAboveGround) * 1000 + Mathf.RoundToInt(Mathf.Repeat(_sim.State.YawDeg, 360f)) + (locked ? 500000 : 0);
            if (stamp == _shownGunStamp) return;
            _shownGunStamp = stamp;
            _hud.SetGunCamera(Loc.T("hud.gun", Mathf.RoundToInt(Mathf.Repeat(_sim.State.YawDeg, 360f)), Mathf.RoundToInt(_sim.AltitudeAboveGround)) +
                              "\n" + Loc.T(locked ? "hud.gun.locked" : "hud.gun.search"));
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
            if (_phase == Phase.Mission && _respawnAfterFreeze)
            {
                _respawnAfterFreeze = false;
                _sim.Reset();
                _previous = _sim.State;
                _run.Respawned();
                _hasFlown = false;
                _camera.Snap();
                _drone.Visible = _camera.View != CameraView.Fpv && _camera.View != CameraView.Gun;
                return;
            }
            if (_phase == Phase.Mission)
            {
                ShowResult();
                return;
            }
            ResetFlight();
            RestartCourse();
            _drone.Visible = _camera.View != CameraView.Fpv && _camera.View != CameraView.Gun;
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

            if (_phase == Phase.Globe)
            {
                _camera.LookAt(GlobeView.Center, 34f, 6.5f);
                return;
            }
            if (!flying)
            {
                // The drone sits beside whichever panel is open: menu on the left, hangar on the right.
                float side = _phase == Phase.Hangar ? 2.6f : _phase == Phase.Menu ? -2.6f : 0f;
                float reach = Mathf.Lerp(1f, _camera.Reach, 0.6f);
                _camera.Orbit(_island.Spawn, Time.unscaledTime, side * reach, (_phase == Phase.Hangar ? 7.5f : 11f) * reach, _phase == Phase.Hangar ? 0.30f : 0.22f);
                return;
            }

            if (_introTimer > 0f)
            {
                _world.Update(_run, now, 0f);
                _camera.Orbit(_introPoint + Vector3.up * 4f, 14f + (4.2f - _introTimer) * 2.2f, 0f, 62f);
                return;
            }
            if (_radioTimer > 0f)
            {
                _radioTimer -= Time.unscaledDeltaTime;
                if (_radioTimer <= 0f) _hud.SetRadio(null);
            }
            UpdateSecondaries(deltaTime);

            bool riding = _phase == Phase.Mission && _run.GuidedActive;
            if (_killCam > 0f) _killCam -= Time.unscaledDeltaTime;
            if (_photo) _camera.Free(position + Vector3.up * 0.3f, _photoYaw, _photoPitch, _photoDistance);
            else if (riding) _camera.Ride(_run.GuidedPosition, _run.GuidedDirection);
            else if (SniperMode) _camera.Scope(_mission.Perch, SniperAim, 30f / Mathf.Max(1f, _zoom));
            else if (_killCam > 0f) _camera.Orbit(_killPoint + Vector3.up * 3f, Time.unscaledTime * 3.5f, 0f, 30f, 0.34f);
            else _camera.Update(position, yaw, tilt, speedShare, deltaTime);
            _hud.SetTelemetry(speed * 3.6f, _sim.AltitudeAboveGround);
            string moveLabel = Loc.T(SniperMode ? "hint.aim" : _sim.Spec.Ground ? "hint.drive.move" : "hint.move");
            string liftLabel = Loc.T(SniperMode ? "hint.aim" : _sim.Spec.Ground ? "hint.drive.turn" : "hint.lift");
            _hud.UpdateSticks(_sticks, _save.SticksSwapped ? liftLabel : moveLabel, _save.SticksSwapped ? moveLabel : liftLabel);

            if (_messageTimer > 0f)
            {
                _messageTimer -= deltaTime;
                if (_messageTimer <= 0f)
                {
                    _hud.SetMessage("");
                    if (_phase == Phase.FreeFlight && _course.Finished) RestartCourse();
                }
            }

            PresentLanding();
            PresentGunCamera();
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
            if (objective != null && objective.Type == ObjectiveType.Destroy) detail = _run.Remaining(objective);
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
                                Loc.T("hud.timer", Format.Time(_run.Elapsed), Format.Time(_mission.TimeGoalSeconds)) +
                                (_run.Kamikaze ? "   " + Loc.T("hud.drones", _run.DronesLeft) : ""));
            }

            bool lowBattery = _run.Battery <= MissionRun.LowBattery;
            bool lowSignal = _run.Signal <= MissionRun.LowSignal;
            _hud.SetGauges(_run.Battery, lowBattery, _run.Signal, lowSignal, _run.Hull, _run.Detection, _run.BeingDetected);

            bool live = objective != null && _freezeTimer <= 0f;
            bool hasTarget = live && TryGetMarker(objective, now.Position, out Vector3 marker);
            _hud.UpdateMarker(_camera.Camera, hasTarget ? MarkerPosition(objective, now.Position) : Vector3.zero,
                hasTarget, _run.TargetFramed);

            bool armed = live && _run.IsCombat && !_run.Kamikaze && !_run.Sniper && objective.Type != ObjectiveType.Photo &&
                         objective.Type != ObjectiveType.Strike;
            if (armed)
            {
                // The buttons carry the ammunition count; their text is rebuilt only when it changes.
                int ammo = _run.Rockets * 100 + _run.Bombs;
                if (ammo != _shownAmmoStamp)
                {
                    _shownAmmoStamp = ammo;
                    _hud.SetAction(Loc.T(_sim.Spec.Ground ? "hud.shells" : "hud.rockets", _run.Rockets));
                    _hud.SetSecondary(_sim.Spec.Bombs > 0 ? Loc.T("hud.bombs", _run.Bombs) : null);
                }
                _hud.SetActionReady(_run.LockedTarget >= 0 || (_run.FreeAim != null && _run.Rockets > 0));
                // The special equipment and the support, redrawn only when a number on them changes.
                int tier = _run.SupportTier, cannon = Mathf.CeilToInt(_run.CannonSeconds);
                int special = _run.AbilityCharges + tier * 10 + _run.Streak * 100 + cannon * 10000 + (_run.Jamming ? 1000000 : 0);
                if (special != _shownSpecialStamp)
                {
                    _shownSpecialStamp = special;
                    DroneSpec spec = _sim.Spec;
                    string ability = spec.Ability != Ability.None
                        ? Loc.T("hud.ability." + spec.Ability.ToString().ToLowerInvariant(), _run.AbilityCharges) : null;
                    if (spec.Cannon > 0f) ability = (ability != null ? ability + "\n" : "") + Loc.T("hud.cannon", cannon);
                    _hud.SetAbility(ability);
                    _hud.SetSupport(tier > 0 ? Loc.T("hud.support." + tier) : Loc.T("hud.streak", _run.Streak, MissionRun.SupportStreak[0]), tier > 0);
                }
                if (_run.LockedTarget >= 0 && _lastLock < 0)
                {
                    _sfx.Play(Sound.Lock, 0.6f);
                    if (!_radioedLock)
                    {
                        _radioedLock = true;
                        Radio("radio.weapons_free");
                    }
                }
                _lastLock = _run.LockedTarget;
            }
            else
            {
                string action = null;
                if (live && objective.Type == ObjectiveType.Photo) action = Loc.T("hud.action.photo");
                if (live && objective.Type == ObjectiveType.Strike && _run.ShotsLeft > 0 && !_run.Shot.Active)
                    action = Loc.T("hud.action.fire");
                if (live && _run.Sniper) action = Loc.T("hud.action.fire");
                _hud.SetAction(action);
                _hud.SetActionReady(true);
                _hud.SetSecondary(null);
                _hud.SetAbility(null);
                _hud.SetSupport(null, false);
                _shownSpecialStamp = -1;
                _shownAmmoStamp = -1;
            }
            _hud.UpdateTargets(_camera.Camera, live && _run.IsCombat ? _run : null, _mission,
                objective != null && objective.Type == ObjectiveType.Destroy ? objective : null);
            _hud.UpdateScope(_run, _mission, objective != null && objective.Type == ObjectiveType.Destroy ? objective : null,
                now.Position, now.YawDeg);
            _hud.SetDamage(_run.UnderFire && _freezeTimer <= 0f, Time.deltaTime);
            if (!_radioedHull && _run.IsCombat && !_run.Kamikaze && _run.Hull < 0.4f && _freezeTimer <= 0f)
            {
                _radioedHull = true;
                Radio("radio.hull");
            }
            _gunSoundClock -= Time.deltaTime;
            _noticeClock -= Time.deltaTime;

            // One beep when something first goes wrong, not a siren for the rest of the flight.
            bool warning = lowBattery || lowSignal || _run.BeingDetected || _run.UnderFire;
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
                case ObjectiveType.Destroy:
                    return Loc.T(objective.TextKey, detail);
                default:
                    return Loc.T(objective.TextKey);
            }
        }

        string Hint(ObjectiveDefinition objective, FlightState now, bool lowBattery, bool lowSignal, int percent)
        {
            if (_run.Signal <= 0f) return Loc.T("warn.signal_lost", Mathf.CeilToInt(_run.SignalCountdown));
            if (_run.BeingDetected) return Loc.T("warn.detected");
            if (_run.UnderFire) return Loc.T(_run.Hull < 0.3f ? "warn.hull" : "warn.under_fire");
            if (lowSignal) return Loc.T("warn.signal");
            if (lowBattery) return Loc.T("warn.battery");
            if (!_hasFlown) return Loc.T(_sim.Spec.Ground ? "hint.drive" : "hint.takeoff");
            if (objective == null) return "";
            if (_run.IsCombat)
            {
                if (AllyUnderFire()) return Loc.T("warn.ally");
                // Behind a rifle none of the aircraft's advice applies; the scope says what matters.
                if (_run.Sniper) return "";
                if (_run.Kamikaze) return Loc.T("hint.kamikaze");
                if (objective.Type == ObjectiveType.Destroy && _run.Rockets <= 0 && _run.Bombs <= 0 && !_run.Sniper) return Loc.T("hint.rearm");
                // The first fight explains the two weapons; after that the player knows.
                if (objective.Type == ObjectiveType.Destroy && _mission.Order == 1)
                    return Loc.T(_run.Rockets > 0 ? "hint.lock" : "hint.bomb");
            }

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

        bool AllyUnderFire()
        {
            for (int i = 0; i < _run.TargetCount; i++)
            {
                TargetDefinition definition = _mission.Targets[i];
                if (definition.Attacks >= 0 && _run.Target(i).Alive && _run.Target(i).Firing) return true;
            }
            return false;
        }

        /// <summary>Sound, fire and smoke for everything that happened to the fight in the last step.</summary>
        void CombatEvents()
        {
            Vector3 drone = _sim.State.Position;
            foreach (CombatEvent e in _run.Events)
            {
                float distance = Vector3.Distance(drone, e.Position);
                FieldEvent(e, distance);
                switch (e.Type)
                {
                    case CombatEventType.RocketFired:
                        _sfx.Play(Sound.Rocket, 0.8f);
                        break;
                    case CombatEventType.BombDropped:
                        _sfx.Play(Sound.Release);
                        break;
                    case CombatEventType.Explosion:
                        if (_island.Map.Height(e.Position.x, e.Position.z) < Island.WaterLevel - 0.3f && e.Position.y < Island.WaterLevel + 2.5f)
                            _effects.Splash(e.Position, e.Size);
                        else _effects.Explosion(e.Position, e.Size);
                        _sfx.Play(Sound.Explosion, Mathf.Lerp(1f, 0.25f, distance / 350f));
                        _camera.Shake(1.2f - distance / (e.Size * 9f));
                        break;
                    case CombatEventType.TargetDestroyed:
                    {
                        TargetDefinition definition = _mission.Targets[e.Target];
                        _effects.Explosion(e.Position + Vector3.up * 1.5f, e.Size);
                        _sfx.Play(Sound.Explosion, Mathf.Lerp(1f, 0.35f, distance / 400f));
                        _camera.Shake(1.3f - distance / (e.Size * 10f));
                        if (definition.Kind.Structure) _effects.Collapse(e.Position, definition.Kind.Radius * 1.3f);
                        // Whatever was inside goes on exploding for a second or two.
                        int cookOff = definition.Kind.Structure || definition.Kind.Armed || definition.Kind.Blast > 0f ? 3 : 1;
                        for (int i = 0; i < cookOff; i++)
                        {
                            Vector3 at = e.Position + new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f)) * definition.Kind.Radius * 0.6f;
                            _secondaries.Add(new Vector4(at.x, at.y + 1f, at.z, 0.35f + i * UnityEngine.Random.Range(0.35f, 0.6f)));
                        }
                        if (!definition.Friendly)
                        {
                            _save.Stats.Kills++;
                            ShowMessage(Loc.T("msg.destroyed", Loc.T(definition.Kind.NameKey)), 1.6f);
                            // The kill that finishes the job is worth a long look.
                            ObjectiveDefinition current = _run.CurrentObjective;
                            bool last = current == null || current.Type != ObjectiveType.Destroy || _run.Remaining(current) == 0;
                            if (last || definition.Kind.Structure)
                            {
                                _slowMotion = last ? 1.5f : 0.9f;
                                if (last)
                                {
                                    // The camera leaves the aircraft for a look at what it has done.
                                    _killCam = 1.3f;
                                    _killPoint = e.Position;
                                    Radio("radio.target_down");
                                }
                            }
                            if (distance < 90f) Buzz();
                        }
                        break;
                    }
                    case CombatEventType.IncomingFire:
                    {
                        // Most rounds go wide; the ones that count are in the hull gauge.
                        Vector3 muzzle = _world.Muzzle(e.Target);
                        _effects.MuzzleFlash(muzzle, 1.4f);
                        _effects.Tracer(muzzle, drone + UnityEngine.Random.insideUnitSphere * 2.5f);
                        if (_gunSoundClock <= 0f)
                        {
                            _gunSoundClock = 0.36f;
                            _sfx.Play(Sound.Gun, Mathf.Lerp(0.9f, 0.3f, distance / 200f));
                        }
                        break;
                    }
                    case CombatEventType.AllyUnderFire:
                    {
                        TargetState ally = _run.Target(e.Target);
                        _effects.MuzzleFlash(e.Position, 1.2f);
                        _effects.Tracer(e.Position, ally.Position + Vector3.up * 2f + UnityEngine.Random.insideUnitSphere * 3f);
                        if (_gunSoundClock <= 0f)
                        {
                            _gunSoundClock = 0.36f;
                            _sfx.Play(Sound.Gun, Mathf.Lerp(0.6f, 0.1f, distance / 400f));
                        }
                        break;
                    }
                    case CombatEventType.Rearmed:
                        _sfx.Play(Sound.Success);
                        ShowMessage(Loc.T("msg.rearmed"), 1.4f);
                        break;
                    case CombatEventType.Detonated:
                        _camera.Shake(1f);
                        Buzz();
                        break;
                    case CombatEventType.CannonFire:
                    {
                        Vector3 at = _run.Target(e.Target).Position + Vector3.up * 1.2f + UnityEngine.Random.insideUnitSphere * 1.5f;
                        _effects.MuzzleFlash(drone + Vector3.down * 0.3f, 0.7f);
                        _effects.Tracer(drone, at);
                        _effects.Impact(at);
                        if (_gunSoundClock <= 0f)
                        {
                            _gunSoundClock = 0.3f;
                            _sfx.Play(Sound.Gun, 0.8f);
                        }
                        break;
                    }
                    case CombatEventType.AllyFire:
                    {
                        Vector3 at = _run.Target(e.Target).Position + Vector3.up * 1.5f + UnityEngine.Random.insideUnitSphere * 2f;
                        _effects.MuzzleFlash(e.Position, 1.1f);
                        _effects.Tracer(e.Position, at);
                        if (_gunSoundClock <= 0f)
                        {
                            _gunSoundClock = 0.36f;
                            _sfx.Play(Sound.Gun, Mathf.Lerp(0.5f, 0.1f, distance / 400f));
                        }
                        break;
                    }
                    case CombatEventType.SupportCalled:
                        Radio("radio.support." + Mathf.Clamp((int)e.Size, 1, 3));
                        _sfx.Play(Sound.Lock);
                        break;
                    case CombatEventType.JammerOn:
                        Radio("radio.jammer");
                        break;
                    case CombatEventType.FlaresOut:
                        _effects.Flares(e.Position, _sim.State.Velocity);
                        _sfx.Play(Sound.Release);
                        break;
                    case CombatEventType.GuidedLaunched:
                        _sfx.Play(Sound.Rocket);
                        break;
                    case CombatEventType.SpareUsed:
                        _effects.Explosion(e.Position, 5f);
                        _sfx.Play(Sound.Explosion);
                        _camera.Shake(0.9f);
                        Buzz();
                        break;
                    case CombatEventType.Scattered:
                        if (!_radioedScatter)
                        {
                            _radioedScatter = true;
                            Radio("radio.scatter");
                        }
                        break;
                }
            }
        }

        /// <summary>
        /// Marks a touchdown on the pad for how near the middle and how softly it was made; the
        /// automatic landing earns no mark.
        /// </summary>
        void GradeLanding()
        {
            Vector3 offset = _sim.State.Position - _island.Spawn;
            float miss = new Vector2(offset.x, offset.z).magnitude;
            if (miss > 14f || !_hasFlown || _sim.AutoLanding) return;
            float speed = _sim.LastTouchdownSpeed;
            int grade = miss < 2.5f && speed < 1.2f ? 3 : miss < 6f && speed < 1.8f ? 2 : 1;
            _landingGrade = Mathf.Max(_landingGrade, grade);
            ShowMessage(Loc.T("msg.landing." + grade), 1.6f);
        }

        /// <summary>A short buzz of the phone, if the player has not turned it off.</summary>
        void Buzz()
        {
            if (!_save.Career.Haptics || Time.unscaledTime < _hapticClock) return;
            _hapticClock = Time.unscaledTime + 0.35f;
#if UNITY_ANDROID && !UNITY_EDITOR
            Handheld.Vibrate();
#endif
        }

        /// <summary>
        /// Photo mode: time stops, the instruments go away and the sticks move the camera round
        /// the aircraft. The phone's own screenshot takes the picture.
        /// </summary>
        void TogglePhoto()
        {
            if (_phase != Phase.Mission && _phase != Phase.FreeFlight) return;
            Click();
            _photo = !_photo;
            Time.timeScale = _photo ? 0f : 1f;
            _photoYaw = _sim.State.YawDeg + 150f;
            _hud.SetPhoto(_photo);
        }

        void UpdatePhoto()
        {
            Vector2 look = _sticks.Left.Value, zoom = _sticks.Right.Value;
            float dt = Time.unscaledDeltaTime;
            _photoYaw += look.x * 90f * dt;
            _photoPitch = Mathf.Clamp(_photoPitch + look.y * 60f * dt, -8f, 78f);
            _photoDistance = Mathf.Clamp(_photoDistance - zoom.y * 14f * dt, 3f, 60f);
            _photoYaw += zoom.x * 90f * dt;
        }

        /// <summary>Sets off the delayed blasts queued by a kill; x, y, z are the place and w the seconds left.</summary>
        void UpdateSecondaries(float deltaTime)
        {
            for (int i = _secondaries.Count - 1; i >= 0; i--)
            {
                Vector4 blast = _secondaries[i];
                blast.w -= deltaTime;
                if (blast.w > 0f)
                {
                    _secondaries[i] = blast;
                    continue;
                }
                _secondaries.RemoveAt(i);
                var at = new Vector3(blast.x, blast.y, blast.z);
                float distance = Vector3.Distance(_sim.State.Position, at);
                _effects.Explosion(at, UnityEngine.Random.Range(4f, 7f), false);
                _sfx.Play(Sound.Explosion, Mathf.Lerp(0.6f, 0.15f, distance / 350f));
                _camera.Shake(0.5f - distance / 120f);
            }
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
                case ActionResult.NoLock:
                case ActionResult.NoAmmo:
                    // A thumb held on the button would otherwise repeat this every frame.
                    if (_noticeClock > 0f) break;
                    _noticeClock = 1.2f;
                    _sfx.Play(Sound.Warning);
                    ShowMessage(Loc.T(action == ActionResult.NoLock ? "msg.no_lock" : "msg.no_ammo"), 1.2f);
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
                case FailReason.ShotDown: message = Loc.T("msg.shot_down"); break;
                case FailReason.AllyLost: message = Loc.T("msg.ally_lost"); break;
                case FailReason.Escaped: message = Loc.T("msg.escaped"); break;
                case FailReason.OutOfDrones: message = Loc.T("msg.out_of_drones"); break;
                default: message = Loc.T("fail." + _run.FailReason); break;
            }
            // A drone that is destroyed goes up where it is.
            if (_run.FailReason == FailReason.Destroyed || _run.FailReason == FailReason.ShotDown ||
                _run.FailReason == FailReason.OutOfDrones)
            {
                _effects.Explosion(_sim.State.Position, 5f);
                _sfx.Play(Sound.Explosion);
                _camera.Shake(0.8f);
                _drone.Visible = false;
            }
            _hud.SetSecondary(null);
            _hud.UpdateTargets(_camera.Camera, null, null, null);
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
            _save.Stats.Laps++;
            Announce(Achievements.Collect(_save));
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
            _effects.Wind = weather.WindAt(0f);
            // Smoke is lit by the day it hangs in: grey at noon, nearly black at night.
            Color daylight = weather.AmbientSky * 0.55f + weather.SunColor * (weather.SunIntensity * 0.42f);
            _effects.Daylight = new Color(Mathf.Clamp01(daylight.r * 1.25f), Mathf.Clamp01(daylight.g * 1.25f), Mathf.Clamp01(daylight.b * 1.25f));
            _hud.SetWind(weather.HasWind ? Loc.T("hud.wind", Mathf.RoundToInt(weather.WindSpeed)) : "");
        }

        void ApplyQuality()
        {
            GraphicsTier level = _save.Quality == 0 ? SceneLook.Recommend() : (GraphicsTier)_save.Quality;
            _look.SetQuality(level);
            GlassBackdrop.Allowed = level > GraphicsTier.Low;
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

        /// <summary>Every screen holds text in the old language, so the game is rebuilt from the save.</summary>
        void SetLanguage(string code)
        {
            if (code == Loc.Language)
            {
                Click();
                return;
            }
            _save.Language = code;
            _store.Save(_save);
            Bootstrap.Restart();
        }

        /// <summary>
        /// The device's language if the game has it. The engine's own list has no Uzbek, so on a
        /// phone the system locale is asked directly.
        /// </summary>
        static string DeviceLanguage()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var locale = new AndroidJavaClass("java.util.Locale"))
                using (AndroidJavaObject current = locale.CallStatic<AndroidJavaObject>("getDefault"))
                {
                    string code = current.Call<string>("getLanguage");
                    if (Array.IndexOf(Loc.Languages, code) >= 0) return code;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Skyforge] Could not read the system locale: " + e.Message);
            }
#endif
            return Loc.Detect();
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
            if (SniperMode) return;
            _camera.View = (CameraView)(((int)_camera.View + 1) % 4);
            _drone.Visible = _camera.View != CameraView.Fpv && _camera.View != CameraView.Gun;
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

        public void AutomationShowGlobe(int region, int stars)
        {
            // Give the build machine's empty save enough stars to show open and locked regions.
            for (int i = 0; i < stars / 3; i++) _save.GetOrAdd(_missions[i % _missions.Count].Id).Stars = 3;
            ShowGlobe();
            _globeIndex = region;
            _globe.Focus(region, true);
            RefreshGlobe();
        }

        public void AutomationLoadRegion(string regionId)
        {
            LoadRegion(RegionCatalog.Get(regionId));
            ShowMenu();
        }

        public string AutomationFirstMissionId => _missions[0].Id;

        /// <summary>Restarts the game in another language; the screenshot run carries on in the new game object.</summary>
        public void AutomationSetLanguage(string code)
        {
            _save.Language = code;
            _store.Save(_save);
        }

        public void AutomationShowDaily()
        {
            ShowMenu();
            ShowDaily();
        }

        public void AutomationShowAchievements()
        {
            ShowMenu();
            _achievements.Show(_save);
        }

        public void AutomationShowTalk()
        {
            Negotiation negotiation = Negotiations.For(_mission.Id);
            if (negotiation != null) _talk.Ask(negotiation, _save);
        }

        public void AutomationHideTalk() => _talk.Visible = false;

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
            _mission = AppliedMission();
            // Shots are staged on any mission, whatever the campaign progress of the build machine.
            _save.GetOrAdd(_missions[Mathf.Max(0, _missionIndex - 1)].Id).Completions = 1;
        }

        public void AutomationSetCamera(CameraView view)
        {
            _camera.View = view;
            _drone.Visible = view != CameraView.Fpv && view != CameraView.Gun;
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

        /// <summary>Presses a weapon button, as the player's thumb would.</summary>
        public void AutomationFire(bool bomb)
        {
            if (bomb) _bombPressed = true;
            else _actionPressed = true;
        }

        public int AutomationKills => _run != null ? _run.Kills : 0;
        public int AutomationLock => _run != null ? _run.LockedTarget : -1;

        /// <summary>A blast for the camera, where no mission would put one.</summary>
        public void AutomationExplosion(Vector3 at, float radius) => _effects.Explosion(at, radius);

        void RefreshLabels()
        {
            _hud.SetCameraLabel(Loc.T(_camera.View == CameraView.Chase ? "hud.camera.chase"
                : _camera.View == CameraView.Fpv ? "hud.camera.fpv" : _camera.View == CameraView.Gun ? "hud.camera.gun" : "hud.camera.top"));
            _hud.SetZoomLabel(Loc.T("hud.zoom", Mathf.RoundToInt(_zoom)));
            _hud.SetModeLabel(Loc.T(_sim.Mode == FlightMode.Assist ? "hud.mode.assist" : "hud.mode.sport"));
        }
    }
}
