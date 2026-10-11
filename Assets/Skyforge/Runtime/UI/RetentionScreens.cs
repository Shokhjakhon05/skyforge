using System;
using System.Collections.Generic;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>Today's three operations and the login streak.</summary>
    public sealed class DailyScreen
    {
        /// <summary>The player wants to fly the operation in this slot.</summary>
        public event Action<int> FlyPressed;
        public event Action ClaimPressed, ClosePressed, CratePressed;

        sealed class Row
        {
            public GameObject Root;
            public Text Title, Note, ButtonLabel;
            public Image Button;
        }

        readonly GameObject _root;
        readonly Text _streak, _streakReward, _claim, _empty, _crate;
        readonly GameObject _crateRow;
        readonly Text _watch;
        readonly Image _claimButton;
        readonly Row[] _rows = new Row[Daily.OpsPerDay];

        public DailyScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Daily", 22);
            _root = canvas.gameObject;
            Image shade = UiKit.Box(canvas.transform, "Shade", new Color(0f, 0f, 0f, 0.55f));
            UiKit.Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 900f));
            Transform p = panel.transform;
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            Text title = UiKit.Label(p, 48, TextAnchor.UpperLeft);
            title.text = Loc.T("daily.title");
            UiKit.Place(title.rectTransform, topLeft, new Vector2(60f, -40f), new Vector2(1000f, 60f));
            Text close = UiKit.Button(p, "Close", UiKit.Faint, 28, () => ClosePressed?.Invoke());
            close.text = Loc.T("settings.close_short");
            UiKit.Place((RectTransform)close.transform.parent, topRight, new Vector2(-50f, -36f), new Vector2(240f, 72f));

            _streak = UiKit.Label(p, 34, TextAnchor.UpperLeft);
            _streak.color = UiKit.Accent;
            UiKit.Place(_streak.rectTransform, topLeft, new Vector2(60f, -140f), new Vector2(900f, 44f));
            _streakReward = UiKit.Label(p, 28, TextAnchor.UpperLeft);
            UiKit.Light(_streakReward);
            UiKit.Place(_streakReward.rectTransform, topLeft, new Vector2(60f, -188f), new Vector2(900f, 38f));
            _claim = UiKit.Button(p, "Claim", UiKit.Accent, 32, () => ClaimPressed?.Invoke());
            var claimRect = (RectTransform)_claim.transform.parent;
            UiKit.Place(claimRect, topRight, new Vector2(-60f, -140f), new Vector2(420f, 92f));
            _claimButton = claimRect.GetComponent<Image>();

            // A free crate of parts for a watched ad; the row exists only when an ad can be shown.
            RectTransform crateRow = UiKit.Rect("Crate", p);
            UiKit.Place(crateRow, new Vector2(0f, 0f), new Vector2(60f, 16f), new Vector2(1380f, 60f));
            _crateRow = crateRow.gameObject;
            _crate = UiKit.Label(crateRow, 26, TextAnchor.MiddleLeft);
            UiKit.Light(_crate);
            UiKit.Place(_crate.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(1000f, 40f));
            Text watch = _watch = UiKit.Button(crateRow, "Watch", new Color(0.35f, 0.85f, 0.45f, 0.9f), 26, () => CratePressed?.Invoke());
            watch.text = Loc.T("ads.crate_button");
            watch.color = new Color(0.03f, 0.10f, 0.05f);
            UiKit.Place((RectTransform)watch.transform.parent, new Vector2(1f, 0.5f), new Vector2(0f, 0f), new Vector2(340f, 60f));
            _crateRow.SetActive(false);

            _empty = UiKit.Label(p, 32, TextAnchor.MiddleCenter, true);
            UiKit.Light(_empty);
            _empty.text = Loc.T("daily.none");
            UiKit.Place(_empty.rectTransform, new Vector2(0.5f, 0.5f), new Vector2(0f, -120f), new Vector2(1200f, 120f));

            for (int i = 0; i < _rows.Length; i++)
            {
                int slot = i;
                var row = new Row();
                Image back = UiKit.Box(p, "Row" + i, new Color(1f, 1f, 1f, 0.06f));
                UiKit.Place(back.rectTransform, topLeft, new Vector2(60f, -280f - i * 190f), new Vector2(1380f, 170f));
                row.Root = back.gameObject;
                row.Title = UiKit.Label(back.transform, 36, TextAnchor.UpperLeft);
                UiKit.Place(row.Title.rectTransform, topLeft, new Vector2(30f, -18f), new Vector2(900f, 46f));
                row.Note = UiKit.Label(back.transform, 26, TextAnchor.UpperLeft, true);
                UiKit.Light(row.Note);
                row.Note.lineSpacing = 1.15f;
                row.Note.color = new Color(1f, 1f, 1f, 0.8f);
                UiKit.Place(row.Note.rectTransform, topLeft, new Vector2(30f, -72f), new Vector2(940f, 90f));
                row.ButtonLabel = UiKit.Button(back.transform, "Fly", UiKit.Accent, 32, () => FlyPressed?.Invoke(slot));
                var buttonRect = (RectTransform)row.ButtonLabel.transform.parent;
                UiKit.Place(buttonRect, new Vector2(1f, 0.5f), new Vector2(-30f, 0f), new Vector2(340f, 100f));
                row.Button = buttonRect.GetComponent<Image>();
                _rows[i] = row;
            }
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        /// <summary>Shows the free crate offer with this text; null hides it.</summary>
        public void SetCrate(string text, string button = null)
        {
            _crateRow.SetActive(!string.IsNullOrEmpty(text));
            if (!string.IsNullOrEmpty(text)) _crate.text = text;
            _watch.text = button ?? Loc.T("ads.crate_button");
        }

        /// <param name="titles">What to call each operation: its region and mission.</param>
        public void Show(SaveData save, string today, IList<DailyOp> ops, IList<string> titles)
        {
            bool canClaim = Daily.CanClaimStreak(save, today);
            int day = Daily.NextStreakDay(save, today);
            _streak.text = Loc.T("daily.streak", day);
            _streakReward.text = Loc.T("daily.streak_reward", Daily.StreakCredits[day - 1], Daily.StreakParts[day - 1]);
            _claim.text = Loc.T(canClaim ? "daily.claim" : "daily.claimed");
            _claim.color = canClaim ? new Color(0.08f, 0.06f, 0.02f) : Color.white;
            _claimButton.color = canClaim ? UiKit.Accent : UiKit.Faint;

            _empty.gameObject.SetActive(ops.Count == 0);
            for (int i = 0; i < _rows.Length; i++)
            {
                Row row = _rows[i];
                bool used = i < ops.Count;
                row.Root.SetActive(used);
                if (!used) continue;
                bool done = Daily.IsDone(save, today, i);
                row.Title.text = titles[i];
                row.Note.text = Loc.T(ops[i].ModifierKey) + "\n" + Loc.T("daily.reward", ops[i].Credits, ops[i].Parts);
                row.ButtonLabel.text = Loc.T(done ? "daily.done" : "daily.fly");
                row.ButtonLabel.color = done ? Color.white : new Color(0.08f, 0.06f, 0.02f);
                row.Button.color = done ? UiKit.Good : UiKit.Accent;
            }
            Visible = true;
        }
    }

    /// <summary>The list of achievements, a page at a time.</summary>
    public sealed class AchievementsScreen
    {
        public event Action ClosePressed, PagePressed;

        const int PerPage = 8;
        readonly GameObject _root;
        readonly Text _title, _page;
        readonly Text[] _names = new Text[PerPage], _states = new Text[PerPage];
        readonly RectTransform[] _fills = new RectTransform[PerPage];
        readonly GameObject[] _rows = new GameObject[PerPage];
        SaveData _save;
        int _pageIndex;

        public AchievementsScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Achievements", 22);
            _root = canvas.gameObject;
            Image shade = UiKit.Box(canvas.transform, "Shade", new Color(0f, 0f, 0f, 0.55f));
            UiKit.Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 940f));
            Transform p = panel.transform;
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            _title = UiKit.Label(p, 48, TextAnchor.UpperLeft);
            UiKit.Place(_title.rectTransform, topLeft, new Vector2(60f, -36f), new Vector2(900f, 60f));
            Text close = UiKit.Button(p, "Close", UiKit.Faint, 28, () => ClosePressed?.Invoke());
            close.text = Loc.T("settings.close_short");
            UiKit.Place((RectTransform)close.transform.parent, topRight, new Vector2(-50f, -32f), new Vector2(240f, 72f));
            _page = UiKit.Button(p, "Page", UiKit.Faint, 28, () => NextPage());
            UiKit.Place((RectTransform)_page.transform.parent, topRight, new Vector2(-310f, -32f), new Vector2(200f, 72f));

            for (int i = 0; i < PerPage; i++)
            {
                Image back = UiKit.Box(p, "Row" + i, new Color(1f, 1f, 1f, 0.06f));
                UiKit.Place(back.rectTransform, topLeft, new Vector2(60f, -130f - i * 98f), new Vector2(1380f, 88f));
                _rows[i] = back.gameObject;
                _names[i] = UiKit.Label(back.transform, 30, TextAnchor.MiddleLeft);
                UiKit.Place(_names[i].rectTransform, new Vector2(0f, 0.5f), new Vector2(26f, 10f), new Vector2(800f, 44f));
                _states[i] = UiKit.Label(back.transform, 28, TextAnchor.MiddleRight);
                UiKit.Place(_states[i].rectTransform, new Vector2(1f, 0.5f), new Vector2(-26f, 0f), new Vector2(480f, 44f));
                Image bar = UiKit.Box(back.transform, "Bar", UiKit.Faint);
                UiKit.Place(bar.rectTransform, new Vector2(0f, 0f), new Vector2(26f, 12f), new Vector2(800f, 8f));
                Image fill = UiKit.Box(bar.transform, "Fill", UiKit.Accent);
                UiKit.Stretch(fill.rectTransform);
                _fills[i] = fill.rectTransform;
            }
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        public void Show(SaveData save)
        {
            _save = save;
            _pageIndex = 0;
            Visible = true;
            Refresh();
        }

        void NextPage()
        {
            int pages = (Achievements.All.Count + PerPage - 1) / PerPage;
            _pageIndex = (_pageIndex + 1) % pages;
            PagePressed?.Invoke();
            Refresh();
        }

        void Refresh()
        {
            IReadOnlyList<Achievement> all = Achievements.All;
            int pages = (all.Count + PerPage - 1) / PerPage;
            _title.text = Loc.T("achievements.title", _save.Achievements.Count, all.Count);
            _page.text = Loc.T("achievements.page", _pageIndex + 1, pages) + "  >";

            for (int i = 0; i < PerPage; i++)
            {
                int index = _pageIndex * PerPage + i;
                bool used = index < all.Count;
                _rows[i].SetActive(used);
                if (!used) continue;

                Achievement achievement = all[index];
                bool earned = Achievements.IsEarned(_save, achievement);
                int value = Mathf.Min(Achievements.Value(_save, achievement.Kind), achievement.Goal);
                _names[i].text = Loc.T(achievement.NameKey, achievement.Goal);
                _states[i].text = earned
                    ? Loc.T("achievements.earned")
                    : Loc.T("achievements.progress", value, achievement.Goal) + "   " + Loc.T("achievements.reward", achievement.Parts);
                _states[i].color = earned ? UiKit.Good : Color.white;
                _fills[i].anchorMax = new Vector2(earned ? 1f : Mathf.Clamp01(value / (float)achievement.Goal), 1f);
            }
        }
    }
}
