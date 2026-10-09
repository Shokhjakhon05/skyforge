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
        static readonly string[] OtherLanguages = { "ru", "uz" };

        const int Width = 1600, Height = 720;
        Game _game;

        IEnumerator Start()
        {
            _game = GetComponent<Game>();
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
                _game.AutomationSelectMission("clean_strike");
                _game.AutomationStartMission();
                _game.AutomationPlace(_game.Island.Anchors["poi2"] + new Vector3(6f, 60f, -4f), 0f);
                yield return Shot(prefix + "e_mission_strike");
                _game.AutomationSelectMission("shadow");
                _game.AutomationStartMission();
                _game.AutomationPlace(_game.Island.Anchors["coast7"] + new Vector3(0f, 25f, -50f), 0f);
                yield return Shot(prefix + "f_mission_follow");
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

            _game.AutomationShowSettings();
            yield return Shot("02_settings");
            _game.AutomationHideSettings();

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

            // One staged moment from each kind of mission that puts something new into the world.
            yield return MissionShot("11a", "sprint", island.Rings[0] + new Vector3(-30f, 6f, -30f), 45f, CameraView.Chase);
            yield return MissionShot("11b", "silent_path", island.Anchors["poi3"] + new Vector3(0f, 60f, -230f), 0f, CameraView.Chase);
            yield return MissionShot("11c", "clean_strike", island.Anchors["poi2"] + new Vector3(6f, 60f, -4f), 0f, CameraView.Chase);
            yield return MissionShot("11d", "clean_strike", island.Anchors["poi2"] + new Vector3(6f, 60f, -4f), 0f, CameraView.Top);
            yield return MissionShot("11e", "safe_passage", island.Anchors["poi10"] + new Vector3(0f, 22f, -45f), 0f, CameraView.Chase);
            yield return MissionShot("11f", "shadow", island.Anchors["coast7"] + new Vector3(0f, 25f, -50f), 0f, CameraView.Chase);
            yield return MissionShot("11g", "evidence", island.Anchors["poi0"] + new Vector3(0f, 12f, -32f), 0f, CameraView.Fpv);
            yield return MissionShot("11h", "coast_watch", island.Anchors["poi10"] + new Vector3(0f, 16f, -45f), 0f, CameraView.Chase);
            yield return MissionShot("11i", "supply_run", island.Anchors["poi4"] + new Vector3(0f, 14f, -30f), 0f, CameraView.Chase);
            _game.AutomationSetCamera(CameraView.Chase);

            _game.AutomationSelectMission("clean_strike");
            _game.AutomationShowMenu();
            yield return Shot("12_menu_strike");

            // The hangar, and a few of the drones in the air.
            _game.AutomationShowHangar("sparrow", 0, 2500);
            yield return Shot("13a_hangar_sparrow_upgrades");
            _game.AutomationShowHangar("kestrel", 1, 2500);
            yield return Shot("13b_hangar_kestrel_modules");
            _game.AutomationShowHangar("albatross", 0, 2500);
            yield return Shot("13c_hangar_albatross");
            _game.AutomationShowHangar("aegis", 0, 2500);
            yield return Shot("13d_hangar_aegis");
            int drone = 0;
            foreach (string id in new[] { "viper", "mule", "petrel", "relay", "albatross", "talon" })
            {
                _game.AutomationUseDrone(id);
                _game.AutomationFreeFlight(WeatherCatalog.Clear);
                _game.AutomationPlace(overview, 0f);
                yield return Shot("14" + (char)('a' + drone++) + "_drone_" + id);
            }
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
            foreach (string regionId in new[] { "kharad", "nordheim" })
            {
                _game.AutomationLoadRegion(regionId);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_menu");
                Island other = _game.Island;
                _game.AutomationSelectMission(_game.AutomationFirstMissionId);
                _game.AutomationStartMission();
                _game.AutomationPlace(other.Spawn + new Vector3(0f, 34f, -20f), 0f);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_overview");
                _game.AutomationPlace(other.Anchors["summit"] + new Vector3(0f, 40f, -160f), 0f);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_summit");
                _game.AutomationPlace(new Vector3(0f, 7f, -other.Map.HalfSize * 0.86f), 0f);
                yield return Shot("16" + (char)('a' + shot++) + "_" + regionId + "_from_sea");
            }
            _game.AutomationLoadRegion("arvelia");

            // Start again in each other language; the new game object's director takes those shots.
            _game.AutomationSelectMission("first_contact");
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

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        /// <summary>Lets the scene settle, then renders the camera and the UI into a PNG.</summary>
        IEnumerator Shot(string name)
        {
            yield return Frames(6);

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
