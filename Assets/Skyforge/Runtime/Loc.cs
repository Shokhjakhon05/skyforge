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
            { "hud.menu", new[] { "MENU", "МЕНЮ" } },
            { "hud.battery", new[] { "BAT", "БАТ" } },
            { "hud.signal", new[] { "SIG", "СИГ" } },
            { "hud.hull", new[] { "HULL", "КОРП" } },
            { "hud.timer", new[] { "{0}  /  {1}", "{0}  /  {1}" } },
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
            { "hint.scan", new[] { "Hover inside the orange ring above the beacon", "Зависни внутри оранжевого кольца над маяком" } },
            { "warn.battery", new[] { "LOW BATTERY - RETURN TO THE PAD", "НИЗКИЙ ЗАРЯД - ВОЗВРАЩАЙСЯ НА ПЛОЩАДКУ" } },
            { "warn.signal", new[] { "WEAK SIGNAL - TURN BACK", "СЛАБЫЙ СИГНАЛ - ВЕРНИСЬ БЛИЖЕ" } },
            { "warn.signal_lost", new[] { "SIGNAL LOST - {0}", "СИГНАЛ ПОТЕРЯН - {0}" } },
            { "objective.takeoff", new[] { "Take off", "Взлети" } },
            { "objective.scan", new[] { "Scan beacon {0}/{1}", "Просканируй маяк {0}/{1}" } },
            { "objective.scanning", new[] { "SCANNING {0}%", "СКАНИРОВАНИЕ {0}%" } },
            { "objective.land", new[] { "Return and land on the pad", "Вернись и сядь на площадку" } },
            { "msg.complete", new[] { "MISSION COMPLETE", "МИССИЯ ВЫПОЛНЕНА" } },
            { "msg.scanned", new[] { "BEACON SCANNED", "МАЯК ПРОСКАНИРОВАН" } },
            { "msg.battery", new[] { "BATTERY EMPTY", "БАТАРЕЯ РАЗРЯЖЕНА" } },
            { "msg.signal", new[] { "SIGNAL LOST", "СИГНАЛ ПОТЕРЯН" } },
            { "menu.operation", new[] { "OPERATION", "ОПЕРАЦИЯ" } },
            { "menu.credits", new[] { "CREDITS  {0}", "КРЕДИТЫ  {0}" } },
            { "menu.start", new[] { "START MISSION", "НАЧАТЬ МИССИЮ" } },
            { "menu.free", new[] { "FREE FLIGHT", "СВОБОДНЫЙ ПОЛЁТ" } },
            { "menu.best", new[] { "Best time {0}", "Лучшее время {0}" } },
            { "menu.not_flown", new[] { "Not flown yet", "Ещё не пройдена" } },
            { "menu.stars", new[] { "STARS", "ЗВЁЗДЫ" } },
            { "menu.reward", new[] { "Reward: {0} credits, plus {1} for each star", "Награда: {0} кредитов и ещё {1} за каждую звезду" } },
            { "star.complete", new[] { "Complete the mission", "Выполни задание" } },
            { "star.time", new[] { "Finish within {0}", "Уложись в {0}" } },
            { "star.no_damage", new[] { "No damage to the drone", "Без повреждений дрона" } },
            { "mission.first_contact.title", new[] { "First Contact", "Первый контакт" } },
            { "mission.first_contact.brief", new[] {
                "Three weather beacons on the island have gone silent. Fly to each one, hover above it until the scan completes, then bring the drone home.",
                "Три метеомаяка на острове перестали выходить на связь. Долети до каждого, зависни над ним до конца сканирования и верни дрон на базу." } },
            { "result.success", new[] { "MISSION COMPLETE", "МИССИЯ ВЫПОЛНЕНА" } },
            { "result.failed", new[] { "MISSION FAILED", "МИССИЯ ПРОВАЛЕНА" } },
            { "result.time", new[] { "Time {0}, goal {1}", "Время {0}, цель {1}" } },
            { "result.damaged", new[] { "The drone was damaged", "Дрон получил повреждения" } },
            { "result.reward", new[] { "+{0} credits", "+{0} кредитов" } },
            { "result.no_reward", new[] { "No reward. Try again.", "Награды нет. Попробуй ещё раз." } },
            { "result.yes", new[] { "DONE", "ЕСТЬ" } },
            { "result.no", new[] { "MISSED", "НЕТ" } },
            { "result.retry", new[] { "RETRY", "ПОВТОРИТЬ" } },
            { "result.continue", new[] { "CONTINUE", "ПРОДОЛЖИТЬ" } },
            { "fail.Destroyed", new[] { "The drone was destroyed", "Дрон разбит" } },
            { "fail.BatteryEmpty", new[] { "The battery ran out", "Батарея разрядилась" } },
            { "fail.SignalLost", new[] { "Contact with the drone was lost", "Связь с дроном потеряна" } },
            { "fail.None", new[] { "", "" } },
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
