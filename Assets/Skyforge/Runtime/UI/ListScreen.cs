using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>One line of a <see cref="ListScreen"/>: what it is, how far along, and what its button does.</summary>
    public sealed class ListRow
    {
        public string Title, Note, Button;
        /// <summary>0..1 draws a bar under the note; negative draws none.</summary>
        public float Progress = -1f;
        public Action Press;
        /// <summary>A dull button that still answers (with a refusal) rather than a missing one.</summary>
        public bool Enabled = true;
        /// <summary>Lights the row: the thing in use, the task that is done.</summary>
        public bool Lit;
        public Color? Tint;
    }

    /// <summary>
    /// A panel with a title, tabs, six rows to a page and a line of small print. The headquarters
    /// screens (operations, career, base, collection) are all this, filled with different rows.
    /// </summary>
    public sealed class ListScreen
    {
        public const int RowsPerPage = 6;
        public event Action<int> TabPressed;
        public event Action ClosePressed;

        sealed class RowView
        {
            public GameObject Root;
            public Image Back, Bar, BarFill, Button;
            public Text Title, Note, Label;
            public Action Press;
        }

        readonly GameObject _root;
        readonly Text _title, _wallet, _footer, _page;
        readonly Text[] _tabLabels = new Text[5];
        readonly Image[] _tabs = new Image[5];
        readonly RowView[] _rows = new RowView[RowsPerPage];
        readonly GameObject _pager;
        List<ListRow> _all = new List<ListRow>();
        int _pageIndex;

        public ListScreen(Transform parent, string name, int order)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, name, order);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            // A dim sheet behind the panel: what is underneath is out of reach while it is open.
            Image dim = UiKit.Box(safe, "Dim", new Color(0f, 0.01f, 0.03f, 0.55f));
            dim.raycastTarget = true;
            UiKit.Stretch(dim.rectTransform);

            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1500f, 980f));
            Transform p = panel.transform;
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            _title = UiKit.Label(p, 46, TextAnchor.UpperLeft);
            _title.color = UiKit.Accent;
            UiKit.Place(_title.rectTransform, topLeft, new Vector2(44f, -26f), new Vector2(700f, 60f));
            _wallet = UiKit.Label(p, 26, TextAnchor.UpperRight);
            _wallet.color = new Color(1f, 1f, 1f, 0.85f);
            UiKit.Place(_wallet.rectTransform, topRight, new Vector2(-250f, -36f), new Vector2(620f, 40f));
            UiKit.FitWidth(_wallet, 16);
            Text close = UiKit.Button(p, "Close", UiKit.Quiet, 26, () => ClosePressed?.Invoke());
            close.text = Loc.T("list.close");
            UiKit.FitWidth(close, 16);
            UiKit.Place((RectTransform)close.transform.parent, topRight, new Vector2(-40f, -24f), new Vector2(190f, 64f));

            for (int i = 0; i < _tabs.Length; i++)
            {
                int index = i;
                _tabLabels[i] = UiKit.Button(p, "Tab" + i, UiKit.Faint, 24, () => TabPressed?.Invoke(index));
                UiKit.FitWidth(_tabLabels[i], 14);
                var rect = (RectTransform)_tabLabels[i].transform.parent;
                UiKit.Place(rect, topLeft, new Vector2(44f + i * 284f, -100f), new Vector2(272f, 58f));
                _tabs[i] = rect.GetComponent<Image>();
            }
            UiKit.Rule(p, topLeft, new Vector2(44f, -170f), 1412f, 0.35f);

            for (int i = 0; i < RowsPerPage; i++)
            {
                var row = new RowView();
                row.Back = UiKit.Box(p, "Row" + i, new Color(0.30f, 0.86f, 1f, 0.05f), UiKit.Chamfer);
                row.Back.type = Image.Type.Sliced;
                UiKit.Place(row.Back.rectTransform, topLeft, new Vector2(44f, -184f - i * 112f), new Vector2(1412f, 102f));
                row.Root = row.Back.gameObject;
                Transform r = row.Back.transform;
                row.Title = UiKit.Label(r, 30, TextAnchor.UpperLeft);
                UiKit.Place(row.Title.rectTransform, topLeft, new Vector2(24f, -10f), new Vector2(1060f, 38f));
                UiKit.FitWidth(row.Title, 18);
                row.Title.supportRichText = true;
                row.Note = UiKit.Label(r, 22, TextAnchor.UpperLeft, true);
                UiKit.Light(row.Note);
                row.Note.color = new Color(1f, 1f, 1f, 0.72f);
                row.Note.supportRichText = true;
                UiKit.Place(row.Note.rectTransform, topLeft, new Vector2(24f, -48f), new Vector2(1060f, 46f));
                UiKit.FitWidth(row.Note, 14);
                row.Bar = UiKit.Box(r, "Bar", UiKit.Faint);
                UiKit.Place(row.Bar.rectTransform, new Vector2(0f, 0f), new Vector2(24f, 6f), new Vector2(1060f, 6f));
                row.BarFill = UiKit.Box(row.Bar.transform, "Fill", UiKit.Cyan);
                UiKit.Stretch(row.BarFill.rectTransform);
                int index = i;
                row.Label = UiKit.Button(r, "Button", UiKit.Accent, 26, () => _rows[index].Press?.Invoke());
                UiKit.FitWidth(row.Label, 15);
                var buttonRect = (RectTransform)row.Label.transform.parent;
                UiKit.Place(buttonRect, new Vector2(1f, 0.5f), new Vector2(-18f, 0f), new Vector2(280f, 74f));
                row.Button = buttonRect.GetComponent<Image>();
                _rows[i] = row;
            }

            _footer = UiKit.Label(p, 22, TextAnchor.LowerLeft, true);
            UiKit.Light(_footer);
            _footer.color = new Color(1f, 1f, 1f, 0.7f);
            _footer.supportRichText = true;
            UiKit.Place(_footer.rectTransform, new Vector2(0f, 0f), new Vector2(44f, 26f), new Vector2(980f, 84f));
            UiKit.FitWidth(_footer, 14);

            RectTransform pager = UiKit.Rect("Pager", p);
            UiKit.Place(pager, new Vector2(1f, 0f), new Vector2(-40f, 26f), new Vector2(400f, 70f));
            _pager = pager.gameObject;
            Text previous = UiKit.Button(pager, "Previous", UiKit.Quiet, 32, () => Turn(-1));
            previous.text = "<";
            UiKit.Place((RectTransform)previous.transform.parent, new Vector2(0f, 0.5f), Vector2.zero, new Vector2(110f, 66f));
            Text next = UiKit.Button(pager, "Next", UiKit.Quiet, 32, () => Turn(1));
            next.text = ">";
            UiKit.Place((RectTransform)next.transform.parent, new Vector2(1f, 0.5f), Vector2.zero, new Vector2(110f, 66f));
            _page = UiKit.Label(pager, 26, TextAnchor.MiddleCenter);
            UiKit.Stretch(_page.rectTransform);
            _root.SetActive(false);
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        void Turn(int step)
        {
            int pages = Mathf.Max(1, (_all.Count + RowsPerPage - 1) / RowsPerPage);
            _pageIndex = (_pageIndex + step + pages) % pages;
            UiKit.Tap?.Invoke();
            Fill();
        }

        /// <param name="keepPage">Stay on the page being looked at: the same list, refreshed after a press.</param>
        public void Show(string title, string wallet, string[] tabs, int tab, List<ListRow> rows, string footer, bool keepPage = false)
        {
            _title.text = title;
            _wallet.text = wallet ?? "";
            for (int i = 0; i < _tabs.Length; i++)
            {
                bool used = tabs != null && i < tabs.Length;
                _tabs[i].gameObject.SetActive(used);
                if (!used) continue;
                _tabLabels[i].text = tabs[i];
                _tabs[i].color = i == tab ? UiKit.Lit : UiKit.Faint;
            }
            _all = rows ?? new List<ListRow>();
            _footer.text = footer ?? "";
            if (!keepPage) _pageIndex = 0;
            Fill();
            Visible = true;
        }

        void Fill()
        {
            int pages = Mathf.Max(1, (_all.Count + RowsPerPage - 1) / RowsPerPage);
            _pageIndex = Mathf.Clamp(_pageIndex, 0, pages - 1);
            _pager.SetActive(pages > 1);
            _page.text = (_pageIndex + 1) + " / " + pages;
            for (int i = 0; i < RowsPerPage; i++)
            {
                int index = _pageIndex * RowsPerPage + i;
                RowView view = _rows[i];
                bool used = index < _all.Count;
                view.Root.SetActive(used);
                if (!used) continue;
                ListRow row = _all[index];
                view.Title.text = row.Title ?? "";
                view.Title.color = row.Tint ?? Color.white;
                view.Note.text = row.Note ?? "";
                view.Back.color = row.Lit ? new Color(0.30f, 0.86f, 1f, 0.16f) : new Color(0.30f, 0.86f, 1f, 0.05f);
                bool bar = row.Progress >= 0f;
                view.Bar.gameObject.SetActive(bar);
                if (bar) view.BarFill.rectTransform.anchorMax = new Vector2(Mathf.Clamp01(row.Progress), 1f);
                bool button = !string.IsNullOrEmpty(row.Button);
                view.Button.gameObject.SetActive(button);
                view.Press = row.Press;
                if (!button) continue;
                view.Label.text = row.Button;
                view.Button.color = row.Enabled ? UiKit.Accent : UiKit.Faint;
                view.Label.color = row.Enabled ? new Color(0.08f, 0.06f, 0.02f) : new Color(1f, 1f, 1f, 0.8f);
            }
        }
    }

    /// <summary>
    /// A transmission: someone speaking to the player between operations. A name, what they
    /// say, typed out, and one button.
    /// </summary>
    public sealed class TransmissionScreen
    {
        public event Action ContinuePressed;
        readonly GameObject _root;
        readonly Text _speaker, _text;
        readonly Typewriter _typing;

        public TransmissionScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Transmission", 45);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image dim = UiKit.Box(safe, "Dim", new Color(0f, 0.01f, 0.03f, 0.6f));
            dim.raycastTarget = true;
            UiKit.Stretch(dim.rectTransform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(1500f, 400f));
            Transform p = panel.transform;
            Text head = UiKit.Label(p, 20, TextAnchor.UpperLeft);
            head.text = "// " + Loc.T("story.incoming");
            head.color = UiKit.Cyan;
            UiKit.Place(head.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -22f), new Vector2(900f, 28f));
            _speaker = UiKit.Label(p, 40, TextAnchor.UpperLeft);
            _speaker.color = UiKit.Accent;
            UiKit.Place(_speaker.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -52f), new Vector2(1100f, 52f));
            _text = UiKit.Label(p, 30, TextAnchor.UpperLeft, true);
            UiKit.Light(_text);
            _text.lineSpacing = 1.12f;
            UiKit.Place(_text.rectTransform, new Vector2(0f, 1f), new Vector2(44f, -116f), new Vector2(1412f, 180f));
            UiKit.FitWidth(_text, 18);
            _typing = _text.gameObject.AddComponent<Typewriter>();
            Text button = UiKit.Button(p, "Continue", UiKit.Accent, 30, () => ContinuePressed?.Invoke());
            button.text = Loc.T("story.continue");
            button.color = new Color(0.08f, 0.06f, 0.02f);
            UiKit.Place((RectTransform)button.transform.parent, new Vector2(1f, 0f), new Vector2(-40f, 26f), new Vector2(340f, 78f));
            _root.SetActive(false);
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        public void Show(string speaker, string text)
        {
            _speaker.text = speaker;
            Visible = true;
            _typing.Type(text);
        }
    }

    /// <summary>
    /// What the player sees first: the mark drawing itself and the name arriving, over black,
    /// then gone. It plays once per launch and a touch skips it.
    /// </summary>
    public sealed class SplashScreen : MonoBehaviour
    {
        const float Seconds = 3.0f;
        CanvasGroup _group;
        RectTransform _ring, _wing, _sweep;
        Image _ringImage, _glow;
        Text _name, _tag;
        float _time;
        public Action Finished;

        public static SplashScreen Create(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Toast", 100);
            canvas.name = "Splash";
            var splash = canvas.gameObject.AddComponent<SplashScreen>();
            splash._group = canvas.gameObject.AddComponent<CanvasGroup>();
            Image back = UiKit.Box(canvas.transform, "Back", new Color(0.01f, 0.02f, 0.035f, 1f));
            back.raycastTarget = true;
            UiKit.Stretch(back.rectTransform);
            var middle = new Vector2(0.5f, 0.5f);

            splash._glow = UiKit.Box(canvas.transform, "Glow", new Color(1f, 0.55f, 0.10f, 0f), UiKit.Disc);
            UiKit.Place(splash._glow.rectTransform, middle, new Vector2(0f, 70f), new Vector2(620f, 620f));
            splash._ringImage = UiKit.Box(canvas.transform, "Ring", UiKit.Cyan, UiKit.Ring);
            splash._ringImage.type = Image.Type.Filled;
            splash._ringImage.fillMethod = Image.FillMethod.Radial360;
            splash._ring = splash._ringImage.rectTransform;
            UiKit.Place(splash._ring, middle, new Vector2(0f, 70f), new Vector2(300f, 300f));

            // The mark: a delta wing made of two leaning bars and a spine.
            RectTransform wing = UiKit.Rect("Wing", canvas.transform);
            UiKit.Place(wing, middle, new Vector2(0f, 70f), new Vector2(10f, 10f));
            splash._wing = wing;
            foreach (float side in new[] { -1f, 1f })
            {
                Image bar = UiKit.Box(wing, "Bar", UiKit.Accent);
                UiKit.Place(bar.rectTransform, middle, new Vector2(side * 46f, -14f), new Vector2(22f, 190f));
                bar.rectTransform.localRotation = Quaternion.Euler(0f, 0f, side * 32f);
            }
            Image spine = UiKit.Box(wing, "Spine", Color.white);
            UiKit.Place(spine.rectTransform, middle, new Vector2(0f, 10f), new Vector2(12f, 150f));

            splash._name = UiKit.Label(canvas.transform, 110, TextAnchor.MiddleCenter);
            splash._name.text = "SKYFORGE";
            UiKit.Place(splash._name.rectTransform, middle, new Vector2(0f, -170f), new Vector2(1400f, 130f));
            splash._tag = UiKit.Label(canvas.transform, 30, TextAnchor.MiddleCenter);
            splash._tag.text = "G L O B A L   O P E R A T I O N S";
            splash._tag.color = UiKit.Cyan;
            UiKit.Place(splash._tag.rectTransform, middle, new Vector2(0f, -250f), new Vector2(1400f, 44f));
            Image sweep = UiKit.Box(canvas.transform, "Sweep", new Color(1f, 1f, 1f, 0.5f));
            splash._sweep = sweep.rectTransform;
            UiKit.Place(splash._sweep, middle, new Vector2(-700f, -170f), new Vector2(6f, 120f));
            splash.Apply();
            return splash;
        }

        void Update()
        {
            // The first frames after loading are long ones; counted in full they would eat the whole animation.
            _time += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (_time > 0.4f && (Input.touchCount > 0 || Input.GetMouseButtonDown(0))) _time = Mathf.Max(_time, Seconds - 0.35f);
            Apply();
            if (_time < Seconds) return;
            Action done = Finished;
            Finished = null;
            Destroy(gameObject);
            done?.Invoke();
        }

        static float Ease(float t) => 1f - (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t)) * (1f - Mathf.Clamp01(t));

        void Apply()
        {
            // The ring draws itself, the wing drops into it, the name is wiped in, and it all fades.
            _ringImage.fillAmount = Ease(_time / 0.9f);
            _ring.localRotation = Quaternion.Euler(0f, 0f, -_time * 40f);
            float drop = Ease((_time - 0.5f) / 0.6f);
            _wing.localScale = Vector3.one * Mathf.Lerp(2.4f, 1f, drop);
            foreach (Image part in _wing.GetComponentsInChildren<Image>())
            {
                Color c = part.color;
                c.a = drop;
                part.color = c;
            }
            _glow.color = new Color(1f, 0.55f, 0.10f, 0.18f * drop * (0.8f + 0.2f * Mathf.Sin(_time * 5f)));
            float wipe = Ease((_time - 1.0f) / 0.8f);
            _name.color = new Color(1f, 1f, 1f, wipe);
            _name.rectTransform.localScale = Vector3.one * Mathf.Lerp(1.12f, 1f, wipe);
            _sweep.anchoredPosition = new Vector2(Mathf.Lerp(-700f, 700f, wipe), -170f);
            _sweep.GetComponent<Image>().color = new Color(1f, 1f, 1f, wipe < 1f ? 0.6f * Mathf.Sin(wipe * Mathf.PI) : 0f);
            _tag.color = new Color(UiKit.Cyan.r, UiKit.Cyan.g, UiKit.Cyan.b, Ease((_time - 1.5f) / 0.6f));
            _group.alpha = 1f - Ease((_time - (Seconds - 0.4f)) / 0.4f);
        }
    }
}
