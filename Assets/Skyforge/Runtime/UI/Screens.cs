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
        public event Action StartPressed, FreeFlightPressed;

        readonly GameObject _root;
        readonly Text _credits, _title, _body, _best;
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

            _credits = UiKit.Label(p, 34, TextAnchor.UpperRight);
            UiKit.Place(_credits.rectTransform, new Vector2(1f, 1f), new Vector2(-50f, -60f), new Vector2(420f, 44f));

            Text heading = UiKit.Label(p, 26, TextAnchor.UpperLeft);
            heading.text = Loc.T("menu.operation");
            heading.color = new Color(1f, 1f, 1f, 0.6f);
            UiKit.Place(heading.rectTransform, topLeft, new Vector2(64f, -220f), new Vector2(600f, 34f));
            _title = UiKit.Label(p, 56, TextAnchor.UpperLeft);
            UiKit.Place(_title.rectTransform, topLeft, new Vector2(60f, -254f), new Vector2(860f, 70f));

            for (int i = 0; i < 3; i++)
            {
                _stars[i] = UiKit.Box(p, "Star" + i, UiKit.Faint, UiKit.Star);
                UiKit.Place(_stars[i].rectTransform, topLeft, new Vector2(60f + i * 62f, -334f), new Vector2(54f, 54f));
            }
            _best = UiKit.Label(p, 30, TextAnchor.MiddleLeft);
            _best.color = new Color(1f, 1f, 1f, 0.75f);
            UiKit.Place(_best.rectTransform, topLeft, new Vector2(260f, -340f), new Vector2(660f, 44f));

            _body = UiKit.Label(p, 30, TextAnchor.UpperLeft, true);
            _body.fontStyle = FontStyle.Normal;
            _body.lineSpacing = 1.15f;
            UiKit.Place(_body.rectTransform, topLeft, new Vector2(62f, -412f), new Vector2(860f, 480f));

            Text start = UiKit.Button(p, "Start", UiKit.Accent, 40, () => StartPressed?.Invoke());
            start.text = Loc.T("menu.start");
            start.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)start.transform.parent, new Vector2(0f, 0f), new Vector2(60f, 50f), new Vector2(500f, 110f));

            Text free = UiKit.Button(p, "FreeFlight", new Color(1f, 1f, 1f, 0.14f), 32, () => FreeFlightPressed?.Invoke());
            free.text = Loc.T("menu.free");
            UiKit.Place((RectTransform)free.transform.parent, new Vector2(0f, 0f), new Vector2(580f, 50f), new Vector2(340f, 110f));
        }

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        public void Show(MissionDefinition mission, SaveData save)
        {
            _credits.text = Loc.T("menu.credits", save.Credits);
            _title.text = Loc.T(mission.TitleKey);

            MissionRecord record = save.Find(mission.Id);
            int stars = record != null ? record.Stars : 0;
            for (int i = 0; i < 3; i++) _stars[i].color = i < stars ? UiKit.Accent : UiKit.Faint;
            _best.text = record != null && record.Completions > 0
                ? Loc.T("menu.best", Format.Time(record.BestTime))
                : Loc.T("menu.not_flown");

            var body = new StringBuilder();
            body.Append(Loc.T(mission.BriefKey)).Append("\n\n");
            body.Append("<color=").Append(UiKit.AccentHex).Append(">").Append(Loc.T("menu.stars")).Append("</color>\n");
            body.Append("1  ").Append(Loc.T("star.complete")).Append('\n');
            body.Append("2  ").Append(Loc.T("star.time", Format.Time(mission.TimeGoalSeconds))).Append('\n');
            body.Append("3  ").Append(Loc.T("star.no_damage")).Append("\n\n");
            body.Append(Loc.T("menu.reward", mission.BaseReward, mission.StarReward));
            _body.text = body.ToString();
            Visible = true;
        }
    }

    /// <summary>What happened in the mission and why each star was or was not earned.</summary>
    public sealed class ResultScreen
    {
        public event Action RetryPressed, ContinuePressed;

        readonly GameObject _root;
        readonly Text _title, _reason, _lines, _reward;
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
            Image panel = UiKit.Box(safe, "Panel", UiKit.SolidPanelColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1100f, 900f));
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

        public void Show(MissionDefinition mission, MissionOutcome outcome, SaveData save)
        {
            _title.text = Loc.T(outcome.Succeeded ? "result.success" : "result.failed");
            _title.color = outcome.Succeeded ? Color.white : UiKit.Bad;
            for (int i = 0; i < 3; i++) _stars[i].color = i < outcome.Stars ? UiKit.Accent : UiKit.Faint;

            if (outcome.Succeeded)
            {
                _reason.text = Loc.T(mission.TitleKey);
                string time = Format.Time(outcome.Time), goal = Format.Time(mission.TimeGoalSeconds);
                _lines.text =
                    Mark(true) + Loc.T("star.complete") + "\n" +
                    Mark(outcome.TimeGoalMet) + Loc.T("result.time", time, goal) + "\n" +
                    Mark(outcome.NoDamage) + Loc.T(outcome.NoDamage ? "star.no_damage" : "result.damaged");
                _reward.text = Loc.T("result.reward", outcome.CreditsGranted) + "\n" +
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
        public static string Time(float seconds)
        {
            int whole = Mathf.FloorToInt(Mathf.Max(0f, seconds));
            return (whole / 60) + ":" + (whole % 60).ToString("00");
        }
    }
}
