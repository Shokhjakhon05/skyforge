using System.Collections;
using System.IO;
using Skyforge;
using UnityEngine;

namespace Sniper
{
    /// <summary>
    /// Plays the game by itself inside the build machine's editor and saves pictures of it: nobody
    /// who works on this project can run it, so these frames are the only way it is ever seen.
    /// </summary>
    public sealed class SniperShots : MonoBehaviour
    {
        const int Width = 1600, Height = 720;
        SniperGame _game;

        IEnumerator Start()
        {
            _game = GetComponent<SniperGame>();
            _game.SkipIntro = true;
            yield return Frames(20);
            SniperSplash splash = _game.AutomationSplash(1.5f);
            yield return Shot("00_splash_aim", 2);
            splash.Freeze(2.9f);
            yield return Shot("00_splash_name", 2);
            Destroy(splash.gameObject);
            yield return Frames(10);
            yield return Shot("01_menu");

            // Everyone the game can put in a scene, side by side, each doing something different.
            yield return Parade();

            // The place itself, from where nobody plays: is it a quay or a heap of boxes?
            _game.AutomationLook(new Vector3(-60f, 40f, -120f), new Vector3(0f, 2f, 20f), 55f);
            yield return Shot("02_docks_high");
            _game.AutomationLook(new Vector3(30f, 9f, -34f), new Vector3(-4f, 2f, 10f), 55f);
            yield return Shot("03_docks_low");
            _game.AutomationLook(new Vector3(-2f, 2.2f, -9f), new Vector3(-4f, 1.4f, 2f), 45f);
            yield return Shot("04_docks_man");

            _game.AutomationLook(new Vector3(-30f, 3f, 10f), new Vector3(20f, 4f, 30f), 60f);
            yield return Shot("04b_docks_lane");
            _game.AutomationLook(new Vector3(0f, 26f, -150f), new Vector3(0f, 6f, 20f), 46f);
            yield return Shot("04c_docks_from_perch");
            _game.AutomationBriefing("docks1");
            yield return Shot("05_briefing");
            _game.AutomationPlay("docks1");
            yield return Frames(20);
            yield return Shot("06_play_wide");
            _game.AutomationZoom(2);
            _game.AutomationAim(_game.AutomationHead(0), false);
            yield return Frames(8);
            yield return Shot("07_scope_x4", 2);
            _game.AutomationZoom(3);
            _game.AutomationAim(_game.AutomationHead(0), true);
            yield return Frames(4);
            yield return Shot("08_scope_x6", 2);
            _game.AutomationFollow = true;
            yield return Frames(2);
            _game.AutomationFire();
            yield return Frames(3);
            yield return Shot("09_bullet_cam", 1);
            yield return Frames(10);
            yield return Shot("10_bullet_cam_later", 1);
            for (int i = 0; i < 400 && _game.AutomationPlaying; i++) yield return null;
            yield return Shot("11_result");

            _game.AutomationPlay("docks3");
            yield return Frames(120);
            _game.AutomationZoom(2);
            _game.AutomationAim(_game.AutomationHead(0), false);
            yield return Frames(3);
            yield return Shot("12_boss_walk", 1);
            _game.AutomationFollow = false;
            _game.AutomationAim(new Vector3(-30f, 1f, 30f), false);
            yield return Frames(3);
            _game.AutomationFire();
            yield return Frames(200);
            _game.AutomationAim(_game.AutomationHead(1), false);
            yield return Shot("13_alarm_guard", 1);
            yield return Frames(200);
            yield return Shot("14_under_fire", 1);

            if (ShotDirector.Quick)
            {
                ShotDirector.Finished = true;
                yield break;
            }

            // The other two places: from above, from the street, from the rifle.
            _game.AutomationMenu("town");
            yield return Frames(10);
            yield return Shot("20_town_menu");
            _game.AutomationLook(new Vector3(-70f, 44f, -120f), new Vector3(0f, 4f, 24f), 55f);
            yield return Shot("21_town_high");
            _game.AutomationLook(new Vector3(-46f, 3f, 2f), new Vector3(20f, 5f, 26f), 60f);
            yield return Shot("22_town_street");
            _game.AutomationPlay("town3");
            yield return Frames(60);
            yield return Shot("23_town_wide", 1);
            _game.AutomationZoom(2);
            _game.AutomationAim(_game.AutomationHead(0), false);
            yield return Frames(3);
            yield return Shot("24_town_scope", 1);
            _game.AutomationZoom(3);
            yield return Shot("25_town_scope_x6", 2);

            _game.AutomationMenu("fort");
            yield return Frames(10);
            yield return Shot("30_fort_menu");
            _game.AutomationLook(new Vector3(-80f, 50f, -130f), new Vector3(0f, 3f, 24f), 55f);
            yield return Shot("31_fort_high");
            _game.AutomationLook(new Vector3(-20f, 3f, -4f), new Vector3(10f, 4f, 40f), 60f);
            yield return Shot("32_fort_yard");
            _game.AutomationPlay("fort6");
            yield return Frames(60);
            yield return Shot("33_fort_wide", 1);
            _game.AutomationZoom(3);
            _game.AutomationAim(_game.AutomationHead(0), false);
            yield return Frames(3);
            yield return Shot("34_fort_scope", 1);

            // The three places added last: behind the menu, from the rifle, through the glass.
            string[] added = { "air", "rail", "snow" };
            for (int i = 0; i < added.Length; i++)
            {
                string place = added[i];
                _game.AutomationWin(36);
                _game.AutomationMenu(place);
                yield return Frames(10);
                yield return Shot("4" + i + "a_" + place + "_menu");
                _game.AutomationLook(new Vector3(-70f, 46f, -120f), new Vector3(0f, 3f, 24f), 55f);
                yield return Shot("4" + i + "b_" + place + "_high");
                _game.AutomationBriefing(place + "5");
                yield return Shot("4" + i + "c_" + place + "_briefing");
                _game.AutomationPlay(place + "1");
                yield return Frames(40);
                yield return Shot("4" + i + "d_" + place + "_wide", 1);
                _game.AutomationZoom(4);
                _game.AutomationAim(_game.AutomationHead(0), false);
                yield return Frames(3);
                yield return Shot("4" + i + "e_" + place + "_scope_x8", 1);
            }

            // A drum of fuel beside two men.
            _game.AutomationPlay("air3");
            yield return Frames(30);
            _game.AutomationZoom(3);
            _game.AutomationAim(new Vector3(-40f, 0.6f, 10f), true);
            yield return Frames(3);
            yield return Shot("50_drum_before", 1);
            _game.AutomationFire();
            yield return Frames(22);
            yield return Shot("51_drum_after", 1);
            yield return Frames(40);
            yield return Shot("52_drum_later", 1);

            _game.AutomationMenu("docks");
            yield return Frames(6);
            _game.AutomationHunt();
            yield return Shot("53_hunt_briefing");
            _game.AutomationMenu("docks");
            yield return Frames(6);
            _game.AutomationCareer();
            yield return Shot("54_career");
            _game.AutomationSettings();
            yield return Shot("55_settings");

            _game.AutomationCash(30000);
            _game.AutomationArmory();
            yield return Shot("15_armory");
            _game.AutomationLanguage("ru");
            _game.AutomationMenu();
            yield return Frames(10);
            yield return Shot("16_menu_ru");
            _game.AutomationBriefing("docks3");
            yield return Shot("17_briefing_ru");
            _game.AutomationLanguage("en");

            ShotDirector.Finished = true;
        }

