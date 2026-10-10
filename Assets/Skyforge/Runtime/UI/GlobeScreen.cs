using System;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>The panel beside the world map: which region is selected, how far along it is, and the way in.</summary>
    public sealed class GlobeScreen
    {
        public event Action PreviousPressed, NextPressed, DeployPressed, BackPressed;

        readonly GameObject _root;
        readonly Text _name, _note, _progress, _deploy, _lock;
        readonly Image _deployButton;

        public GlobeScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "GlobeScreen", 15);
            _root = canvas.gameObject;
            RectTransform safe = UiKit.SafeArea(canvas.transform);
            var topLeft = new Vector2(0f, 1f);
            var topRight = new Vector2(1f, 1f);

            Text back = UiKit.Button(safe, "Back", UiKit.SolidPanelColor, 30, () => BackPressed?.Invoke());
            back.text = Loc.T("hangar.back");
            UiKit.Place((RectTransform)back.transform.parent, topLeft, new Vector2(36f, -26f), new Vector2(280f, 84f));

            Text hint = UiKit.Label(safe, 26, TextAnchor.LowerLeft);
            hint.text = Loc.T("globe.hint");
            hint.color = new Color(1f, 1f, 1f, 0.6f);
            UiKit.Place(hint.rectTransform, new Vector2(0f, 0f), new Vector2(40f, 30f), new Vector2(700f, 36f));

            Image panel = UiKit.Box(safe, "Panel", UiKit.SolidPanelColor);
            panel.rectTransform.anchorMin = new Vector2(1f, 0f);
            panel.rectTransform.anchorMax = new Vector2(1f, 1f);
            panel.rectTransform.pivot = new Vector2(1f, 0.5f);
            panel.rectTransform.anchoredPosition = Vector2.zero;
            panel.rectTransform.sizeDelta = new Vector2(860f, 0f);
            Transform p = panel.transform;

            Text title = UiKit.Label(p, 34, TextAnchor.UpperLeft);
            title.text = Loc.T("globe.title");
            title.color = UiKit.Accent;
            UiKit.Place(title.rectTransform, topLeft, new Vector2(50f, -40f), new Vector2(760f, 44f));

            _name = UiKit.Label(p, 76, TextAnchor.UpperLeft);
            UiKit.Place(_name.rectTransform, topLeft, new Vector2(48f, -130f), new Vector2(560f, 90f));

            Text previous = UiKit.Button(p, "Previous", UiKit.Faint, 44, () => PreviousPressed?.Invoke());
            previous.text = "<";
            UiKit.Place((RectTransform)previous.transform.parent, topRight, new Vector2(-170f, -136f), new Vector2(110f, 96f));
            Text next = UiKit.Button(p, "Next", UiKit.Faint, 44, () => NextPressed?.Invoke());
            next.text = ">";
            UiKit.Place((RectTransform)next.transform.parent, topRight, new Vector2(-50f, -136f), new Vector2(110f, 96f));

            _note = UiKit.Label(p, 30, TextAnchor.UpperLeft, true);
            UiKit.Light(_note);
            _note.lineSpacing = 1.15f;
            UiKit.Place(_note.rectTransform, topLeft, new Vector2(52f, -260f), new Vector2(760f, 200f));

            _progress = UiKit.Label(p, 30, TextAnchor.UpperLeft);
            UiKit.Place(_progress.rectTransform, topLeft, new Vector2(52f, -480f), new Vector2(760f, 40f));
            _lock = UiKit.Label(p, 28, TextAnchor.UpperLeft, true);
            _lock.color = UiKit.Bad;
            UiKit.Place(_lock.rectTransform, topLeft, new Vector2(52f, -536f), new Vector2(760f, 80f));

            _deploy = UiKit.Button(p, "Deploy", UiKit.Accent, 40, () => DeployPressed?.Invoke());
            var deployRect = (RectTransform)_deploy.transform.parent;
            UiKit.Place(deployRect, new Vector2(0f, 0f), new Vector2(50f, 110f), new Vector2(560f, 116f));
            _deployButton = deployRect.GetComponent<Image>();

            Text soon = UiKit.Label(p, 24, TextAnchor.LowerLeft);
            soon.text = Loc.T("globe.soon");
            soon.color = new Color(1f, 1f, 1f, 0.5f);
            UiKit.Place(soon.rectTransform, new Vector2(0f, 0f), new Vector2(52f, 40f), new Vector2(760f, 34f));
        }

        public bool Visible
        {
            set => _root.SetActive(value);
        }

        /// <param name="completed">Missions of the region the player has finished.</param>
        public void Show(RegionDefinition region, int completed, int missions, int stars, int totalStars)
        {
            bool locked = totalStars < region.StarsRequired;
            _name.text = Loc.T(region.NameKey);
            _note.text = Loc.T(region.NoteKey);
            _progress.text = Loc.T("globe.progress", completed, missions, stars, missions * 3);
            _lock.text = locked ? Loc.T("globe.locked", region.StarsRequired, totalStars) : "";
            _deploy.text = Loc.T(locked ? "menu.locked_short" : "globe.enter");
            _deploy.color = locked ? Color.white : new Color(0.08f, 0.06f, 0.02f);
            _deployButton.color = locked ? UiKit.Faint : UiKit.Accent;
            Visible = true;
        }
    }
}
