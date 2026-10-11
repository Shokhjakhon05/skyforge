using System.Collections;
using System.IO;
using Skyforge.Core;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// Stages the game and saves screenshots. The CI build runs the game in the editor with this
    /// attached, because nobody on the project can look at an editor window: the pictures are the
    /// only view of the game short of installing the APK. Never active in a player build.
    /// </summary>
    public sealed class ShotDirector : MonoBehaviour
    {
        /// <summary>Set by the build script; null everywhere else.</summary>
        public static string OutputDir;
        public static bool Finished;
        /// <summary>0: the main run in English. N: after a restart in the Nth other language, shots that check its text.</summary>
        public static int Stage;
        /// <summary>A short run: the menu and the imported characters only.</summary>
        public static bool Quick;
        static readonly string[] OtherLanguages = { "ru", "uz" };

        const int Width = 1600, Height = 720;
        Game _game;

        IEnumerator Start()
        {
            _game = GetComponent<Game>();
            _game.SkipIntro = true;
            // The splash is caught mid-flourish on the first start only, then cleared out of the way.
            if (Stage == 0)
            {
                yield return Frames(3);
                yield return Shot("00_splash");
            }
            _game.AutomationSkipSplash();
            yield return Frames(40);

            if (Stage > 0)
            {
                // A restart in another language: the screens with the most text, to see that it fits.
                string code = OtherLanguages[Stage - 1];
                string prefix = "2" + Stage + "_" + code + "_";
                yield return Shot(prefix + "a_menu");
                _game.AutomationShowSettings();
                yield return Shot(prefix + "b_settings");
                _game.AutomationHideSettings();
                _game.AutomationShowHangar("kestrel", 1, 2500);
                yield return Shot(prefix + "c_hangar_modules");
                _game.AutomationShowHangar("albatross", 0, 2500);
                yield return Shot(prefix + "d_hangar_upgrades");
                _game.AutomationSelectMission("first_strike");
                _game.AutomationShowMenu();
                yield return Shot(prefix + "d2_menu_combat");
                _game.AutomationStartMission();
                _game.AutomationPlace(_game.Island.Anchors["poi0"] + new Vector3(0f, 24f, -80f), 0f);
                yield return Shot(prefix + "e_mission_assault");
                _game.AutomationSelectMission("fpv_strike");
                _game.AutomationStartMission();
                _game.AutomationPlace(_game.Island.Anchors["poi9"] + new Vector3(0f, 22f, -70f), 0f);
                yield return Shot(prefix + "f_mission_kamikaze");
                _game.AutomationShowResult();
                yield return Shot(prefix + "g_result");
                _game.AutomationShowDaily();
                yield return Shot(prefix + "h_daily");
                _game.AutomationShowAchievements();
                yield return Shot(prefix + "i_achievements");
                _game.AutomationSelectMission("broken_arrow");
                _game.AutomationShowMenu();
                yield return Shot(prefix + "j_menu_major");
                _game.AutomationShowTalk();
                yield return Shot(prefix + "k_negotiation");
                _game.AutomationHideTalk();
                _game.AutomationShowGlobe(2, 24);
                yield return Frames(20);
                yield return Shot(prefix + "l_globe");
                _game.AutomationShowMenu();

                if (Stage < OtherLanguages.Length)
                {
                    _game.AutomationSetLanguage(OtherLanguages[Stage]);
                    Stage++;
                    Bootstrap.Restart();
                    yield break;
                }
                _game.AutomationSetLanguage("");
                Stage = 0;
                Finished = true;
                yield break;
            }

            Island island = _game.Island;
            Vector3 spawn = island.Spawn;
            Vector3 overview = spawn + new Vector3(0f, 34f, -20f);
            Vector3 beacon = island.Beacons[0];
            Vector3 nearBeacon = beacon + new Vector3(-24f, Island.BeaconHeight + 12f, -24f);
            Vector3 sea = new Vector3(0f, 7f, -island.Map.HalfSize * 0.86f);

            yield return Shot("01_menu");

            if (Quick)
            {
                // A short run that only answers one question: do the imported characters stand, walk and fall?
                _game.AutomationFreeFlight(WeatherCatalog.Clear);
                _game.AutomationSetCamera(CameraView.Fpv);
                _game.AutomationHud(false);
                _game.AutomationPlace(spawn + new Vector3(0f, 1.5f, -6f), 0f);
                var actors = new System.Collections.Generic.List<Actor>();
                string[] names = { "swat", "soldier" };
                for (int i = 0; i < names.Length; i++)
                {
                    var prefab = Resources.Load<GameObject>("Chars/" + names[i]);
                    Debug.Log("[Skyforge] quick: prefab " + names[i] + " " + (prefab != null));
                    if (prefab == null) continue;
                    GameObject made = Instantiate(prefab, spawn + new Vector3(i * 2.4f - 1.2f, 0.05f, 0f), Quaternion.Euler(0f, 180f, 0f));
                    Actor actor = made.GetComponent<Actor>();
                    made.transform.localScale = Vector3.one * (1.8f / actor.Height);
                    actors.Add(actor);
                }
                string[] plays = { "Idle", "Walk", "Run", "Idle_Shoot", "Death" };
                foreach (string play in plays)
                {
                    foreach (Actor actor in actors)
                        if (!actor.Play(play, play != "Death")) actor.Play(play == "Walk" ? "Run" : "Idle", true);
                    yield return Frames(14);
                    yield return Shot("40_actor_" + play.ToLowerInvariant(), 1);
                }
                Finished = true;
                yield break;
            }

            _game.AutomationShowSettings();
            yield return Shot("02_settings");
            _game.AutomationHideSettings();

            // 1.3 first, so a slow run still brings these back: the vehicles, the rifle, the instructor, loot and chains, and frames for the icon.
            Vector3 raid = _game.AutomationGround(false, 3);
            _game.AutomationSetCamera(CameraView.Chase);
            yield return Frames(12);
            yield return Shot("19a_armour_start");
            _game.AutomationLesson("drive", 0);
            _game.AutomationPlace(raid + new Vector3(0f, 0f, -130f), 0f);
            yield return Frames(20);
            yield return Shot("19b_armour_engage");
            for (int frame = 0; frame < 70; frame++)
            {
                if (frame % 6 == 0) _game.AutomationFire(false);
                yield return null;
            }
            yield return Shot("19c_armour_firing", 1);
            _game.AutomationShowMenu();
            _game.AutomationShowHangar("rhino", 0, 20000);
            yield return Shot("19d_hangar_rhino");
            _game.AutomationShowHangar("bulldog", 0, 20000);
            yield return Shot("19e_hangar_bulldog");
            Vector3 man = _game.AutomationGround(true, 6);
            yield return Frames(10);
            _game.AutomationSniperAim(man + Vector3.up);
            yield return Shot("19f_sniper_scope");
            _game.AutomationZoom();
            _game.AutomationSniperAim(man + Vector3.up * 1.2f);
            yield return Shot("19g_sniper_zoomed");
            _game.AutomationFire(false);
            yield return Frames(40);
            yield return Shot("19h_sniper_after_shot", 1);
            _game.AutomationShowResult();
            yield return Shot("19i_sniper_result");

            _game.AutomationUseDrone("talon");
            Vector3 strike = _game.AutomationOperation(0, 9);
            _game.AutomationSetCamera(CameraView.Chase);
            _game.AutomationPlace(strike + new Vector3(0f, 26f, -95f), 0f);
            _game.AutomationLesson("fight", 0);
            yield return Frames(20);
            yield return Shot("19j_coach_fight");
            _game.AutomationLesson(null, 0);
            int had = _game.AutomationKills;
            for (int frame = 0; frame < 260 && _game.AutomationKills < had + 3; frame++)
            {
                if (frame % 5 == 0) _game.AutomationFire(frame % 40 == 0);
                yield return null;
            }
            _game.AutomationBanner(3);
            yield return Shot("19k_loot_and_chain", 2);
            _game.AutomationPlace(strike + new Vector3(8f, 14f, -38f), -8f);
            yield return Frames(14);
            yield return Shot("19l_over_the_wrecks", 1);
            _game.AutomationHud(false);
            yield return Shot("30a_hero", 1);
            _game.AutomationPlace(strike + new Vector3(-20f, 10f, -30f), 30f);
            yield return Frames(10);
            yield return Shot("30b_hero", 1);
            _game.AutomationPlace(strike + new Vector3(0f, 18f, -60f), 0f);
            for (int frame = 0; frame < 30; frame++)
            {
                if (frame % 5 == 0) _game.AutomationFire(frame == 10);
                yield return null;
            }
            yield return Shot("30c_hero", 1);
            _game.AutomationHud(true);
            _game.AutomationUseDrone("sparrow");
            _game.AutomationFreeFlight(WeatherCatalog.Clear);
            _game.AutomationPlace(_game.Island.Spawn + new Vector3(-30f, 16f, -46f), 30f);
            yield return Shot("19m_camp_by_the_pad");
            _game.AutomationShowMenu();

            _game.AutomationStartMission();
            yield return Shot("03_mission_pad");

            _game.AutomationPlace(overview, 0f);
            yield return Shot("04_mission_overview");

            _game.AutomationPlace(nearBeacon, 45f);
            yield return Shot("05_mission_beacon");

            _game.AutomationSetCamera(CameraView.Fpv);
            yield return Shot("06_mission_beacon_fpv");
            _game.AutomationSetCamera(CameraView.Chase);

            _game.AutomationShowResult();
            yield return Shot("07_result");

            _game.AutomationSetCamera(CameraView.Chase);
            _game.AutomationFreeFlight(WeatherCatalog.Clear);
            _game.AutomationPlace(sea, 180f);
            yield return Shot("08_sea_outward");
            _game.AutomationPlace(sea, 0f);
            yield return Shot("09_sea_towards_island");

            int index = 10;
            foreach (Weather weather in WeatherCatalog.All)
            {
                if (weather.Id == WeatherCatalog.Clear) continue;
                _game.AutomationFreeFlight(weather.Id);
                _game.AutomationPlace(overview, 0f);
                yield return Shot(index++ + "_weather_" + weather.Id);
            }

            // The war on the ground: a shelled village from the air and from among the ruins.
            _game.AutomationFreeFlight(WeatherCatalog.Clear);
            Vector3 village = island.Villages.Count > 0 ? island.Villages[0] : island.Anchors["poi1"];
            _game.AutomationPlace(village + new Vector3(0f, 45f, -110f), 0f);
            yield return Shot("11_village_above");
            _game.AutomationPlace(village + new Vector3(-30f, 14f, -55f), 25f);
            yield return Shot("11_village_low");
            _game.AutomationSetCamera(CameraView.Fpv);
            _game.AutomationPlace(village + new Vector3(10f, 9f, -48f), -10f);
            yield return Shot("11_village_fpv");
            _game.AutomationSetCamera(CameraView.Top);
            _game.AutomationPlace(village + new Vector3(0f, 30f, 0f), 0f);
            yield return Shot("11_village_top");
            _game.AutomationSetCamera(CameraView.Chase);

            // The fights: each one seen from where a player would attack, then with the shooting started.
            yield return Battle("12a", "first_strike", "poi0", new Vector3(0f, 24f, -85f), false);
            yield return Battle("12b", "tank_hunt", "poi1", new Vector3(-10f, 26f, -95f), false);
            yield return Battle("12c", "blind_them", "poi3", new Vector3(0f, 22f, -100f), false);
            yield return Battle("12d", "hammer_fall", "poi7", new Vector3(24f, 26f, -100f), false);
            yield return Battle("12e", "night_raid", "poi2", new Vector3(0f, 24f, -90f), false);
            yield return Battle("12f", "iron_tide", "sea0", new Vector3(0f, 30f, -120f), false);
            yield return Battle("12g", "broken_arrow", "poi2", new Vector3(-10f, 26f, -100f), false);
            yield return MissionShot("12h", "fpv_strike", island.Anchors["poi9"] + new Vector3(0f, 20f, -60f), 0f, CameraView.Chase);
            yield return MissionShot("12i", "fpv_strike", island.Anchors["poi9"] + new Vector3(-6f, 9f, -30f), 0f, CameraView.Fpv);
            yield return MissionShot("12j", "hold_the_line", island.Spawn + new Vector3(20f, 30f, -40f), 40f, CameraView.Chase);
            yield return MissionShot("12k", "convoy_ambush", island.Anchors["coast6"] + new Vector3(0f, 30f, -90f), 0f, CameraView.Chase);
            yield return MissionShot("12l", "coast_watch", island.Anchors["sea3"] + new Vector3(0f, 26f, -90f), 0f, CameraView.Chase);
            yield return MissionShot("12m", "headhunter", island.Anchors["coast1"] + new Vector3(0f, 28f, -80f), 0f, CameraView.Chase);
            yield return MissionShot("12n", "sprint", island.Rings[0] + new Vector3(-30f, 6f, -30f), 45f, CameraView.Chase);
            yield return MissionShot("12o", "silent_path", island.Anchors["poi3"] + new Vector3(0f, 60f, -230f), 0f, CameraView.Chase);
            yield return MissionShot("12p", "supply_run", island.Anchors["poi4"] + new Vector3(0f, 14f, -30f), 0f, CameraView.Chase);
            _game.AutomationSetCamera(CameraView.Chase);

            _game.AutomationSelectMission("hammer_fall");
            _game.AutomationShowMenu();
            yield return Shot("12z_menu_demolition");

            // The hangar, and a few of the drones in the air.
            _game.AutomationShowHangar("sparrow", 0, 2500);
            yield return Shot("13a_hangar_sparrow_upgrades");
            _game.AutomationShowHangar("kestrel", 1, 2500);
            yield return Shot("13b_hangar_kestrel_modules");
            _game.AutomationShowHangar("albatross", 0, 2500);
            yield return Shot("13c_hangar_albatross");
            _game.AutomationShowHangar("aegis", 0, 2500);
            yield return Shot("13d_hangar_aegis");
            _game.AutomationShowHangar("wasp", 0, 2500);
            yield return Shot("13e_hangar_wasp");
            _game.AutomationShowHangar("talon", 0, 2500);
            yield return Shot("13f_hangar_talon");
            _game.AutomationShowHangar("mule", 0, 2500);
            yield return Shot("13g_hangar_mule");
            int drone = 0;
            foreach (string id in new[] { "kestrel", "wasp", "mule", "ghost", "talon", "aegis" })
            {
                _game.AutomationUseDrone(id);
                _game.AutomationFreeFlight(WeatherCatalog.Clear);
                _game.AutomationPlace(overview, 0f);
                yield return Shot("14" + (char)('a' + drone++) + "_drone_" + id);
            }
            _game.AutomationUseDrone("sparrow");

            // The gun camera over a target, and a kamikaze class drone on a strike mission.
            yield return MissionShot("14m", "tank_hunt", island.Anchors["poi1"] + new Vector3(0f, 40f, -110f), 0f, CameraView.Gun);
            _game.AutomationSetCamera(CameraView.Chase);
            _game.AutomationUseDrone("wasp");
            yield return MissionShot("14n", "first_strike", island.Anchors["poi0"] + new Vector3(0f, 24f, -70f), 0f, CameraView.Chase);
            _game.AutomationUseDrone("sparrow");

            // The headquarters: every screen and tab once.
            _game.AutomationShowMenu();
            int[][] pages = { new[] { 1, 0 }, new[] { 1, 1 }, new[] { 1, 2 }, new[] { 1, 3 }, new[] { 2, 0 }, new[] { 2, 1 }, new[] { 2, 3 },
                new[] { 3, 0 }, new[] { 3, 1 }, new[] { 4, 0 }, new[] { 4, 1 }, new[] { 4, 2 } };
            foreach (int[] page in pages)
            {
                _game.AutomationHeadquarters(page[0], page[1]);
                yield return Shot("17_hq_" + page[0] + "_" + page[1]);
            }
            _game.AutomationCloseHeadquarters();
            _game.AutomationTransmission(2);
            yield return Frames(40);
            yield return Shot("17z_transmission");
            _game.AutomationHideTransmission();

            // The generated operations, one frame over each kind's targets.
            string[] kinds = { "strike", "convoy", "defend", "airpatrol", "train", "launcher", "bridge", "rescue", "nighthunt", "platform", "flagship", "offensive" };
            for (int kind = 0; kind < kinds.Length; kind++)
            {
                Vector3 at = _game.AutomationOperation(kind, 9);
                _game.AutomationSetCamera(kind == 8 ? CameraView.Gun : CameraView.Chase);
                _game.AutomationPlace(at + new Vector3(0f, kind >= 9 ? 44f : 30f, kind >= 9 ? -110f : -75f), 0f);
                yield return Frames(kind == 3 ? 60 : 12);
                yield return Shot("18" + (char)('a' + kind) + "_op_" + kinds[kind]);
            }
            _game.AutomationSetCamera(CameraView.Chase);
            Vector3 wave = _game.AutomationOperation(-1, 1);
            _game.AutomationPlace(island.Spawn + new Vector3(40f, 34f, -40f), 40f);
            yield return Frames(50);
            yield return Shot("18m_survival");
            Vector3 range = _game.AutomationOperation(-3, 1);
            _game.AutomationPlace(range + new Vector3(0f, 22f, -70f), 0f);
            yield return Shot("18n_range");

            // The hand-flown missile, seen through its own camera, and a painted aircraft.
            _game.AutomationUseDrone("owl");
            Vector3 mark = _game.AutomationOperation(0, 4);
            _game.AutomationPlace(mark + new Vector3(0f, 40f, -130f), 0f);
            yield return Frames(10);
            _game.AutomationAbility();
            yield return Frames(30);
            yield return Shot("18o_guided_missile");
            _game.AutomationUseDrone("talon");
            _game.AutomationWear("ember");
            _game.AutomationShowHangar("talon", 0, 9000);
            yield return Shot("18p_hangar_painted");
            _game.AutomationUseDrone("sparrow");

            _game.AutomationSelectMission("broken_arrow");
            _game.AutomationShowMenu();
            _game.AutomationShowTalk();
            yield return Shot("14z_negotiation");
            _game.AutomationHideTalk();

            _game.AutomationShowDaily();
            yield return Shot("14y_daily");
            _game.AutomationShowAchievements();
            yield return Shot("14x_achievements");
            _game.AutomationShowMenu();

            // The world map, then a look at the other two regions.
            for (int region = 0; region < RegionCatalog.All.Count; region++)
            {
                _game.AutomationShowGlobe(region, 24);
                yield return Frames(20);
                yield return Shot("15" + (char)('a' + region) + "_globe_" + RegionCatalog.All[region].Id);
            }
            int shot = 0;
            foreach (string regionId in new[] { "kharad", "nordheim", "tessara", "vulkar", "redrock", "meridian" })
            {
                _game.AutomationLoadRegion(regionId);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_menu");
                Island other = _game.Island;
                _game.AutomationSelectMission(_game.AutomationFirstMissionId);
                _game.AutomationStartMission();
                _game.AutomationPlace(other.Spawn + new Vector3(0f, 34f, -20f), 0f);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_overview");
                if (regionId == "meridian")
                {
                    Vector3 town = other.Villages.Count > 0 ? other.Villages[0] : other.Anchors["poi1"];
                    _game.AutomationPlace(town + new Vector3(0f, 34f, -150f), 0f);
                    yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_city");
                }
            }
            _game.AutomationLoadRegion("arvelia");

            // Start again in each other language; the new game object's director takes those shots.
            _game.AutomationSelectMission("first_strike");
            _game.AutomationShowMenu();
            _game.AutomationSetLanguage(OtherLanguages[0]);
            Stage = 1;
            Bootstrap.Restart();
        }

        IEnumerator MissionShot(string prefix, string missionId, Vector3 position, float yawDeg, CameraView view)
        {
            _game.AutomationSelectMission(missionId);
            _game.AutomationStartMission();
            _game.AutomationSetCamera(view);
            _game.AutomationPlace(position, yawDeg);
            yield return Shot(prefix + "_" + missionId + "_" + view.ToString().ToLowerInvariant());
        }

        /// <summary>
        /// A fight in three pictures: the approach, the moment after the weapons are released,
        /// and what is left when the smoke has had a second to rise.
        /// </summary>
        /// <param name="bombs">Attack from overhead with bombs instead of from a distance with rockets.</param>
        IEnumerator Battle(string prefix, string missionId, string anchor, Vector3 offset, bool bombs)
        {
            _game.AutomationSelectMission(missionId);
            _game.AutomationStartMission();
            _game.AutomationSetCamera(CameraView.Chase);
            _game.AutomationPlace(_game.Island.Anchors[anchor] + offset, 0f);
            yield return Shot(prefix + "_" + missionId + "_1_approach");

            // Keep the button down until something dies, or for long enough that nothing will.
            int before = _game.AutomationKills;
            for (int frame = 0; frame < 160 && _game.AutomationKills == before; frame++)
            {
                if (frame % 4 == 0) _game.AutomationFire(bombs);
                yield return null;
            }
            yield return Shot(prefix + "_" + missionId + "_2_hit", 1);
            for (int frame = 0; frame < 30; frame++)
            {
                if (frame % 4 == 0) _game.AutomationFire(bombs);
                yield return null;
            }
            yield return Shot(prefix + "_" + missionId + "_3_burning", 1);
            Debug.Log("[Skyforge] Battle " + missionId + ": kills " + _game.AutomationKills + ", lock " + _game.AutomationLock);
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Lets the scene settle, then renders the camera and the UI into a PNG.</summary>
        /// <param name="settle">Frames to wait first, so that a newly staged scene has been drawn once.</param>
        IEnumerator Shot(string name, int settle = 6)
        {
            yield return Frames(settle);

            Camera cam = _game.AutomationCamera;
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
                // Overlay canvases are not part of a camera render, so they are attached to the camera for the shot.
                foreach (Canvas canvas in canvases)
                {
                    if (!canvas.isRootCanvas) continue;
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = cam;
                    canvas.planeDistance = 1f;
                }
                cam.targetTexture = target;
                Canvas.ForceUpdateCanvases();
                cam.Render();

                RenderTexture.active = target;
                var picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
                picture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
                picture.Apply();
                File.WriteAllBytes(Path.Combine(OutputDir, name + ".png"), picture.EncodeToPNG());
                Destroy(picture);
                Debug.Log("[Skyforge] Screenshot " + name);
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                foreach (Canvas canvas in canvases)
                {
                    if (canvas != null && canvas.isRootCanvas) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                }
                target.Release();
                Destroy(target);
            }
            yield return null;
        }
    }
}
