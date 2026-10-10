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
    public sealed class Game : MonoBehaviour
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
            _look = new SceneLook(transform, _camera.Camera, null);
            _effects = new Effects(transform);
            GlassBackdrop.Attach(_camera.Camera);
            UiKit.Tap = () => _sfx.Play(Sound.Tap, 0.7f);
            ApplyQuality();
            AudioListener.volume = _save.Muted ? 0f : 1f;

            LoadRegion(RegionCatalog.Get(_save.Region));
            ShowDroneModel(Garage.Selected(_save));

            _hud = new Hud(transform);
            _hud.CameraPressed += ToggleCamera;
            _hud.ModePressed += ToggleMode;
            _hud.SticksPressed += ToggleSticks;
            _hud.MenuPressed += () => { Click(); ShowMenu(); };
            _hud.ZoomPressed += ToggleZoom;
            _hud.ActionPressed += () => _actionPressed = true;
            _hud.BombPressed += () => _bombPressed = true;
            _hud.LandPressed += ToggleAutoLand;

            _menu = new MenuScreen(transform);
            _menu.SetMap(TacticalMap.Build(_island), _island.Map.HalfSize);
            _menu.StartPressed += () => { Click(); StartMission(); };
            _menu.FreeFlightPressed += () => { Click(); StartFreeFlight(); };
            _menu.SettingsPressed += () => { Click(); ShowSettings(); };
            _menu.WeatherPressed += NextFreeFlightWeather;
            _menu.PreviousPressed += () => SelectMission(_missionIndex - 1);
            _menu.NextPressed += () => SelectMission(_missionIndex + 1);
            _menu.HangarPressed += () => { Click(); ShowHangar(); };

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
            _settings.LanguagePressed += SetLanguage;
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
            _menu.Show(_mission, _save, _missionIndex + 1, _missions.Count, IsLocked(_missionIndex), Loc.T(_region.NameKey));
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
            DroneSpec spec = Garage.SelectedSpec(_save);
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
            // A one-way attack drone turns a mission of pure destruction into a kamikaze sortie;
            // anything that asks for a photograph, a delivery or a landing it cannot fly at all.
            DroneSpec chosen = Garage.SelectedSpec(_save);
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
            _phase = Phase.Mission;
            _analytics.Event("mission_start", _mission.Id);
            _sim = CreateSim(_mission.Payload, _mission.StrikeDrones > 0);
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
            _hangar.Visible = false;
            _globeScreen.Visible = false;
            _talk.Visible = false;
            _daily.Visible = false;
            _achievements.Visible = false;
            _save.Stats.Flights++;
            // Whatever model the hangar was showing, the drone that flies is the selected one.
            ShowDroneModel(Garage.Selected(_save));
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
            Progress.Grant(_save, _mission, outcome);
            _advanceOnContinue = outcome.Succeeded && _save.Find(_mission.Id).Completions == 1 && _dailySlot < 0;

            // A daily operation pays on top, once, if its twist was honoured.
            string extra = null;
            if (_dailySlot >= 0 && _dailySlot < _dailyOps.Count && outcome.Succeeded)
            {
                DailyOp op = _dailyOps[_dailySlot];
                if (!Daily.Satisfied(outcome, op.Modifier)) extra = Loc.T("daily.not_met");
                else if (Daily.Complete(_save, _today, _dailySlot, op)) extra = Loc.T("daily.completed", op.Credits, op.Parts);
            }
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
            _caches.Update(deltaTime);
            _effects.Update(deltaTime, _camera.Camera.transform);
            if (_toastTimer > 0f)
            {
                _toastTimer -= Time.unscaledDeltaTime;
                if (_toastTimer <= 0f) _toast.text = "";
            }
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
                    ActionResult action = _run.Step(_sim.State, simEvent, _load, _sim.LastTouchdownSpeed, StepSeconds,
                        _actionPressed, _sim.Wind, _zoom, _bombPressed);
                    _actionPressed = _bombPressed = false;
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
                        // The strike drone is gone; show what it did, then hand over the next one.
                        _drone.Visible = false;
                        _respawnAfterFreeze = true;
                        Freeze(Loc.T("msg.next_drone", _run.DronesLeft), 1.5f);
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
            if (_autoLand)
            {
                // Any firm push on a stick takes the drone back from the automatic landing.
                if (move.magnitude > 0.35f || lift.magnitude > 0.35f || _sim.State.Grounded) _autoLand = false;
                else return FlightSim.LandingInput(_sim.State, _island.Spawn);
            }
            return new FlightInput { Move = move, Throttle = lift.y, Yaw = lift.x };
        }

        /// <summary>The automatic landing is offered in the air near the pad, to a drone that is coming back.</summary>
        bool CanAutoLand()
        {
            if (_sim.State.Grounded || _freezeTimer > 0f) return false;
            if (_phase == Phase.Mission && _run.Kamikaze) return false;
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
            bool gun = _camera.View == CameraView.Gun;
            if (gun != _look.Scope) _look.SetScope(gun);
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

            bool armed = live && _run.IsCombat && !_run.Kamikaze && objective.Type != ObjectiveType.Photo &&
                         objective.Type != ObjectiveType.Strike;
            if (armed)
            {
                // The buttons carry the ammunition count; their text is rebuilt only when it changes.
                int ammo = _run.Rockets * 100 + _run.Bombs;
                if (ammo != _shownAmmoStamp)
                {
                    _shownAmmoStamp = ammo;
                    _hud.SetAction(Loc.T("hud.rockets", _run.Rockets));
                    _hud.SetSecondary(Loc.T("hud.bombs", _run.Bombs));
                }
                _hud.SetActionReady(_run.LockedTarget >= 0);
                if (_run.LockedTarget >= 0 && _lastLock < 0) _sfx.Play(Sound.Lock, 0.6f);
                _lastLock = _run.LockedTarget;
            }
            else
            {
                string action = null;
                if (live && objective.Type == ObjectiveType.Photo) action = Loc.T("hud.action.photo");
                if (live && objective.Type == ObjectiveType.Strike && _run.ShotsLeft > 0 && !_run.Shot.Active)
                    action = Loc.T("hud.action.fire");
                _hud.SetAction(action);
                _hud.SetActionReady(true);
                _hud.SetSecondary(null);
                _shownAmmoStamp = -1;
            }
            _hud.UpdateTargets(_camera.Camera, live && _run.IsCombat ? _run : null, _mission,
                objective != null && objective.Type == ObjectiveType.Destroy ? objective : null);
            _hud.UpdateScope(_run, _mission, objective != null && objective.Type == ObjectiveType.Destroy ? objective : null,
                now.Position, now.YawDeg);
            _hud.SetDamage(_run.UnderFire && _freezeTimer <= 0f, Time.deltaTime);
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
            if (!_hasFlown) return Loc.T("hint.takeoff");
            if (objective == null) return "";
            if (_run.IsCombat)
            {
                if (AllyUnderFire()) return Loc.T("warn.ally");
                if (_run.Kamikaze) return Loc.T("hint.kamikaze");
                if (objective.Type == ObjectiveType.Destroy && _run.Rockets <= 0 && _run.Bombs <= 0) return Loc.T("hint.rearm");
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
                switch (e.Type)
                {
                    case CombatEventType.RocketFired:
                        _sfx.Play(Sound.Rocket, 0.8f);
                        break;
                    case CombatEventType.BombDropped:
                        _sfx.Play(Sound.Release);
                        break;
                    case CombatEventType.Explosion:
                        _effects.Explosion(e.Position, e.Size);
                        _sfx.Play(Sound.Explosion, Mathf.Lerp(1f, 0.25f, distance / 350f));
                        _camera.Shake(1.2f - distance / (e.Size * 9f));
                        break;
                    case CombatEventType.TargetDestroyed:
                    {
                        TargetDefinition definition = _mission.Targets[e.Target];
                        _effects.Explosion(e.Position + Vector3.up * 1.5f, e.Size);
                        _sfx.Play(Sound.Explosion, Mathf.Lerp(1f, 0.35f, distance / 400f));
                        _camera.Shake(1.3f - distance / (e.Size * 10f));
                        if (!definition.Friendly)
                        {
                            _save.Stats.Kills++;
                            ShowMessage(Loc.T("msg.destroyed", Loc.T(definition.Kind.NameKey)), 1.6f);
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
                        break;
                }
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
