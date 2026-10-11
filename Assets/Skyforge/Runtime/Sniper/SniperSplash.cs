using Skyforge;
using Skyforge.UI;
using UnityEngine;
using UnityEngine.UI;

namespace Sniper
{
    /// <summary>
    /// What the game opens on: darkness, a scope that draws itself, a cross that closes on a red
    /// point, one shot, and the name. Four seconds, cut short by a touch.
    /// </summary>
    public sealed class SniperSplash : MonoBehaviour
    {
        const float Seconds = 4.4f;
        const string Title = "ZERO HOUR";
        CanvasGroup _group;
        Image _back, _ring, _inner, _dot, _flash, _streak;
        RectTransform[] _arms;
        Text[] _letters;
        Text _tag, _sub;
        float _time;
        bool _fired, _frozen;
        Sfx _sfx;

        public static SniperSplash Create(Transform parent, Sfx sfx)
        {
            Canvas canvas = UiKit.CreateCanvas(parent, "Toast", 100);
            canvas.name = "Splash";
            var splash = canvas.gameObject.AddComponent<SniperSplash>();
            splash._sfx = sfx;
            splash._group = canvas.gameObject.AddComponent<CanvasGroup>();
            var middle = new Vector2(0.5f, 0.5f);
            var centre = new Vector2(0f, 90f);
            splash._back = UiKit.Box(canvas.transform, "Back", new Color(0.012f, 0.014f, 0.02f, 1f));
            splash._back.raycastTarget = true;
            UiKit.Stretch(splash._back.rectTransform);

            splash._ring = UiKit.Box(canvas.transform, "Ring", new Color(1f, 1f, 1f, 0.9f), UiKit.Ring);
            splash._ring.type = Image.Type.Filled;
            splash._ring.fillMethod = Image.FillMethod.Radial360;
            UiKit.Place(splash._ring.rectTransform, middle, centre, new Vector2(420f, 420f));
            splash._inner = UiKit.Box(canvas.transform, "Inner", new Color(1f, 1f, 1f, 0.35f), UiKit.Ring);
            splash._inner.type = Image.Type.Filled;
            splash._inner.fillMethod = Image.FillMethod.Radial360;
            UiKit.Place(splash._inner.rectTransform, middle, centre, new Vector2(210f, 210f));

            // The four arms of the cross, which come in from outside the ring.
            splash._arms = new RectTransform[4];
            for (int i = 0; i < 4; i++)
            {
                Image arm = UiKit.Box(canvas.transform, "Arm", new Color(1f, 1f, 1f, 0.9f));
                UiKit.Place(arm.rectTransform, middle, centre, i % 2 == 0 ? new Vector2(150f, 3f) : new Vector2(3f, 150f));
                splash._arms[i] = arm.rectTransform;
            }
            splash._dot = UiKit.Box(canvas.transform, "Dot", new Color(0.95f, 0.16f, 0.1f, 0f), UiKit.Disc);
            UiKit.Place(splash._dot.rectTransform, middle, centre, new Vector2(22f, 22f));
            splash._flash = UiKit.Box(canvas.transform, "Flash", new Color(1f, 0.92f, 0.8f, 0f), UiKit.Disc);
            UiKit.Place(splash._flash.rectTransform, middle, centre, new Vector2(200f, 200f));
            splash._streak = UiKit.Box(canvas.transform, "Streak", new Color(1f, 0.8f, 0.4f, 0f));
            UiKit.Place(splash._streak.rectTransform, middle, centre, new Vector2(900f, 4f));

            splash._letters = new Text[Title.Length];
            const float pitch = 118f;
            for (int i = 0; i < Title.Length; i++)
            {
                Text letter = UiKit.Label(canvas.transform, 124, TextAnchor.MiddleCenter);
                letter.font = UiKit.TitleFont;
                letter.text = Title[i].ToString();
                UiKit.Place(letter.rectTransform, middle, new Vector2((i - (Title.Length - 1) * 0.5f) * pitch, -230f), new Vector2(160f, 150f));
                letter.gameObject.AddComponent<Shadow>().effectColor = new Color(0.9f, 0.2f, 0.1f, 0.35f);
                splash._letters[i] = letter;
            }
            splash._tag = UiKit.Label(canvas.transform, 34, TextAnchor.MiddleCenter);
            splash._tag.text = "S N I P E R     C O N T R A C T S";
            UiKit.Place(splash._tag.rectTransform, middle, new Vector2(0f, -330f), new Vector2(1500f, 46f));
            splash._sub = UiKit.Label(canvas.transform, 22, TextAnchor.MiddleCenter);
            splash._sub.text = Loc.T("sn.splash.sub");
            UiKit.Place(splash._sub.rectTransform, middle, new Vector2(0f, -384f), new Vector2(1500f, 32f));
            splash.Apply();
            return splash;
        }

