using Skyforge.Core;
using UnityEngine;
using UnityEngine.UI;

namespace Skyforge.UI
{
    /// <summary>
    /// What the fight writes over the picture: the sight in the middle of the screen, numbers that
    /// rise from a kill, the banner of a chain, a red frame when the hull is going, the sniper's
    /// scope and the instructor's notes of the first flights. Nothing here takes a touch.
    /// </summary>
    public sealed class FieldHud
    {
        const int Pops = 10;

        struct Pop
        {
            public Text Text;
            public Vector3 World;
            public float Age;
            public bool Active;
        }

        readonly Canvas _canvas;
        readonly RectTransform _screen;
        readonly GameObject _reticle, _scope, _coach, _lootBox;
        readonly Image _reticleRing, _vignette, _coachArrow;
        readonly Pop[] _pops = new Pop[Pops];
        readonly Text _banner, _bannerSub, _scopeText, _scopeRounds, _coachText, _coachStep, _loot;
        float _bannerAge = 99f, _vignetteShown, _hit;
        int _shownLoot = -1;
        string _shownScope, _shownRounds, _shownCoach;

        public FieldHud(Transform parent)
        {
            _canvas = UiKit.CreateCanvas(parent, "Hud", 1);
            // Nothing on this canvas is pressed, and it must not swallow what is meant for the one below.
            Object.Destroy(_canvas.GetComponent<GraphicRaycaster>());
            _screen = UiKit.Rect("Screen", _canvas.transform);
            UiKit.Stretch(_screen);
            var middle = new Vector2(0.5f, 0.5f);

            _vignette = Quiet(UiKit.Box(_screen, "Vignette", new Color(0.85f, 0.05f, 0.03f, 0f), Sprite.Create(VignetteTexture(), new Rect(0, 0, 64, 64), middle)));
            UiKit.Stretch(_vignette.rectTransform);

            // The sight: where a rocket goes when nothing is locked.
            RectTransform reticle = UiKit.Rect("Reticle", _screen);
            UiKit.Place(reticle, middle, Vector2.zero, new Vector2(120f, 120f));
            reticle.pivot = middle;
            _reticle = reticle.gameObject;
            _reticleRing = Quiet(UiKit.Box(reticle, "Ring", new Color(1f, 1f, 1f, 0.75f), UiKit.Ring));
            UiKit.Place(_reticleRing.rectTransform, middle, Vector2.zero, new Vector2(64f, 64f));
            _reticleRing.rectTransform.pivot = middle;
            Image dot = Quiet(UiKit.Box(reticle, "Dot", new Color(1f, 1f, 1f, 0.9f), UiKit.Disc));
            UiKit.Place(dot.rectTransform, middle, Vector2.zero, new Vector2(8f, 8f));
            dot.rectTransform.pivot = middle;
            foreach (Vector2 arm in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            {
                Image tick = Quiet(UiKit.Box(reticle, "Tick", new Color(1f, 1f, 1f, 0.75f)));
                UiKit.Place(tick.rectTransform, middle, arm * 46f, arm.x != 0f ? new Vector2(18f, 3f) : new Vector2(3f, 18f));
                tick.rectTransform.pivot = middle;
            }
            _reticle.SetActive(false);

            for (int i = 0; i < Pops; i++)
            {
                Text text = UiKit.Label(_screen, 34, TextAnchor.MiddleCenter);
                text.raycastTarget = false;
                text.font = UiKit.TitleFont;
                text.rectTransform.anchorMin = text.rectTransform.anchorMax = Vector2.zero;
                text.rectTransform.sizeDelta = new Vector2(420f, 50f);
                text.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.7f);
                text.gameObject.SetActive(false);
                _pops[i].Text = text;
            }

            _banner = UiKit.Label(_screen, 64, TextAnchor.MiddleCenter);
            _banner.raycastTarget = false;
            _banner.font = UiKit.TitleFont;
            UiKit.Place(_banner.rectTransform, middle, new Vector2(0f, -190f), new Vector2(1500f, 90f));
            _banner.rectTransform.pivot = middle;
            _banner.gameObject.AddComponent<Outline>().effectColor = new Color(0f, 0f, 0f, 0.8f);
            _bannerSub = UiKit.Label(_screen, 30, TextAnchor.MiddleCenter);
            _bannerSub.raycastTarget = false;
            UiKit.Place(_bannerSub.rectTransform, middle, new Vector2(0f, -250f), new Vector2(1500f, 44f));
            _bannerSub.rectTransform.pivot = middle;
            _banner.text = _bannerSub.text = "";

            // Credits picked up on the field, under the top-right corner's furniture.
            Image lootBox = Quiet(UiKit.Box(_screen, "Loot", new Color(0f, 0f, 0f, 0.35f), UiKit.Chamfer));
            lootBox.type = Image.Type.Sliced;
            UiKit.Place(lootBox.rectTransform, new Vector2(1f, 1f), new Vector2(-40f, -250f), new Vector2(230f, 48f));
            _lootBox = lootBox.gameObject;
            _loot = UiKit.Label(lootBox.transform, 28, TextAnchor.MiddleCenter);
            _loot.raycastTarget = false;
            _loot.color = new Color(1f, 0.84f, 0.3f);
            UiKit.Stretch(_loot.rectTransform);
            _lootBox.SetActive(false);

            // The scope of a sniper's rifle: black all round, a fine cross, range ticks below the centre.
            RectTransform scope = UiKit.Rect("Scope", _screen);
            UiKit.Stretch(scope);
            _scope = scope.gameObject;
            Image mask = Quiet(UiKit.Box(scope, "Mask", Color.black, Sprite.Create(ScopeTexture(), new Rect(0, 0, 256, 256), middle)));
            mask.rectTransform.anchorMin = mask.rectTransform.anchorMax = middle;
            mask.rectTransform.pivot = middle;
            mask.rectTransform.sizeDelta = new Vector2(1080f, 1080f);
            foreach (float side in new[] { -1f, 1f })
            {
                Image bar = Quiet(UiKit.Box(scope, "Side", Color.black));
                bar.rectTransform.anchorMin = new Vector2(side < 0f ? 0f : 0.5f, 0f);
                bar.rectTransform.anchorMax = new Vector2(side < 0f ? 0.5f : 1f, 1f);
                bar.rectTransform.offsetMin = new Vector2(side < 0f ? 0f : 538f, 0f);
                bar.rectTransform.offsetMax = new Vector2(side < 0f ? -538f : 0f, 0f);
            }
            var ink = new Color(0f, 0f, 0f, 0.9f);
            foreach (Vector2 arm in new[] { Vector2.left, Vector2.right, Vector2.up, Vector2.down })
            {
                Image line = Quiet(UiKit.Box(scope, "Hair", ink));
                UiKit.Place(line.rectTransform, middle, arm * 280f, arm.x != 0f ? new Vector2(500f, 2f) : new Vector2(2f, 500f));
                line.rectTransform.pivot = middle;
            }
            for (int i = 1; i <= 4; i++)
            {
                // Hold-over marks: each is one more step of drop below the cross.
                Image tick = Quiet(UiKit.Box(scope, "Drop", ink));
                UiKit.Place(tick.rectTransform, middle, new Vector2(0f, -i * 34f), new Vector2(46f - i * 7f, 2f));
                tick.rectTransform.pivot = middle;
            }
            Image centre = Quiet(UiKit.Box(scope, "Centre", new Color(0.9f, 0.1f, 0.05f, 0.95f), UiKit.Disc));
            UiKit.Place(centre.rectTransform, middle, Vector2.zero, new Vector2(7f, 7f));
            centre.rectTransform.pivot = middle;
            _scopeText = UiKit.Label(scope, 28, TextAnchor.UpperLeft);
            _scopeText.raycastTarget = false;
            _scopeText.color = new Color(0.75f, 1f, 0.8f);
            UiKit.Place(_scopeText.rectTransform, middle, new Vector2(150f, -150f), new Vector2(420f, 130f));
            _scopeText.rectTransform.pivot = new Vector2(0f, 1f);
            _scopeRounds = UiKit.Label(scope, 44, TextAnchor.MiddleCenter);
            _scopeRounds.raycastTarget = false;
            _scopeRounds.font = UiKit.TitleFont;
            _scopeRounds.color = UiKit.Accent;
            UiKit.Place(_scopeRounds.rectTransform, new Vector2(0.5f, 0f), new Vector2(0f, 40f), new Vector2(700f, 60f));
            _scopeRounds.rectTransform.pivot = new Vector2(0.5f, 0f);
            _scope.SetActive(false);

            // The instructor: a line of advice across the top of the picture and an arrow at what it means.
            Image coach = Quiet(UiKit.Box(_screen, "Coach", new Color(0.02f, 0.06f, 0.10f, 0.82f), UiKit.Chamfer));
            coach.type = Image.Type.Sliced;
            UiKit.Place(coach.rectTransform, new Vector2(0.5f, 1f), new Vector2(0f, -210f), new Vector2(1180f, 124f));
            coach.rectTransform.pivot = new Vector2(0.5f, 1f);
            _coach = coach.gameObject;
            Image edge = Quiet(UiKit.Box(coach.transform, "Edge", UiKit.Accent));
            UiKit.Place(edge.rectTransform, new Vector2(0f, 0.5f), new Vector2(0f, 0f), new Vector2(8f, 124f));
            edge.rectTransform.pivot = new Vector2(0f, 0.5f);
            _coachStep = UiKit.Label(coach.transform, 22, TextAnchor.UpperLeft);
            _coachStep.raycastTarget = false;
            _coachStep.color = UiKit.Accent;
            UiKit.Place(_coachStep.rectTransform, new Vector2(0f, 1f), new Vector2(30f, -10f), new Vector2(600f, 30f));
            _coachStep.rectTransform.pivot = new Vector2(0f, 1f);
            _coachText = UiKit.Label(coach.transform, 32, TextAnchor.MiddleLeft, true);
            _coachText.raycastTarget = false;
            _coachText.rectTransform.anchorMin = Vector2.zero;
            _coachText.rectTransform.anchorMax = Vector2.one;
            _coachText.rectTransform.offsetMin = new Vector2(30f, 8f);
            _coachText.rectTransform.offsetMax = new Vector2(-24f, -36f);
            UiKit.FitWidth(_coachText, 22);
            _coachArrow = Quiet(UiKit.Box(_screen, "CoachArrow", UiKit.Accent, UiKit.Ring));
            _coachArrow.rectTransform.anchorMin = _coachArrow.rectTransform.anchorMax = Vector2.zero;
            _coachArrow.rectTransform.pivot = middle;
            _coachArrow.rectTransform.sizeDelta = new Vector2(190f, 190f);
            _coachArrow.gameObject.SetActive(false);
            _coach.SetActive(false);
        }

