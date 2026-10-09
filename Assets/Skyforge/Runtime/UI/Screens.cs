using System;
using System.Text;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>The screen between flights: who you are, what the next operation is, and how to start it.</summary>
    public sealed class MenuScreen
    {
        public event Action StartPressed, FreeFlightPressed, SettingsPressed, WeatherPressed, PreviousPressed, NextPressed, HangarPressed, GlobePressed, DailyPressed, AchievementsPressed;

        readonly GameObject _root;
        readonly Text _credits, _title, _body, _best, _weather, _heading, _start;
        readonly Image _startButton;
        readonly Image[] _stars = new Image[3];

        public MenuScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Menu", 10);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            var topLeft = new Vector2(0f, 1f);

            Image panel = UiKit.Box(safe, "Panel", UiKit.SolidPanelColor);
            panel.rectTransform.anchorMin = new Vector2(0f, 0f);
            panel.rectTransform.anchorMax = new Vector2(0f, 1f);
            panel.rectTransform.pivot = new Vector2(0f, 0.5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            panel.rectTransform.sizeDelta = new Vector2(980f, 0f);
            Transform p = panel.transform;

            Text logo = UiKit.Label(p, 84, TextAnchor.UpperLeft);
            logo.text = "SKYFORGE";
            UiKit.Place(logo.rectTransform, topLeft, new Vector2(60f, -40f), new Vector2(700f, 96f));
            Text tagline = UiKit.Label(p, 30, TextAnchor.UpperLeft);
            tagline.text = "GLOBAL OPERATIONS";
            tagline.color = UiKit.Accent;
            UiKit.Place(tagline.rectTransform, topLeft, new Vector2(64f, -136f), new Vector2(700f, 40f));

            _credits = UiKit.Label(p, 28, TextAnchor.UpperRight);
            _credits.lineSpacing = 1.15f;
            UiKit.Place(_credits.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -44f), new Vector2(420f, 120f));

            _heading = UiKit.Label(p, 26, TextAnchor.UpperLeft);
            _heading.color = new Color(1f, 1f, 1f, 0.6f);
            UiKit.Place(_heading.rectTransform, topLeft, new Vector2(64f, -220f), new Vector2(660f, 34f));
            UiKit.FitWidth(_heading, 16);
            _title = UiKit.Label(p, 56, TextAnchor.UpperLeft);
            UiKit.Place(_title.rectTransform, topLeft, new Vector2(60f, -254f), new Vector2(700f, 70f));

            // Browse the operations with the two arrows next to the title.
            Text previous = UiKit.Button(p, "Previous", new Color(1f, 1f, 1f, 0.14f), 44, () => PreviousPressed?.Invoke());
            previous.text = "<";
            UiKit.Place((RectTransform)previous.transform.parent, new Vector2(1f, 1f), new Vector2(-170f, -226f), new Vector2(110f, 100f));
            Text next = UiKit.Button(p, "Next", new Color(1f, 1f, 1f, 0.14f), 44, () => NextPressed?.Invoke());
            next.text = ">";
            UiKit.Place((RectTransform)next.transform.parent, new Vector2(1f, 1f), new Vector2(-50f, -226f), new Vector2(110f, 100f));

            for (int i = 0; i < 3; i++)
            {
                _stars[i] = UiKit.Box(p, "Star" + i, UiKit.Faint, UiKit.Star);
                UiKit.Place(_stars[i].rectTransform, topLeft, new Vector2(60f + i * 62f, -334f), new Vector2(54f, 54f));
            }
            _best = UiKit.Label(p, 30, TextAnchor.MiddleLeft);
            _best.color = new Color(1f, 1f, 1f, 0.75f);
            UiKit.Place(_best.rectTransform, topLeft, new Vector2(260f, -340f), new Vector2(660f, 44f));

            _body = UiKit.Label(p, 28, TextAnchor.UpperLeft, true);
            _body.fontStyle = FontStyle.Normal;
            _body.lineSpacing = 1.12f;
            UiKit.Place(_body.rectTransform, topLeft, new Vector2(62f, -408f), new Vector2(860f, 420f));

            // Free flight has no mission to set the weather, so the player picks it here.
            _weather = UiKit.Label(p, 28, TextAnchor.MiddleLeft);
            _weather.color = new Color(1f, 1f, 1f, 0.8f);
            UiKit.Place(_weather.rectTransform, new Vector2(0f, 0f), new Vector2(62f, 180f), new Vector2(560f, 64f));
            Text change = UiKit.Button(p, "Weather", new Color(1f, 1f, 1f, 0.14f), 26, () => WeatherPressed?.Invoke());
            change.text = Loc.T("menu.change");
            UiKit.Place((RectTransform)change.transform.parent, new Vector2(0f, 0f), new Vector2(640f, 180f), new Vector2(280f, 64f));

            Text settings = UiKit.Button(safe, "Settings", UiKit.SolidPanelColor, 30, () => SettingsPressed?.Invoke());
            settings.text = Loc.T("menu.settings");
            UiKit.Place((RectTransform)settings.transform.parent, new Vector2(1f, 1f), new Vector2(-36f, -26f), new Vector2(320f, 84f));

            Text hangar = UiKit.Button(safe, "Hangar", UiKit.Accent, 30, () => HangarPressed?.Invoke());
            hangar.text = Loc.T("menu.hangar");
            hangar.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)hangar.transform.parent, new Vector2(1f, 1f), new Vector2(-36f, -124f), new Vector2(320f, 84f));

            Text globe = UiKit.Button(safe, "Globe", UiKit.SolidPanelColor, 30, () => GlobePressed?.Invoke());
            globe.text = Loc.T("menu.globe");
            UiKit.Place((RectTransform)globe.transform.parent, new Vector2(1f, 1f), new Vector2(-36f, -222f), new Vector2(320f, 84f));

            Text daily = UiKit.Button(safe, "Daily", UiKit.SolidPanelColor, 30, () => DailyPressed?.Invoke());
            daily.text = Loc.T("menu.daily");
            UiKit.Place((RectTransform)daily.transform.parent, new Vector2(1f, 1f), new Vector2(-36f, -320f), new Vector2(320f, 84f));

            Text achievements = UiKit.Button(safe, "Achievements", UiKit.SolidPanelColor, 30, () => AchievementsPressed?.Invoke());
            achievements.text = Loc.T("menu.achievements");
            UiKit.Place((RectTransform)achievements.transform.parent, new Vector2(1f, 1f), new Vector2(-36f, -418f), new Vector2(320f, 84f));

            _start = UiKit.Button(p, "Start", UiKit.Accent, 40, () => StartPressed?.Invoke());
            var startRect = (RectTransform)_start.transform.parent;
            UiKit.Place(startRect, new Vector2(0f, 0f), new Vector2(60f, 50f), new Vector2(500f, 110f));
            _startButton = startRect.GetComponent<Image>();
            UiKit.FitWidth(_start, 24);

            Text free = UiKit.Button(p, "FreeFlight", new Color(1f, 1f, 1f, 0.14f), 32, () => FreeFlightPressed?.Invoke());
            free.text = Loc.T("menu.free");
            UiKit.FitWidth(free, 20);
            UiKit.Place((RectTransform)free.transform.parent, new Vector2(0f, 0f), new Vector2(580f, 50f), new Vector2(340f, 110f));
        }

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        /// <param name="number">Position of the mission in the campaign, from 1.</param>
        /// <param name="locked">The previous mission has not been completed yet.</param>
        public void Show(MissionDefinition mission, SaveData save, int number, int total, bool locked, string regionName)
        {
            _credits.text = Loc.T("menu.credits", save.Credits) + "\n" + Loc.T("menu.parts", save.Parts) + "\n" +
                            Loc.T("menu.drone", Loc.T(Garage.Selected(save).NameKey));
            _title.text = Loc.T(mission.TitleKey);
            // The last three missions of a region are the stages of its major operation.
            string kind = number > total - 3 && total >= 3
                ? Loc.T("menu.major", number - (total - 3))
                : Loc.T("template." + mission.Template);
            _heading.text = regionName.ToUpperInvariant() + "  ·  " + number + "/" + total + "  ·  " + kind;

            _start.text = Loc.T(locked ? "menu.locked_short" : "menu.start");
            _start.color = locked ? Color.white : new Color(0.08f, 0.06f, 0.02f);
            _startButton.color = locked ? UiKit.Faint : UiKit.Accent;

            MissionRecord record = save.Find(mission.Id);
            int stars = record != null ? record.Stars : 0;
            for (int i = 0; i < 3; i++) _stars[i].color = i < stars ? UiKit.Accent : UiKit.Faint;
            _best.text = record != null && record.Completions > 0
                ? Loc.T("menu.best", Format.Time(record.BestTime))
                : Loc.T("menu.not_flown");

            Weather weather = WeatherCatalog.Get(mission.WeatherId);
            var body = new StringBuilder();
            body.Append(Loc.T(mission.BriefKey)).Append("\n\n");

            // Everything that changes the flight is said up front.
            body.Append(Loc.T("menu.weather", Loc.T(weather.NameKey), Format.Wind(weather)));
            if (mission.Payload > 0f) body.Append(' ').Append(Loc.T("menu.cargo"));
            if (mission.BatteryStart < 1f)
                body.Append(' ').Append(Loc.T("menu.low_battery", Mathf.RoundToInt(mission.BatteryStart * 100f)));
            if (mission.Zones.Count > 0) body.Append(' ').Append(Loc.T("menu.radars"));
            body.Append('\n');

            body.Append("<color=").Append(UiKit.AccentHex).Append(">").Append(Loc.T("menu.stars")).Append("</color>  ");
            body.Append(Loc.T("star.complete")).Append("; ");
            body.Append(Loc.T("star.time", Format.Time(mission.TimeGoalSeconds))).Append("; ");
            body.Append(Loc.T("star.no_damage")).Append(".\n");
            body.Append(Loc.T("menu.reward", mission.BaseReward, mission.StarReward));
            if (locked) body.Append("\n<color=#F24D40>").Append(Loc.T("menu.locked")).Append("</color>");
            _body.text = body.ToString();

            Weather free = WeatherCatalog.Get(save.FreeFlightWeather);
            _weather.text = Loc.T("menu.free_weather", Loc.T(free.NameKey));
            Visible = true;
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
        }

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
            _lines.fontStyle = FontStyle.Normal;
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

            Text retry = UiKit.Button(p, "Retry", new Color(1f, 1f, 1f, 0.14f), 36, () => RetryPressed?.Invoke());
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
                    Mark(outcome.NoDamage) + Loc.T(outcome.NoDamage ? "result.undamaged" : "result.damaged") +
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