        /// <summary>Held at one moment, for a picture of it.</summary>
        public void Freeze(float at)
        {
            _time = at;
            _frozen = true;
            Apply();
        }

        void Update()
        {
            if (_frozen) return;
            _time += Mathf.Min(Time.unscaledDeltaTime, 0.05f);
            if (_time > 0.7f && (Input.touchCount > 0 || Input.GetMouseButtonDown(0))) _time = Mathf.Max(_time, Seconds - 0.45f);
            if (!_fired && _time >= 1.9f)
            {
                _fired = true;
                if (_sfx != null) _sfx.Play(Sound.Lock, 0.7f);
            }
            Apply();
            if (_time >= Seconds) Destroy(gameObject);
        }

        static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return 1f - (1f - t) * (1f - t) * (1f - t);
        }

        void Apply()
        {
            _ring.fillAmount = Ease(_time / 0.9f);
            _ring.rectTransform.localRotation = Quaternion.Euler(0f, 0f, -_time * 20f);
            _inner.fillAmount = Ease((_time - 0.3f) / 0.8f);
            _inner.rectTransform.localRotation = Quaternion.Euler(0f, 0f, _time * 50f);
            // The arms slide in until their tips nearly meet.
            float close = Ease((_time - 0.5f) / 0.9f);
            float gap = Mathf.Lerp(330f, 96f, close);
            Vector2 centre = new Vector2(0f, 90f);
            _arms[0].anchoredPosition = centre + new Vector2(-gap, 0f);
            _arms[2].anchoredPosition = centre + new Vector2(gap, 0f);
            _arms[1].anchoredPosition = centre + new Vector2(0f, gap);
            _arms[3].anchoredPosition = centre + new Vector2(0f, -gap);
            float lit = Mathf.Clamp01((_time - 0.4f) / 0.4f);
            foreach (RectTransform arm in _arms) arm.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.9f * lit);

            // The point it closes on, beating, then the shot.
            float beat = 0.75f + 0.25f * Mathf.Sin(_time * 14f);
            float shown = Mathf.Clamp01((_time - 1.1f) / 0.3f) * (_time < 1.9f ? 1f : Mathf.Clamp01(1f - (_time - 1.9f) / 0.15f));
            _dot.color = new Color(0.95f, 0.16f, 0.1f, shown * beat);
            float bang = Mathf.Clamp01((_time - 1.9f) / 0.5f);
            _flash.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.3f, 7f, Ease(bang));
            _flash.color = new Color(1f, 0.92f, 0.8f, _time < 1.9f ? 0f : 0.75f * (1f - bang));
            _streak.color = new Color(1f, 0.8f, 0.4f, _time < 1.9f ? 0f : 0.9f * (1f - Mathf.Clamp01((_time - 1.9f) / 0.35f)));
            _streak.rectTransform.localScale = new Vector3(Mathf.Lerp(0.2f, 2.4f, Ease(bang)), 1f, 1f);
            // Recoil: the whole glass kicks once.
            float kick = _time < 1.9f ? 0f : Mathf.Exp(-(_time - 1.9f) * 9f) * 26f;
            _ring.rectTransform.anchoredPosition = centre + new Vector2(0f, kick);
            _inner.rectTransform.anchoredPosition = centre + new Vector2(0f, kick);

            for (int i = 0; i < _letters.Length; i++)
            {
                float t = Mathf.Clamp01((_time - 2.05f - i * 0.06f) / 0.3f);
                _letters[i].color = new Color(1f, 1f, 1f, t);
                float scale = Mathf.Lerp(1.7f, 1f, Ease(t));
                _letters[i].rectTransform.localScale = new Vector3(scale, scale, 1f);
            }
            float tag = Ease((_time - 2.7f) / 0.5f);
            _tag.color = new Color(0.95f, 0.3f, 0.2f, tag);
            _sub.color = new Color(1f, 1f, 1f, 0.5f * Ease((_time - 3f) / 0.5f));

            float open = Ease((_time - (Seconds - 0.8f)) / 0.5f);
            Color back = _back.color;
            back.a = 1f - open * 0.8f;
            _back.color = back;
            _group.alpha = 1f - Ease((_time - (Seconds - 0.4f)) / 0.4f);
        }
    }
}