        static T Quiet<T>(T graphic) where T : Graphic
        {
            graphic.raycastTarget = false;
            return graphic;
        }

        static Texture2D VignetteTexture()
        {
            var texture = new Texture2D(64, 64, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                float dx = (x - 31.5f) / 31.5f, dy = (y - 31.5f) / 31.5f;
                // Square-ish, so the corners and the edges both darken on a wide screen.
                float d = Mathf.Pow(Mathf.Pow(Mathf.Abs(dx), 3f) + Mathf.Pow(Mathf.Abs(dy), 3f), 1f / 3f);
                pixels[y * 64 + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.72f, 1.1f, d)));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        static Texture2D ScopeTexture()
        {
            var texture = new Texture2D(256, 256, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var pixels = new Color32[256 * 256];
            for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                float d = new Vector2(x - 127.5f, y - 127.5f).magnitude / 127.5f;
                pixels[y * 256 + x] = new Color(1f, 1f, 1f, Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.86f, 0.9f, d)));
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        public bool Visible
        {
            get => _canvas.gameObject.activeSelf;
            set
            {
                if (_canvas.gameObject.activeSelf != value) _canvas.gameObject.SetActive(value);
            }
        }

        /// <summary>Clears what belongs to one flight.</summary>
        public void Reset()
        {
            for (int i = 0; i < Pops; i++)
            {
                _pops[i].Active = false;
                _pops[i].Text.gameObject.SetActive(false);
            }
            _banner.text = _bannerSub.text = "";
            _bannerAge = 99f;
            _hit = 0f;
            SetReticle(false, false);
            SetScope(null, null);
            SetCoach(null, null, null);
            SetLoot(0);
        }

