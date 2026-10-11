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
            yield return Frames(30);
            yield return Shot("01_menu");

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