        IEnumerator Parade()
        {
            string[] people = { "swat", "guard", "security", "suit", "casual", "worker", "hoodie", "punk", "farmer", "adventurer", "woman", "lady" };
            string[] doing = { "Idle_Gun", "Idle_Gun_Pointing", "Walk_Gun", "Idle", "Walk", "Interact", "Phone", "Run", "Smoke", "Terrified", "Wave", "Idle" };
            string[] falling = { "Death", "Death_Head", "Death_Back", "Death_Knees", "HitReact", "Run_Gun", "Idle_Gun_Shoot", "Death", "Death_Head", "Death_Back", "Death_Knees", "Walk" };
            var row = new GameObject("Parade").transform;
            var actors = new Actor[people.Length];
            for (int i = 0; i < people.Length; i++)
            {
                var prefab = Resources.Load<GameObject>("Chars/" + people[i]);
                if (prefab == null) continue;
                GameObject made = Instantiate(prefab, row);
                actors[i] = made.GetComponent<Actor>();
                float height = actors[i] != null ? actors[i].Height : 1.8f;
                made.transform.localScale = Vector3.one * (1.8f / Mathf.Max(0.01f, height));
                made.transform.position = new Vector3(-8.25f + (i % 6) * 1.5f - 30f, 0f, -60f + (i / 6) * 2.5f);
                made.transform.rotation = Quaternion.Euler(0f, 180f, 0f);
                if (actors[i] != null && !actors[i].Play(doing[i], true)) actors[i].Play("Idle", true);
            }
            yield return Frames(25);
            _game.AutomationLook(new Vector3(-34.5f, 1.7f, -67.5f), new Vector3(-34.5f, 1.0f, -59f), 50f);
            yield return Shot("01b_people", 2);
            _game.AutomationLook(new Vector3(-36.5f, 1.5f, -63.2f), new Vector3(-36.3f, 1.25f, -60f), 40f);
            yield return Shot("01c_people_near", 2);
            for (int i = 0; i < actors.Length; i++)
                if (actors[i] != null) actors[i].Play(falling[i], falling[i].StartsWith("Run") || falling[i].StartsWith("Walk") || falling[i].EndsWith("Shoot"));
            yield return Frames(22);
            _game.AutomationLook(new Vector3(-34.5f, 1.7f, -67.5f), new Vector3(-34.5f, 1.0f, -59f), 50f);
            yield return Shot("01d_people_falling", 1);
            yield return Frames(70);
            yield return Shot("01e_people_down", 1);
            Destroy(row.gameObject);
        }

        static IEnumerator Frames(int count)
        {
            for (int i = 0; i < count; i++) yield return null;
        }

        IEnumerator Shot(string name, int settle = 6)
        {
            yield return Frames(settle);
            Camera cam = _game.AutomationCamera;
            Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsSortMode.None);
            var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            try
            {
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
                File.WriteAllBytes(Path.Combine(ShotDirector.OutputDir, name + ".png"), picture.EncodeToPNG());
                Destroy(picture);
                Debug.Log("[Skyforge] Screenshot " + name);
            }
            finally
            {
                RenderTexture.active = null;
                cam.targetTexture = null;
                foreach (Canvas canvas in canvases)
                    if (canvas != null && canvas.isRootCanvas) canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                target.Release();
                Destroy(target);
            }
            yield return null;
        }
    }
}