        public void SetReticle(bool shown, bool locked)
        {
            if (_reticle.activeSelf != shown) _reticle.SetActive(shown);
            if (!shown) return;
            Color wanted = locked ? new Color(0.35f, 1f, 0.5f, 0.9f) : new Color(1f, 1f, 1f, 0.75f);
            if (_reticleRing.color != wanted) _reticleRing.color = wanted;
        }

        /// <summary>A number or a word that rises from a point in the world and fades.</summary>
        public void Rise(Vector3 world, string text, Color color)
        {
            int slot = 0;
            for (int i = 0; i < Pops; i++)
            {
                if (!_pops[i].Active) { slot = i; break; }
                if (_pops[i].Age > _pops[slot].Age) slot = i;
            }
            _pops[slot].Active = true;
            _pops[slot].Age = 0f;
            _pops[slot].World = world;
            _pops[slot].Text.text = text;
            _pops[slot].Text.color = color;
            _pops[slot].Text.gameObject.SetActive(true);
        }

        /// <summary>The big line of a chain of kills, with a smaller one under it.</summary>
        public void Banner(string text, string sub, Color color)
        {
            _banner.text = text;
            _banner.color = color;
            _bannerSub.text = sub ?? "";
            _bannerAge = 0f;
        }

        /// <summary>A round has hit the aircraft: the frame flashes.</summary>
        public void Hit(float amount) => _hit = Mathf.Max(_hit, Mathf.Clamp01(amount));

