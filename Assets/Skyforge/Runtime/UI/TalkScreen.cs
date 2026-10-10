using System;
using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>A negotiation: someone speaks, the player picks one of three answers, and hears the reply.</summary>
    public sealed class TalkScreen
    {
        /// <summary>The player picked an answer; the game records it.</summary>
        public event Action<int> Chosen;
        public event Action ContinuePressed, BackPressed;

        readonly GameObject _root;
        readonly Text _speaker, _text;
        readonly Text[] _choiceLabels = new Text[3];
        readonly Image[] _choiceButtons = new Image[3];
        readonly GameObject[] _choices = new GameObject[3];
        readonly GameObject _continue;

        public TalkScreen(Transform parent)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Talk", 25);
            _root = canvas.gameObject;

            Image shade = UiKit.Box(canvas.transform, "Shade", new Color(0f, 0f, 0f, 0.6f));
            UiKit.Stretch(shade.rectTransform);
            shade.raycastTarget = true;

            RectTransform safe = UiKit.SafeArea(canvas.transform);
            Image panel = UiKit.Box(safe, "Panel", UiKit.ModalColor);
            UiKit.Place(panel.rectTransform, new Vector2(0.5f, 0.5f), Vector2.zero, new Vector2(1400f, 880f));
            Transform p = panel.transform;
            var topLeft = new Vector2(0f, 1f);

            Text title = UiKit.Label(p, 30, TextAnchor.UpperLeft);
            title.text = Loc.T("talk.title");
            title.color = UiKit.Accent;
            UiKit.Place(title.rectTransform, topLeft, new Vector2(60f, -40f), new Vector2(800f, 40f));

            Text back = UiKit.Button(p, "Back", UiKit.Faint, 28, () => BackPressed?.Invoke());
            back.text = Loc.T("hangar.back");
            UiKit.Place((RectTransform)back.transform.parent, new Vector2(1f, 1f), new Vector2(-50f, -34f), new Vector2(220f, 70f));

            _speaker = UiKit.Label(p, 44, TextAnchor.UpperLeft);
            UiKit.Place(_speaker.rectTransform, topLeft, new Vector2(58f, -110f), new Vector2(1200f, 56f));
            _text = UiKit.Label(p, 36, TextAnchor.UpperLeft, true);
            UiKit.Light(_text);
            _text.lineSpacing = 1.15f;
            UiKit.Place(_text.rectTransform, topLeft, new Vector2(60f, -190f), new Vector2(1280f, 240f));

            for (int i = 0; i < 3; i++)
            {
                int index = i;
                _choiceLabels[i] = UiKit.Button(p, "Choice" + i, UiKit.Faint, 32, () => Chosen?.Invoke(index));
                var rect = (RectTransform)_choiceLabels[i].transform.parent;
                UiKit.Place(rect, topLeft, new Vector2(60f, -470f - i * 124f), new Vector2(1280f, 108f));
                _choiceButtons[i] = rect.GetComponent<Image>();
                _choices[i] = rect.gameObject;
            }

            Text go = UiKit.Button(p, "Continue", UiKit.Accent, 38, () => ContinuePressed?.Invoke());
            go.text = Loc.T("talk.continue");
            go.color = new Color(0.08f, 0.06f, 0.02f);
            var goRect = (RectTransform)go.transform.parent;
            UiKit.Place(goRect, new Vector2(0.5f, 0f), new Vector2(0f, 60f), new Vector2(700f, 116f));
            _continue = goRect.gameObject;
        }

        public bool Visible
        {
            get => _root.activeSelf;
            set => _root.SetActive(value);
        }

        /// <summary>The question and the three answers. An answer the player cannot pay for is dimmed.</summary>
        public void Ask(Negotiation negotiation, SaveData save)
        {
            _speaker.text = Loc.T(negotiation.SpeakerKey);
            _text.text = Loc.T(negotiation.TextKey);
            for (int i = 0; i < 3; i++)
            {
                bool used = i < negotiation.Choices.Count;
                _choices[i].SetActive(used);
                if (!used) continue;
                NegotiationChoice choice = negotiation.Choices[i];
                bool affordable = Negotiations.CanAfford(save, choice);
                _choiceLabels[i].text = affordable ? Loc.T(choice.TextKey) : Loc.T(choice.TextKey) + "  (" + Loc.T("talk.cannot_pay") + ")";
                _choiceButtons[i].color = affordable ? new Color(1f, 1f, 1f, 0.40f) : UiKit.Faint;
            }
            _continue.SetActive(false);
            Visible = true;
        }

        /// <summary>What the other side says to the chosen answer; one button leads on to the mission.</summary>
        public void Reply(Negotiation negotiation, int index)
        {
            _speaker.text = Loc.T(negotiation.SpeakerKey);
            _text.text = Loc.T(negotiation.Choices[index].ReplyKey);
            foreach (GameObject choice in _choices) choice.SetActive(false);
            _continue.SetActive(true);
            Visible = true;
        }
    }
}
