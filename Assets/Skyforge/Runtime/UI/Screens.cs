using System;
using System.Collections.Generic;
using System.Text;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>The screen between flights: who you are, what the next operation is, and how to start it.</summary>
    /// <summary>
    /// The operations room: the selected mission as a briefing. A tactical map of the area with
    /// the pad and the targets marked, the threat level, what the enemy has there, and the orders,
    /// typed out as if coming over the wire.
    /// </summary>
    public sealed class MenuScreen
    {
        public event Action StartPressed, FreeFlightPressed, SettingsPressed, WeatherPressed, PreviousPressed, NextPressed, HangarPressed, GlobePressed, DailyPressed, AchievementsPressed;

        const int MapMarks = 28;
        const float MapSize = 380f;

        readonly GameObject _root;
        readonly Text _credits, _title, _body, _best, _weather, _heading, _start, _threat, _intel, _stamp, _mapNote;
        readonly Image _startButton, _threatPlate;
        readonly Image[] _stars = new Image[3];
        readonly RawImage _map;
        readonly RectTransform[] _marks = new RectTransform[MapMarks];
        readonly Image[] _markImages = new Image[MapMarks];
        readonly RectTransform _padMark, _sweep;
        readonly Typewriter _typing;
        float _mapHalfSize = 768f;
        string _typedFor;

        public MenuScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Menu", 10);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            Image panel = UiKit.Box(safe, "Panel", UiKit.SolidPanelColor);
            panel.rectTransform.anchorMin = new Vector2(0f, 0f);
            panel.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel.rectTransform.pivot = new Vector2(0f, 0.5f);
            panel.rectTransform.anchoredPosition = new Vector2(24f, 0f);
            panel.rectTransform.sizeDelta = new Vector2(1080f, -48f);
            Transform p = panel.transform;

            // Masthead.
            Text logo = UiKit.Label(p, 58, TextAnchor.UpperLeft);
            logo.text = "SKYFORGE";
            UiKit.Place(logo.rectTransform, topLeft, new Vector2(44f, -26f), new Vector2(520f, 70f));
            Text tagline = UiKit.Label(p, 22, TextAnchor.UpperLeft);
            tagline.text = "GLOBAL OPERATIONS  //  TACTICAL NET";
            tagline.color = UiKit.Cyan;
            UiKit.Place(tagline.rectTransform, topLeft, new Vector2(48f, -94f), new Vector2(700f, 30f));
            _credits = UiKit.Label(p, 24, TextAnchor.UpperRight);
            _credits.lineSpacing = 1.1f;
            _credits.color = new Color(1f, 1f, 1f, 0.85f);
            UiKit.Place(_credits.rectTransform, topRight, new Vector2(-40f, -26f), new Vector2(460f, 100f));
            UiKit.Rule(p, topLeft, new Vector2(44f, -134f), 992f);

            // The operation: where, which one, how dangerous.
            _heading = UiKit.Label(p, 24, TextAnchor.UpperLeft);
            _heading.color = UiKit.Cyan;
            UiKit.Place(_heading.rectTransform, topLeft, new Vector2(46f, -150f), new Vector2(740f, 32f));
            UiKit.FitWidth(_heading, 15);
            _threat = UiKit.Tag(p, UiKit.Bad, 22);
            _threatPlate = _threat.transform.parent.GetComponent<Image>();
            UiKit.Place(_threatPlate.rectTransform, topRight, new Vector2(-40f, -264f), new Vector2(280f, 40f));
            UiKit.FitWidth(_threat, 14);

            _title = UiKit.Label(p, 54, TextAnchor.UpperLeft);
            UiKit.Place(_title.rectTransform, topLeft, new Vector2(44f, -186f), new Vector2(760f, 70f));
            UiKit.FitWidth(_title, 30);

            // Browse the operations with the two arrows next to the title.
            Text previous = UiKit.Button(p, "Previous", UiKit.Quiet, 40, () => PreviousPressed?.Invoke());
            previous.text = "<";
            UiKit.Place((RectTransform)previous.transform.parent, topRight, new Vector2(-160f, -148f), new Vector2(110f, 104f));
            Text next = UiKit.Button(p, "Next", UiKit.Quiet, 40, () => NextPressed?.Invoke());
            next.text = ">";
            UiKit.Place((RectTransform)next.transform.parent, topRight, new Vector2(-40f, -148f), new Vector2(110f, 104f));

            for (int i = 0; i < 3; i++)
            {
                _stars[i] = UiKit.Box(p, "Star" + i, UiKit.Faint, UiKit.Star);
                UiKit.Place(_stars[i].rectTransform, topLeft, new Vector2(46f + i * 48f, -264f), new Vector2(40f, 40f));
            }
            _best = UiKit.Label(p, 24, TextAnchor.MiddleLeft);
            _best.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(_best.rectTransform, topLeft, new Vector2(204f, -266f), new Vector2(290f, 36f));
            UiKit.FitWidth(_best, 15);
            UiKit.Rule(p, topLeft, new Vector2(44f, -318f), 992f, 0.3f);

            // Left column: the orders.
            Text ordersHead = UiKit.Label(p, 20, TextAnchor.UpperLeft);
            ordersHead.text = "// " + Loc.T("brief.orders");
            ordersHead.color = UiKit.Cyan;
            UiKit.Place(ordersHead.rectTransform, topLeft, new Vector2(46f, -332f), new Vector2(560f, 28f));
            _body = UiKit.Label(p, 26, TextAnchor.UpperLeft, true);
            UiKit.Light(_body);
            _body.lineSpacing = 1.1f;
            UiKit.Place(_body.rectTransform, topLeft, new Vector2(46f, -366f), new Vector2(560f, 250f));
            UiKit.FitWidth(_body, 18);
            _typing = _body.gameObject.AddComponent<Typewriter>();

            Text intelHead = UiKit.Label(p, 20, TextAnchor.UpperLeft);
            intelHead.text = "// " + Loc.T("brief.intel");
            intelHead.color = UiKit.Cyan;
            UiKit.Place(intelHead.rectTransform, topLeft, new Vector2(46f, -628f), new Vector2(560f, 28f));
            _intel = UiKit.Label(p, 24, TextAnchor.UpperLeft, true);
            UiKit.Light(_intel);
            _intel.lineSpacing = 1.12f;
            _intel.supportRichText = true;
            UiKit.Place(_intel.rectTransform, topLeft, new Vector2(46f, -660f), new Vector2(560f, 186f));
            UiKit.FitWidth(_intel, 16);

            // Right column: the map of the area.
            Image mapFrame = UiKit.Box(p, "MapFrame", new Color(0.01f, 0.03f, 0.05f, 0.9f));
            UiKit.Place(mapFrame.rectTransform, topRight, new Vector2(-40f, -332f), new Vector2(MapSize + 16f, MapSize + 16f));
            RectTransform mapRect = UiKit.Rect("Map", mapFrame.transform);
            UiKit.Place(mapRect, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(MapSize, MapSize));
            _map = mapRect.gameObject.AddComponent<RawImage>();
            _map.raycastTarget = false;
            // A sweep line passing over the map, like a scope refreshing.
            Image sweep = UiKit.Box(mapRect, "Sweep", UiKit.Quiet, UiKit.Gradient);
            _sweep = sweep.rectTransform;
            _sweep.anchorMin = new Vector2(0f, 0f);
            _sweep.anchorMax = new Vector2(1f, 0f);
            _sweep.pivot = new Vector2(0.5f, 1f);
            _sweep.sizeDelta = new Vector2(0f, 70f);
            sweep.gameObject.AddComponent<MapSweep>().Height = MapSize;
            for (int i = 0; i < MapMarks; i++)
            {
                _markImages[i] = UiKit.Box(mapRect, "Mark", UiKit.Bad);
                _marks[i] = _markImages[i].rectTransform;
                UiKit.Place(_marks[i], new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(11f, 11f));
                _marks[i].pivot = new Vector2(0.5f, 0.5f);
                _marks[i].localRotation = Quaternion.Euler(0f, 0f, 45f);
                _marks[i].gameObject.SetActive(false);
            }
            Image pad = UiKit.Box(mapRect, "Pad", UiKit.Good, UiKit.Ring);
            _padMark = pad.rectTransform;
            UiKit.Place(_padMark, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(24f, 24f));
            _padMark.pivot = new Vector2(0.5f, 0.5f);
            Image mapEdge = UiKit.Box(mapFrame.transform, "Edge", new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, 0.7f), UiKit.Brackets);
            mapEdge.type = Image.Type.Sliced;
            UiKit.Stretch(mapEdge.rectTransform);
            _mapNote = UiKit.Label(p, 20, TextAnchor.UpperLeft);
            _mapNote.color = new Color(1f, 1f, 1f, 0.6f);
            _mapNote.supportRichText = true;
            UiKit.Place(_mapNote.rectTransform, topRight, new Vector2(-40f - MapSize - 16f + (MapSize + 16f), -736f), new Vector2(MapSize + 16f, 60f));
            _mapNote.rectTransform.pivot = new Vector2(1f, 1f);
            UiKit.FitWidth(_mapNote, 14);

            // The "restricted" stamp across the corner of the page.
            _stamp = UiKit.Label(p, 22, TextAnchor.MiddleCenter);
            _stamp.color = new Color(0.95f, 0.30f, 0.25f, 0.75f);
            UiKit.Place(_stamp.rectTransform, topRight, new Vector2(-330f, -268f), new Vector2(250f, 32f));
            _stamp.alignment = TextAnchor.MiddleRight;
            UiKit.FitWidth(_stamp, 13);

            // Free flight has no mission to set the weather, so the player picks it here.
            UiKit.Rule(p, new Vector2(0f, 0f), new Vector2(44f, 176f), 992f, 0.3f);
            _weather = UiKit.Button(p, "Weather", UiKit.Quiet, 22, () => WeatherPressed?.Invoke());
            UiKit.FitWidth(_weather, 13);
            UiKit.Place((RectTransform)_weather.transform.parent, new Vector2(0f, 0f), new Vector2(664f, 126f), new Vector2(372f, 42f));

            _start = UiKit.Button(p, "Start", UiKit.Accent, 40, () => StartPressed?.Invoke());
            var startRect = (RectTransform)_start.transform.parent;
            UiKit.Place(startRect, new Vector2(0f, 0f), new Vector2(44f, 36f), new Vector2(600f, 124f));
            _startButton = startRect.GetComponent<Image>();
            UiKit.FitWidth(_start, 24);

            Text free = UiKit.Button(p, "FreeFlight", UiKit.Quiet, 30, () => FreeFlightPressed?.Invoke());
            free.text = Loc.T("menu.free");
            UiKit.FitWidth(free, 18);
            UiKit.Place((RectTransform)free.transform.parent, new Vector2(0f, 0f), new Vector2(664f, 36f), new Vector2(372f, 84f));

            // The rest of the base, down the right-hand edge.
            float y = -26f;
            Text Side(string name, string key, Color color, Action pressed)
            {
                Text label = UiKit.Button(safe, name, color, 28, pressed);
                label.text = Loc.T(key);
                UiKit.FitWidth(label, 18);
                UiKit.Place((RectTransform)label.transform.parent, topRight, new Vector2(-36f, y), new Vector2(330f, 82f));
                y -= 96f;
                return label;
            }
            Side("Hangar", "menu.hangar", UiKit.Accent, () => HangarPressed?.Invoke()).color = new Color(0.08f, 0.06f, 0.02f);
            Side("Globe", "menu.globe", UiKit.SolidPanelColor, () => GlobePressed?.Invoke());
            Side("Daily", "menu.daily", UiKit.SolidPanelColor, () => DailyPressed?.Invoke());
            Side("Achievements", "menu.achievements", UiKit.SolidPanelColor, () => AchievementsPressed?.Invoke());
            Side("Settings", "menu.settings", UiKit.SolidPanelColor, () => SettingsPressed?.Invoke());
        }

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        /// <summary>The picture of the region the missions are flown over, and how far it reaches from the middle.</summary>
        public void SetMap(Texture map, float halfSize)
        {
            _map.texture = map;
            _mapHalfSize = Mathf.Max(1f, halfSize);
        }

        Vector2 OnMap(Vector3 world)
        {
            float scale = MapSize * 0.5f / _mapHalfSize;
            return new Vector2(Mathf.Clamp(world.x * scale, -MapSize * 0.5f, MapSize * 0.5f),
                Mathf.Clamp(world.z * scale, -MapSize * 0.5f, MapSize * 0.5f));
        }

        /// <param name="number">Position of the mission in the campaign, from 1.</param>
        /// <param name="locked">The previous mission has not been completed yet.</param>
        public void Show(MissionDefinition mission, SaveData save, int number, int total, bool locked, string regionName)
        {
            _credits.text = Loc.T("menu.credits", save.Credits) + "    " + Loc.T("menu.parts", save.Parts) + "\n" +
                            Loc.T("menu.drone", Loc.T(Garage.Selected(save).NameKey));
            _title.text = Loc.T(mission.TitleKey).ToUpperInvariant();
            // The last three missions of a region are the stages of its major operation.
            string kind = number > total - 3 && total >= 3
                ? Loc.T("menu.major", number - (total - 3))
                : Loc.T("template." + mission.Template);
            _heading.text = regionName.ToUpperInvariant() + "  /  OP " + number.ToString("00") + "-" + total.ToString("00") + "  /  " + kind.ToUpperInvariant();

            _start.text = Loc.T(locked ? "menu.locked_short" : "menu.start");
            _start.color = locked ? Color.white : new Color(0.08f, 0.06f, 0.02f);
            _startButton.color = locked ? UiKit.Faint : UiKit.Accent;

            MissionRecord record = save.Find(mission.Id);
            int stars = record != null ? record.Stars : 0;
            for (int i = 0; i < 3; i++) _stars[i].color = i < stars ? UiKit.Accent : UiKit.Faint;
            _best.text = record != null && record.Completions > 0
                ? Loc.T("menu.best", Format.Time(record.BestTime))
                : Loc.T("menu.not_flown");

            // How dangerous: counted from what can shoot and what can see.
            int guns = 0, hostile = 0;
            var counts = new Dictionary<string, int>();
            var order = new List<string>();
            foreach (TargetDefinition target in mission.Targets)
            {
                if (target.Friendly) continue;
                hostile++;
                if (target.Kind.Armed) guns++;
                if (!counts.ContainsKey(target.Kind.NameKey))
                {
                    counts[target.Kind.NameKey] = 0;
                    order.Add(target.Kind.NameKey);
                }
                counts[target.Kind.NameKey]++;
            }
            int danger = guns * 2 + mission.Zones.Count + (hostile > 6 ? 1 : 0);
            int level = danger == 0 ? 0 : danger <= 2 ? 1 : danger <= 6 ? 2 : 3;
            _threat.text = Loc.T("brief.threat") + ": " + Loc.T("brief.threat." + level);
            _threatPlate.color = level == 0 ? new Color(0.20f, 0.55f, 0.32f, 0.95f)
                : level == 1 ? new Color(0.72f, 0.56f, 0.10f, 0.95f)
                : level == 2 ? new Color(0.80f, 0.36f, 0.08f, 0.95f)
                : new Color(0.75f, 0.14f, 0.12f, 0.95f);
            _stamp.text = locked ? Loc.T("brief.stamp.locked") : Loc.T("brief.stamp");

            // The orders are typed out once per mission, not again each time the screen refreshes.
            string orders = Loc.T(mission.BriefKey);
            if (_typedFor != mission.Id + Loc.Language)
            {
                _typedFor = mission.Id + Loc.Language;
                _typing.Type(orders);
            }
            else
            {
                _typing.Finish(orders);
            }

            Weather weather = WeatherCatalog.Get(mission.WeatherId);
            var intel = new StringBuilder();
            string Head(string key) => "<color=" + UiKit.CyanHex + ">" + Loc.T(key) + "</color>  ";
            if (hostile > 0)
            {
                intel.Append(Head("brief.enemy"));
                for (int i = 0; i < order.Count && i < 5; i++)
                {
                    if (i > 0) intel.Append(", ");
                    intel.Append(Loc.T(order[i]).ToLowerInvariant());
                    if (counts[order[i]] > 1) intel.Append(" ×").Append(counts[order[i]]);
                }
                if (order.Count > 5) intel.Append("…");
                intel.Append('\n');
            }
            // Everything that changes the flight is said up front.
            intel.Append(Head("brief.weather")).Append(Loc.T(weather.NameKey)).Append(", ").Append(Format.Wind(weather));
            if (mission.Payload > 0f) intel.Append(' ').Append(Loc.T("menu.cargo"));
            if (mission.BatteryStart < 1f)
                intel.Append(' ').Append(Loc.T("menu.low_battery", Mathf.RoundToInt(mission.BatteryStart * 100f)));
            if (mission.Zones.Count > 0) intel.Append(' ').Append(Loc.T("menu.radars"));
            intel.Append('\n');
            intel.Append("<color=").Append(UiKit.AccentHex).Append(">").Append(Loc.T("menu.stars")).Append("</color>  ");
            intel.Append(Loc.T("star.complete")).Append("; ");
            intel.Append(Loc.T("star.time", Format.Time(mission.TimeGoalSeconds))).Append("; ");
            intel.Append(Loc.T(mission.StrikeDrones > 0 ? "star.spare" : mission.Targets.Count > 0 ? "star.hull" : "star.no_damage")).Append(".\n");
            intel.Append(Head("brief.pay")).Append(Loc.T("menu.reward", mission.BaseReward, mission.StarReward));
            if (locked) intel.Append("\n<color=#F24D40>").Append(Loc.T("menu.locked")).Append("</color>");
            _intel.text = intel.ToString();

            // The map: the pad, then every target; hostile in red, friendly in green, radars as pale marks.
            _padMark.anchoredPosition = OnMap(mission.BasePosition);
            int used = 0;
            foreach (TargetDefinition target in mission.Targets)
            {
                if (used >= MapMarks) break;
                Vector3 at = target.Path.Count > 0 ? target.Path[0] : target.Position;
                Place(used++, at, target.Friendly ? UiKit.Good : UiKit.Bad, 11f);
            }
            foreach (ZoneDefinition zone in mission.Zones)
            {
                if (used >= MapMarks) break;
                Place(used++, zone.Position, new Color(1f, 0.85f, 0.3f, 0.9f), 15f);
            }
            foreach (ObjectiveDefinition objective in mission.Objectives)
            {
                if (used >= MapMarks || hostile > 0) break;
                Place(used++, objective.Position, UiKit.Accent, 12f);
            }
            for (int i = used; i < MapMarks; i++)
                if (_marks[i].gameObject.activeSelf) _marks[i].gameObject.SetActive(false);
            _mapNote.text = "<color=#59D973>◯</color> " + Loc.T("brief.map.pad") + "   <color=#F24D40>◆</color> " + Loc.T("brief.map.target") +
                            (mission.Zones.Count > 0 ? "   <color=#FFD94D>◆</color> " + Loc.T("brief.map.radar") : "");

            Weather free = WeatherCatalog.Get(save.FreeFlightWeather);
            _weather.text = Loc.T("menu.free_weather", Loc.T(free.NameKey)) + "  ›";
            Visible = true;
        }

        void Place(int index, Vector3 world, Color color, float size)
        {
            RectTransform mark = _marks[index];
            if (!mark.gameObject.activeSelf) mark.gameObject.SetActive(true);
            mark.anchoredPosition = OnMap(world);
            mark.sizeDelta = new Vector2(size, size);
            _markImages[index].color = color;
        }
    }

    /// <summary>Graphics quality, the frame-rate counter and sound.</summary>
    public sealed class SettingsScreen
    {
        /// <summary>0 = automatic, 1..4 = Low..Ultra.</summary>
        public event Action<int> QualityPressed;
        /// <summary>The player picked a language; the game restarts to rebuild every screen in it.</summary>
        public event Action<string> LanguagePressed;
        public event Action FpsPressed, SoundPressed, ClosePressed;

        static readonly string[] QualityKeys =
            { "quality.auto", "quality.low", "quality.medium", "quality.high", "quality.ultra" };

        readonly GameObject _root;
        readonly Image[] _qualityButtons = new Image[5];
        readonly Text[] _qualityLabels = new Text[5];
        readonly Text _applied, _fps, _sound;
        readonly Image[] _languageButtons = new Image[Loc.Languages.Length];
        readonly Text[] _languageLabels = new Text[Loc.Languages.Length];

        public SettingsScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Settings", 30);
            _root = canvas.gameObject;

            Image shade = UiKit.Box(canvas.transform, "Shade", new Color(0f, 0f, 0f, 0.55f));
            UiKit.Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 940f));
            Transform p = panel.transform;
            var topLeft = new Vector2(0f, 1f);

            Text language = UiKit.Label(p, 32, TextAnchor.UpperLeft);
            language.text = Loc.T("settings.language");
            language.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(language.rectTransform, topLeft, new Vector2(60f, -530f), new Vector2(900f, 44f));
            for (int i = 0; i < Loc.Languages.Length; i++)
            {
                string code = Loc.Languages[i];
                _languageLabels[i] = UiKit.Button(p, "Language" + i, UiKit.Faint, 30, () => LanguagePressed?.Invoke(code));
                _languageLabels[i].text = Loc.LanguageNames[i];
                var rect = (RectTransform)_languageLabels[i].transform.parent;
                UiKit.Place(rect, topLeft, new Vector2(60f + i * 278f, -584f), new Vector2(264f, 96f));
                _languageButtons[i] = rect.GetComponent<Image>();
            }

            Text title = UiKit.Label(p, 56, TextAnchor.UpperLeft);
            title.text = Loc.T("menu.settings");
            UiKit.Place(title.rectTransform, topLeft, new Vector2(60f, -40f), new Vector2(900f, 70f));

            Text graphics = UiKit.Label(p, 32, TextAnchor.UpperLeft);
            graphics.text = Loc.T("settings.graphics");
            graphics.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(graphics.rectTransform, topLeft, new Vector2(60f, -150f), new Vector2(900f, 44f));

            for (int i = 0; i < 5; i++)
            {
                int level = i;
                _qualityLabels[i] = UiKit.Button(p, "Quality" + i, UiKit.Faint, 30, () => QualityPressed?.Invoke(level));
                _qualityLabels[i].text = Loc.T(QualityKeys[i]);
                var rect = (RectTransform)_qualityLabels[i].transform.parent;
                UiKit.Place(rect, topLeft, new Vector2(60f + i * 278f, -204f), new Vector2(264f, 96f));
                _qualityButtons[i] = rect.GetComponent<Image>();
            }
            _applied = UiKit.Label(p, 28, TextAnchor.UpperLeft);
            _applied.color = new Color(1f, 1f, 1f, 0.7f);
            UiKit.Place(_applied.rectTransform, topLeft, new Vector2(60f, -316f), new Vector2(1300f, 40f));

            _fps = UiKit.Button(p, "Fps", UiKit.Faint, 30, () => FpsPressed?.Invoke());
            UiKit.Place((RectTransform)_fps.transform.parent, topLeft, new Vector2(60f, -400f), new Vector2(680f, 96f));
            _sound = UiKit.Button(p, "Sound", UiKit.Faint, 30, () => SoundPressed?.Invoke());
            UiKit.Place((RectTransform)_sound.transform.parent, topLeft, new Vector2(760f, -400f), new Vector2(680f, 96f));

            Text close = UiKit.Button(p, "Close", UiKit.Accent, 36, () => ClosePressed?.Invoke());
            close.text = Loc.T("settings.close");
            close.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)close.transform.parent, new Vector2(0.5f, 0f), new Vector2(0f, 50f), new Vector2(460f, 110f));
            // The authors of the 3D models, as their licences ask.
            Text credits = UiKit.Button(p, "Credits", UiKit.Faint, 30, () => _credits.SetActive(true));
            credits.text = Loc.T("settings.credits");
            UiKit.Place((RectTransform)credits.transform.parent, new Vector2(1f, 0f), new Vector2(-60f, 50f), new Vector2(300f, 110f));

            Image sheet = UiKit.Box(p, "CreditsSheet", UiKit.ModalColor);
            UiKit.Stretch(sheet.rectTransform);
            sheet.raycastTarget = true;
            _credits = sheet.gameObject;
            Text heading = UiKit.Label(sheet.transform, 48, TextAnchor.UpperLeft);
            heading.text = Loc.T("credits.title");
            UiKit.Place(heading.rectTransform, topLeft, new Vector2(60f, -36f), new Vector2(900f, 60f));
            Text list = UiKit.Label(sheet.transform, 24, TextAnchor.UpperLeft, true);
            UiKit.Light(list);
            var names = Resources.Load<TextAsset>("credits");
            list.text = Loc.T("credits.note") + "\n\n" + (names != null ? names.text.Replace("\n\"", "   \"").Replace(":\n", ":\n") : "");
            UiKit.Place(list.rectTransform, topLeft, new Vector2(60f, -110f), new Vector2(1380f, 680f));
            UiKit.FitWidth(list, 16);
            Text back = UiKit.Button(sheet.transform, "Back", UiKit.Accent, 36, () => _credits.SetActive(false));
            back.text = Loc.T("settings.close_short");
            back.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)back.transform.parent, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(460f, 100f));
            _credits.SetActive(false);
        }

        GameObject _credits;

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        /// <param name="appliedKey">Name of the tier actually in use, shown when the choice is automatic.</param>
        public void Show(SaveData save, string appliedKey)
        {
            for (int i = 0; i < 5; i++)
            {
                bool selected = i == save.Quality;
                _qualityButtons[i].color = selected ? UiKit.Accent : UiKit.Faint;
                _qualityLabels[i].color = selected ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
            }
            _applied.text = save.Quality == 0 ? Loc.T("settings.auto_picked", Loc.T(appliedKey)) : "";
            for (int i = 0; i < Loc.Languages.Length; i++)
            {
                bool current = Loc.Languages[i] == Loc.Language;
                _languageButtons[i].color = current ? UiKit.Accent : UiKit.Faint;
                _languageLabels[i].color = current ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
            }
            _credits.SetActive(false);
            _fps.text = Loc.T(save.ShowFps ? "settings.fps_on" : "settings.fps_off");
            _sound.text = Loc.T(save.Muted ? "settings.sound_off" : "settings.sound_on");
            Visible = true;
        }

        public static string QualityKey(int level) => QualityKeys[Mathf.Clamp(level, 0, 4)];
    }

    /// <summary>What happened in the mission and why each star was or was not earned.</summary>
    public sealed class ResultScreen
    {
        public event Action RetryPressed, ContinuePressed, DoublePressed;

        readonly GameObject _root;
        readonly Text _title, _reason, _lines, _reward, _doubleLabel;
        readonly GameObject _double;
        readonly Image[] _stars = new Image[3];

        public ResultScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Result", 20);
            _root = canvas.gameObject;

            Image shade = UiKit.Box(canvas.transform, "Shade", new Color(0f, 0f, 0f, 0.45f));
            UiKit.Stretch(shade.rectTransform);
            // Swallows touches so nothing behind the result can be pressed.
            shade.raycastTarget = true;

            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 980f));
            Transform p = panel.transform;
            var top = new Vector2(0.5f, 1f);

            _title = UiKit.Label(p, 64, TextAnchor.UpperCenter);
            UiKit.Place(_title.rectTransform, top, new Vector2(0f, -40f), new Vector2(1000f, 80f));
            _reason = UiKit.Label(p, 32, TextAnchor.UpperCenter);
            _reason.color = new Color(1f, 1f, 1f, 0.8f);
            UiKit.Place(_reason.rectTransform, top, new Vector2(0f, -124f), new Vector2(1000f, 44f));

            for (int i = 0; i < 3; i++)
            {
                _stars[i] = UiKit.Box(p, "Star" + i, UiKit.Faint, UiKit.Star);
                UiKit.Place(_stars[i].rectTransform, top, new Vector2((i - 1) * 150f, -190f), new Vector2(130f, 130f));
            }

            _lines = UiKit.Label(p, 34, TextAnchor.UpperCenter, true);
            UiKit.Light(_lines);
            _lines.lineSpacing = 1.2f;
            UiKit.Place(_lines.rectTransform, top, new Vector2(0f, -350f), new Vector2(1000f, 200f));

            _reward = UiKit.Label(p, 48, TextAnchor.UpperCenter, true);
            _reward.color = UiKit.Accent;
            UiKit.Place(_reward.rectTransform, top, new Vector2(0f, -590f), new Vector2(1000f, 130f));

            // Offered only when doubling is possible: an ad is ready, or the player bought "no ads".
            _doubleLabel = UiKit.Button(p, "Double", new Color(0.35f, 0.85f, 0.45f, 0.9f), 30, () => DoublePressed?.Invoke());
            _doubleLabel.color = new Color(0.03f, 0.10f, 0.05f);
            var doubleRect = (RectTransform)_doubleLabel.transform.parent;
            UiKit.Place(doubleRect, new Vector2(0.5f, 0f), new Vector2(0f, 168f), new Vector2(880f, 84f));
            UiKit.FitWidth(_doubleLabel, 20);
            _double = doubleRect.gameObject;
            _double.SetActive(false);

            Text retry = UiKit.Button(p, "Retry", UiKit.Quiet, 36, () => RetryPressed?.Invoke());
            retry.text = Loc.T("result.retry");
            UiKit.Place((RectTransform)retry.transform.parent, new Vector2(0.5f, 0f), new Vector2(-230f, 40f), new Vector2(420f, 110f));

            Text next = UiKit.Button(p, "Continue", UiKit.Accent, 36, () => ContinuePressed?.Invoke());
            next.text = Loc.T("result.continue");
            next.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)next.transform.parent, new Vector2(0.5f, 0f), new Vector2(230f, 40f), new Vector2(420f, 110f));
        }

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        /// <summary>Shows the button that doubles the reward, with this label; null hides it.</summary>
        public void SetDouble(string label)
        {
            _double.SetActive(!string.IsNullOrEmpty(label));
            if (!string.IsNullOrEmpty(label)) _doubleLabel.text = label;
        }

        /// <param name="extra">One more line under the star explanations, or null.</param>
        public void Show(MissionDefinition mission, MissionOutcome outcome, SaveData save, string extra = null)
        {
            _title.text = Loc.T(outcome.Succeeded ? "result.success" : "result.failed");
            _title.color = outcome.Succeeded ? Color.white : UiKit.Bad;
            for (int i = 0; i < 3; i++) _stars[i].color = i < outcome.Stars ? UiKit.Accent : UiKit.Faint;

            if (outcome.Succeeded)
            {
                _reason.text = Loc.T(mission.TitleKey);
                string time = Format.Time(outcome.Time), goal = Format.Time(mission.TimeGoalSeconds);
                _lines.text =
                    Mark(true) + Loc.T("result.completed") + "\n" +
                    Mark(outcome.TimeGoalMet) + Loc.T("result.time", time, goal) + "\n" +
                    Mark(outcome.NoDamage) + Loc.T(CleanKey(mission, outcome.NoDamage)) +
                    (string.IsNullOrEmpty(extra) ? "" : "\n<color=" + UiKit.AccentHex + ">" + extra + "</color>");
                _reward.text = (outcome.PartsGranted > 0
                                   ? Loc.T("result.parts", outcome.CreditsGranted, outcome.PartsGranted)
                                   : Loc.T("result.reward", outcome.CreditsGranted)) + "\n" +
                               "<size=32><color=#FFFFFFBF>" + Loc.T("menu.credits", save.Credits) + "</color></size>";
            }
            else
            {
                _reason.text = Loc.T("fail." + outcome.FailReason);
                _lines.text = Loc.T("result.no_reward");
                _reward.text = "";
            }
            Visible = true;
        }

        /// <summary>The third star means something different when the mission was a fight.</summary>
        static string CleanKey(MissionDefinition mission, bool earned)
        {
            if (mission.StrikeDrones > 0) return earned ? "result.spare_ok" : "result.spare_bad";
            if (mission.Targets.Count > 0) return earned ? "result.hull_ok" : "result.hull_bad";
            return earned ? "result.undamaged" : "result.damaged";
        }

        /// <summary>Earned and missed lines differ by a word as well as by colour.</summary>
        static string Mark(bool earned)
        {
            return earned
                ? "<color=#59D973>" + Loc.T("result.yes") + "</color>   "
                : "<color=#F24D40>" + Loc.T("result.no") + "</color>   ";
        }
    }

    public static class Format
    {
        public static string Wind(Weather weather)
        {
            if (!weather.HasWind) return Loc.T("weather.calm");
            return weather.Gust > 0.5f
                ? Loc.T("weather.wind_gusts", Mathf.RoundToInt(weather.WindSpeed), Mathf.RoundToInt(weather.WindSpeed + weather.Gust))
                : Loc.T("weather.wind", Mathf.RoundToInt(weather.WindSpeed));
        }

        public static string Time(float seconds)
        {
            int whole = Mathf.FloorToInt(Mathf.Max(0f, seconds));
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }
    }
}