        public void SetLoot(int credits)
        {
            if (credits == _shownLoot) return;
            _shownLoot = credits;
            _lootBox.SetActive(credits > 0);
            _loot.text = "+" + credits + "  ◆";
        }

        /// <summary>The scope's data and the rounds left; null takes the scope away.</summary>
        public void SetScope(string data, string rounds)
        {
            bool shown = data != null;
            if (_scope.activeSelf != shown) _scope.SetActive(shown);
            if (!shown) return;
            if (data != _shownScope) _scopeText.text = _shownScope = data;
            if (rounds != _shownRounds) _scopeRounds.text = _shownRounds = rounds;
        }

        /// <summary>The instructor's line. <paramref name="point"/> is where on the screen (0..1) it points, if anywhere.</summary>
        public void SetCoach(string step, string text, Vector2? point)
        {
            bool shown = text != null;
            if (_coach.activeSelf != shown) _coach.SetActive(shown);
            if (_coachArrow.gameObject.activeSelf != (shown && point.HasValue)) _coachArrow.gameObject.SetActive(shown && point.HasValue);
            if (!shown) return;
            if (text != _shownCoach)
            {
                _coachText.text = _shownCoach = text;
                _coachStep.text = step ?? "";
            }
            if (point.HasValue)
                _coachArrow.rectTransform.anchoredPosition = new Vector2(point.Value.x * Screen.width, point.Value.y * Screen.height) / _canvas.scaleFactor;
        }

        /// <param name="hull">What is left of the aircraft, 0..1: below a third the frame stays red.</param>
        public void Update(Camera camera, float hull, float deltaTime)
        {
            for (int i = 0; i < Pops; i++)
            {
                if (!_pops[i].Active) continue;
                _pops[i].Age += deltaTime;
                float t = _pops[i].Age / 1.5f;
                Vector3 point = camera.WorldToScreenPoint(_pops[i].World + Vector3.up * (3f + t * 5f));
                if (t >= 1f || point.z < 0f)
                {
                    _pops[i].Active = false;
                    _pops[i].Text.gameObject.SetActive(false);
                    continue;
                }
                Text text = _pops[i].Text;
                text.rectTransform.anchoredPosition = new Vector2(point.x, point.y) / _canvas.scaleFactor;
                Color color = text.color;
                color.a = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                text.color = color;
                float pop = t < 0.12f ? Mathf.Lerp(1.6f, 1f, t / 0.12f) : 1f;
                text.rectTransform.localScale = new Vector3(pop, pop, 1f);
            }

            if (_bannerAge < 2.6f)
            {
                _bannerAge += deltaTime;
                float alpha = _bannerAge < 1.9f ? 1f : Mathf.Clamp01(1f - (_bannerAge - 1.9f) / 0.7f);
                float scale = _bannerAge < 0.16f ? Mathf.Lerp(2.2f, 1f, _bannerAge / 0.16f) : 1f;
                _banner.rectTransform.localScale = new Vector3(scale, scale, 1f);
                Color c = _banner.color;
                c.a = alpha;
                _banner.color = c;
                Color s = _bannerSub.color;
                s.a = alpha;
                _bannerSub.color = s;
                if (_bannerAge >= 2.6f) _banner.text = _bannerSub.text = "";
            }

            _hit = Mathf.Max(0f, _hit - deltaTime * 1.6f);
            float low = hull < 0.34f ? 0.24f + 0.12f * Mathf.Sin(Time.unscaledTime * 5f) : 0f;
            float wanted = Mathf.Max(low, _hit * 0.55f);
            _vignetteShown = Mathf.MoveTowards(_vignetteShown, wanted, deltaTime * 2.4f);
            Color v = _vignette.color;
            if (Mathf.Abs(v.a - _vignetteShown) > 0.004f)
            {
                v.a = _vignetteShown;
                _vignette.color = v;
            }

            if (_coachArrow.gameObject.activeSelf)
            {
                float pulse = 1f + 0.14f * Mathf.Sin(Time.unscaledTime * 6f);
                _coachArrow.rectTransform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }
    }
}
