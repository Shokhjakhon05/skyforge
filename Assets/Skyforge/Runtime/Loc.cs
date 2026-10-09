using System.Collections.Generic;
using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// User-facing strings. Every text on screen goes through <see cref="T"/>, so adding the
    /// remaining languages later means adding columns here, not hunting through the code.
    /// </summary>
    public static class Loc
    {
        const int English = 0, Russian = 1;
        static readonly int Language = Detect();

        static readonly Dictionary<string, string[]> Table = new Dictionary<string, string[]>
        {
            { "hud.camera.chase", new[] { "CAMERA: CHASE", "КАМЕРА: СЗАДИ" } },
            { "hud.camera.fpv", new[] { "CAMERA: FPV", "КАМЕРА: FPV" } },
            { "hud.mode.assist", new[] { "MODE: ASSIST", "РЕЖИМ: АССИСТ" } },
            { "hud.mode.sport", new[] { "MODE: SPORT", "РЕЖИМ: СПОРТ" } },
            { "hud.sticks", new[] { "SWAP STICKS", "СТИКИ" } },
            { "hud.reset", new[] { "RESET", "СБРОС" } },
            { "hud.speed", new[] { "{0} km/h", "{0} км/ч" } },
            { "hud.altitude", new[] { "ALT {0} m", "ВЫС {0} м" } },
            { "hud.distance", new[] { "{0} m", "{0} м" } },
            { "hud.rings", new[] { "RINGS {0}/{1}   {2}", "КОЛЬЦА {0}/{1}   {2}" } },
            { "hud.best", new[] { "BEST {0}", "РЕКОРД {0}" } },
            { "msg.crash", new[] { "CRASH", "АВАРИЯ" } },
            { "msg.finished", new[] { "LAP COMPLETE  {0}", "КРУГ ПРОЙДЕН  {0}" } },
            { "msg.record", new[] { "NEW RECORD  {0}", "НОВЫЙ РЕКОРД  {0}" } },
            { "hint.move", new[] { "MOVE", "ДВИЖЕНИЕ" } },
            { "hint.lift", new[] { "UP / DOWN · TURN", "ВВЕРХ / ВНИЗ · ПОВОРОТ" } },
            { "hint.takeoff", new[] { "Push this stick up to take off", "Потяни этот стик вверх, чтобы взлететь" } },
            { "hint.rings", new[] { "Fly through the orange ring", "Пролети через оранжевое кольцо" } },
        };

        static int Detect()
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Russian:
                case SystemLanguage.Ukrainian:
                case SystemLanguage.Belarusian:
                    return Russian;
                default:
                    return English;
            }
        }

        public static string T(string key)
        {
            if (Table.TryGetValue(key, out string[] row)) return row[Language];
            Debug.LogWarning("[Skyforge] Missing string " + key);
            return key;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);
    }
}
