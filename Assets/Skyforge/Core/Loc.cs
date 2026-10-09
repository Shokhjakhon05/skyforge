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
            { "objective.takeoff_again", new[] { "Cargo delivered. Take off", "Груз доставлен. Взлетай" } },
            { "objective.scan", new[] { "Hover inside the ring and scan", "Зависни в кольце и просканируй" } },
            { "objective.scan_source", new[] { "Hover over the transmitter and scan it", "Зависни над передатчиком и просканируй его" } },
            { "objective.scan_relay", new[] { "Scan the relay mast", "Просканируй мачту ретранслятора" } },
            { "objective.scanning", new[] { "SCANNING {0}%", "СКАНИРОВАНИЕ {0}%" } },
            { "objective.land", new[] { "Return and land on the pad", "Вернись и сядь на площадку" } },
            { "objective.deliver", new[] { "Land the cargo on the drop pad", "Посади груз на площадку доставки" } },
            { "objective.reach", new[] { "Fly through the gate", "Пролети через ворота" } },
            { "objective.observe", new[] { "Keep the target in view", "Держи цель в кадре" } },
            { "objective.observing", new[] { "OBSERVING {0}%", "НАБЛЮДЕНИЕ {0}%" } },
            { "objective.photo", new[] { "Frame the target and press PHOTO", "Наведи камеру на цель и нажми ФОТО" } },
            { "objective.follow", new[] { "Follow the truck, keep {0}-{1} m away", "Следуй за грузовиком на дистанции {0}-{1} м" } },
            { "objective.following", new[] { "TRACKING {0}%", "СЛЕЖКА {0}%" } },
            { "objective.find", new[] { "Find the source. Scanner {0}%", "Найди источник. Сканер {0}%" } },
            { "objective.strike", new[] { "Climb above {0} m, aim the red ring, press FIRE", "Поднимись выше {0} м, наведи красное кольцо, жми ОГОНЬ" } },
            { "objective.mark", new[] { "Mark the ambushes before the convoy arrives: {0} left", "Отметь засады до подхода колонны: осталось {0}" } },
            { "objective.marking", new[] { "MARKING {0}%", "ОТМЕТКА {0}%" } },
            { "objective.arrive", new[] { "Stay with the convoy until it arrives", "Сопровождай колонну до прибытия" } },
            { "progress.step", new[] { "STEP {0}/{1}", "ШАГ {0}/{1}" } },
            { "hud.action.photo", new[] { "PHOTO", "ФОТО" } },
            { "hud.action.fire", new[] { "FIRE", "ОГОНЬ" } },
            { "hud.zoom", new[] { "ZOOM x{0}", "ЗУМ x{0}" } },
            { "hud.camera.top", new[] { "CAMERA: TOP", "КАМЕРА: СВЕРХУ" } },
            { "hud.detection", new[] { "SEEN", "ЗАМЕЧЕН" } },
            { "hud.framed", new[] { "IN FRAME", "В КАДРЕ" } },
            { "msg.photo", new[] { "PHOTO TAKEN", "СНИМОК СДЕЛАН" } },
            { "msg.photo_missed", new[] { "TARGET NOT IN FRAME", "ЦЕЛЬ НЕ В КАДРЕ" } },
            { "msg.too_low", new[] { "TOO LOW TO RELEASE", "СЛИШКОМ НИЗКО ДЛЯ СБРОСА" } },
            { "msg.released", new[] { "RELEASED", "СБРОС" } },
            { "msg.hit", new[] { "TARGET HIT", "ЦЕЛЬ ПОРАЖЕНА" } },
            { "msg.miss", new[] { "MISS", "ПРОМАХ" } },
            { "msg.objective", new[] { "DONE", "ВЫПОЛНЕНО" } },
            { "msg.marked", new[] { "AMBUSH MARKED", "ЗАСАДА ОТМЕЧЕНА" } },
            { "msg.detected", new[] { "DETECTED", "ОБНАРУЖЕН" } },
            { "warn.detected", new[] { "YOU ARE SEEN - GET LOWER OR MOVE AWAY", "ТЕБЯ ВИДЯТ - СНИЗЬСЯ ИЛИ ОТОЙДИ" } },
            { "warn.too_close", new[] { "TOO CLOSE - FALL BACK", "СЛИШКОМ БЛИЗКО - ОТСТАНЬ" } },
            { "warn.too_far", new[] { "TOO FAR - CATCH UP", "СЛИШКОМ ДАЛЕКО - ДОГОНЯЙ" } },
            { "fail.Detected", new[] { "The drone was detected", "Дрон обнаружен" } },
            { "fail.Missed", new[] { "The shot missed the target", "Выстрел не попал в цель" } },
            { "fail.Ambushed", new[] { "The convoy ran into an ambush", "Колонна попала в засаду" } },
            { "menu.locked_short", new[] { "LOCKED", "ЗАКРЫТО" } },
            { "menu.locked", new[] { "Complete the previous operation first", "Сначала пройди предыдущую операцию" } },
            { "menu.mission_number", new[] { "OPERATION {0} OF {1}", "ОПЕРАЦИЯ {0} ИЗ {1}" } },
            { "menu.cargo", new[] { "Cargo on board: the drone is slower and heavier.", "Груз на борту: дрон медленнее и тяжелее." } },
            { "menu.low_battery", new[] { "Battery at the start: {0}%.", "Заряд на старте: {0}%." } },
            { "menu.radars", new[] { "Radars: stay below the red rings.", "Радары: держись ниже красных колец." } },
            { "template.scan", new[] { "SCANNING", "СКАНИРОВАНИЕ" } },
            { "template.recon", new[] { "RECONNAISSANCE", "РАЗВЕДКА" } },
            { "template.photo", new[] { "PHOTO EVIDENCE", "ФОТОСЪЁМКА" } },
            { "template.race", new[] { "GATE RACE", "ГОНКА ПО ВОРОТАМ" } },
            { "template.delivery", new[] { "DELIVERY", "ДОСТАВКА" } },
            { "template.search", new[] { "SEARCH", "ПОИСК" } },
            { "template.stealth", new[] { "STEALTH", "СКРЫТНОЕ ПРОНИКНОВЕНИЕ" } },
            { "template.tail", new[] { "SURVEILLANCE", "СЛЕЖКА" } },
            { "template.strike", new[] { "PRECISION STRIKE", "ТОЧЕЧНЫЙ УДАР" } },
            { "template.escort", new[] { "ESCORT", "СОПРОВОЖДЕНИЕ" } },
            { "template.storm", new[] { "STORM FLIGHT", "ПОЛЁТ В ШТОРМ" } },
            { "mission.coast_watch.title", new[] { "Coast Watch", "Береговой дозор" } },
            { "mission.coast_watch.brief", new[] {
                "Someone has set up a camp inland and a boat is waiting offshore. Keep each of them in the camera's view long enough to identify it.",
                "В глубине острова кто-то разбил лагерь, а у берега ждёт катер. Удержи каждый объект в кадре, пока его не опознают." } },
            { "mission.evidence.title", new[] { "Evidence", "Улики" } },
            { "mission.evidence.brief", new[] {
                "Four objects on the island need to be photographed. Get close or use the zoom, frame each one and take the shot.",
                "Четыре объекта на острове нужно сфотографировать. Подлети ближе или включи зум, наведи камеру и сделай снимок." } },
            { "mission.sprint.title", new[] { "Sprint", "Спринт" } },
            { "mission.sprint.brief", new[] {
                "A courier route around the island, ten gates, and a clock. Fly through every gate in order.",
                "Курьерский маршрут вокруг острова: десять ворот и таймер. Пройди все ворота по порядку." } },
            { "mission.supply_run.title", new[] { "Supply Run", "Снабжение" } },
            { "mission.supply_run.brief", new[] {
                "A research team in the hills is out of medicine. Carry the cargo to their drop pad and set it down gently.",
                "У исследователей в холмах закончились медикаменты. Доставь груз на их площадку и посади его мягко." } },
            { "mission.lost_signal.title", new[] { "Lost Signal", "Потерянный сигнал" } },
            { "mission.lost_signal.brief", new[] {
                "An unknown transmitter is broadcasting from somewhere in the fog. There is no marker: follow the scanner reading to its source.",
                "Где-то в тумане работает неизвестный передатчик. Маркера нет: ищи источник по показаниям сканера." } },
            { "mission.silent_path.title", new[] { "Silent Path", "Тихая тропа" } },
            { "mission.silent_path.brief", new[] {
                "Two radars cover the middle of the island. They only see what flies above the red rings. Slip under them to the relay mast.",
                "Центр острова закрыт двумя радарами. Они видят только то, что летит выше красных колец. Проскользни под ними к мачте." } },
            { "mission.shadow.title", new[] { "Shadow", "Тень" } },
            { "mission.shadow.brief", new[] {
                "A courier truck is driving along the coast. Stay close enough to track it and far enough that the driver does not notice you.",
                "Вдоль берега едет грузовик курьера. Держись достаточно близко, чтобы вести его, и достаточно далеко, чтобы водитель тебя не заметил." } },
            { "mission.clean_strike.title", new[] { "Clean Strike", "Чистый удар" } },
            { "mission.clean_strike.brief", new[] {
                "The Cobalt syndicate's command van is parked in the hills. You have one munition. Climb high, let the red ring settle on the van, and release.",
                "Командный фургон синдиката «Кобальт» стоит в холмах. Боеприпас один. Набери высоту, дождись, пока красное кольцо ляжет на фургон, и сбрасывай." } },
            { "mission.safe_passage.title", new[] { "Safe Passage", "Безопасный проход" } },
            { "mission.safe_passage.brief", new[] {
                "A supply convoy is leaving the coast and two ambushes wait on its road. Find and mark both before the trucks reach them.",
                "Колонна снабжения выходит от побережья, на дороге её ждут две засады. Найди и отметь обе до того, как подойдут грузовики." } },
            { "mission.storm_front.title", new[] { "Storm Front", "Грозовой фронт" } },
            { "mission.storm_front.brief", new[] {
                "The storm has arrived and the battery is already low. Pass both gates and get home before the charge runs out.",
                "Шторм уже здесь, а батарея наполовину пуста. Пройди оба шлюза и вернись домой, пока хватает заряда." } },
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
            { "menu.stars", new[] { "STARS:", "ЗВЁЗДЫ:" } },
            { "menu.reward", new[] { "Reward: {0} credits, plus {1} for each star", "Награда: {0} кредитов и ещё {1} за каждую звезду" } },
            { "star.complete", new[] { "complete the mission", "выполни задание" } },
            { "star.time", new[] { "finish within {0}", "уложись в {0}" } },
            { "star.no_damage", new[] { "no damage to the drone", "без повреждений дрона" } },
            { "mission.first_contact.title", new[] { "First Contact", "Первый контакт" } },
            { "mission.first_contact.brief", new[] {
                "Three weather beacons on the island have gone silent. Fly to each one, hover above it until the scan completes, then bring the drone home.",
                "Три метеомаяка на острове перестали выходить на связь. Долети до каждого, зависни над ним до конца сканирования и верни дрон на базу." } },
            { "result.success", new[] { "MISSION COMPLETE", "МИССИЯ ВЫПОЛНЕНА" } },
            { "result.failed", new[] { "MISSION FAILED", "МИССИЯ ПРОВАЛЕНА" } },
            { "result.time", new[] { "Time {0}, goal {1}", "Время {0}, цель {1}" } },
            { "result.completed", new[] { "Mission completed", "Задание выполнено" } },
            { "result.undamaged", new[] { "No damage to the drone", "Дрон без повреждений" } },
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
            { "menu.settings", new[] { "SETTINGS", "НАСТРОЙКИ" } },
            { "menu.change", new[] { "CHANGE", "СМЕНИТЬ" } },
            { "menu.weather", new[] { "Weather: {0}. {1}", "Погода: {0}. {1}" } },
            { "menu.free_weather", new[] { "Free flight weather: {0}", "Погода в свободном полёте: {0}" } },
            { "weather.clear", new[] { "Clear", "Ясно" } },
            { "weather.sunset", new[] { "Sunset", "Закат" } },
            { "weather.overcast", new[] { "Overcast", "Пасмурно" } },
            { "weather.rain", new[] { "Rain", "Дождь" } },
            { "weather.fog", new[] { "Fog", "Туман" } },
            { "weather.storm", new[] { "Storm", "Шторм" } },
            { "weather.dust", new[] { "Dust", "Пыль" } },
            { "weather.snow", new[] { "Snow", "Снег" } },
            { "weather.night", new[] { "Night", "Ночь" } },
            { "weather.calm", new[] { "No wind", "Ветра нет" } },
            { "weather.wind", new[] { "Wind {0} m/s", "Ветер {0} м/с" } },
            { "weather.wind_gusts", new[] { "Wind {0} m/s, gusts to {1}", "Ветер {0} м/с, порывы до {1}" } },
            { "hud.wind", new[] { "WIND {0} m/s", "ВЕТЕР {0} м/с" } },
            { "hud.fps", new[] { "{0} FPS", "{0} FPS" } },
            { "settings.graphics", new[] { "GRAPHICS", "ГРАФИКА" } },
            { "settings.auto_picked", new[] { "Chosen for this phone: {0}", "Для этого телефона выбрано: {0}" } },
            { "settings.fps_on", new[] { "FPS COUNTER: ON", "СЧЁТЧИК FPS: ВКЛ" } },
            { "settings.fps_off", new[] { "FPS COUNTER: OFF", "СЧЁТЧИК FPS: ВЫКЛ" } },
            { "settings.sound_on", new[] { "SOUND: ON", "ЗВУК: ВКЛ" } },
            { "settings.sound_off", new[] { "SOUND: OFF", "ЗВУК: ВЫКЛ" } },
            { "settings.close", new[] { "DONE", "ГОТОВО" } },
            { "quality.auto", new[] { "AUTO", "АВТО" } },
            { "quality.low", new[] { "LOW", "НИЗКОЕ" } },
            { "quality.medium", new[] { "MEDIUM", "СРЕДНЕЕ" } },
            { "quality.high", new[] { "HIGH", "ВЫСОКОЕ" } },
            { "quality.ultra", new[] { "ULTRA", "УЛЬТРА" } },
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

        public static bool Has(string key) => Table.ContainsKey(key);

        /// <summary>Every language column of every key, for the tests that check the table.</summary>
        public static IEnumerable<KeyValuePair<string, string[]>> All => Table;

        public static string T(string key)
        {
            if (Table.TryGetValue(key, out string[] row)) return row[Language];
            Debug.LogWarning("[Skyforge] Missing string " + key);
            return key;
        }

        public static string T(string key, params object[] args) => string.Format(T(key), args);
    }
}
