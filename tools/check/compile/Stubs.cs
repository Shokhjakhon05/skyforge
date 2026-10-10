// Stand-ins for packages that have no reference assemblies: uGUI and URP. Signatures only.
using System;
using UnityEngine;
namespace UnityEngine.UI
{
    public class Graphic : MonoBehaviour { public Color color; public RectTransform rectTransform; public bool raycastTarget; public Material material; }
    public class MaskableGraphic : Graphic { }
    public class Text : MaskableGraphic { public string text; public Font font; public int fontSize; public TextAnchor alignment; public FontStyle fontStyle; public HorizontalWrapMode horizontalOverflow; public VerticalWrapMode verticalOverflow; public bool resizeTextForBestFit; public int resizeTextMinSize, resizeTextMaxSize; public bool supportRichText; public float lineSpacing; public float preferredWidth, preferredHeight; }
    public class Image : MaskableGraphic { public Sprite sprite; public Type type; public float fillAmount; public bool preserveAspect; public FillMethod fillMethod; public enum Type { Simple, Sliced, Tiled, Filled } public enum FillMethod { Horizontal, Vertical, Radial90, Radial180, Radial360 } }
    public class RawImage : MaskableGraphic { public Texture texture; public Rect uvRect; }
    public class Selectable : MonoBehaviour { public bool interactable; public Graphic targetGraphic; public ColorBlock colors; public Transition transition; public enum Transition { None, ColorTint } }
    public struct ColorBlock { public Color normalColor, highlightedColor, pressedColor, selectedColor, disabledColor; public float colorMultiplier, fadeDuration; public static ColorBlock defaultColorBlock; }
    public class Button : Selectable { public ButtonClickedEvent onClick = new ButtonClickedEvent(); public class ButtonClickedEvent : UnityEngine.Events.UnityEvent { } }
    public class CanvasScaler : MonoBehaviour { public ScaleMode uiScaleMode; public Vector2 referenceResolution; public float matchWidthOrHeight; public ScreenMatchMode screenMatchMode; public enum ScaleMode { ConstantPixelSize, ScaleWithScreenSize } public enum ScreenMatchMode { MatchWidthOrHeight, Expand, Shrink } }
    public class GraphicRaycaster : MonoBehaviour { }
    public class Shadow : MonoBehaviour { public Color effectColor; public Vector2 effectDistance; }
    public class Outline : Shadow { }
}
namespace UnityEngine.EventSystems
{
    public class EventSystem : MonoBehaviour { public static EventSystem current; public bool IsPointerOverGameObject(int id) => false; public bool IsPointerOverGameObject() => false; }
    public class StandaloneInputModule : MonoBehaviour { }
    public class PointerEventData { public Vector2 position; }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData eventData); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData eventData); }
}
namespace UnityEngine.Rendering
{
    public class VolumeParameter<T> { public T value; }
    public class VolumeComponent : ScriptableObject { public bool active; }
    public class VolumeProfile : ScriptableObject { public T Add<T>(bool overrides = false) where T : VolumeComponent => null; }
    public class Volume : MonoBehaviour { public bool isGlobal; public VolumeProfile sharedProfile; }
}
namespace UnityEngine.Rendering.Universal
{
    using UnityEngine.Rendering;
    public class ColorAdjustments : VolumeComponent { public VolumeParameter<float> contrast = new VolumeParameter<float>(), saturation = new VolumeParameter<float>(), postExposure = new VolumeParameter<float>(); }
    public class Vignette : VolumeComponent { public VolumeParameter<float> intensity = new VolumeParameter<float>(), smoothness = new VolumeParameter<float>(); public VolumeParameter<Color> color = new VolumeParameter<Color>(); }
    public class Bloom : VolumeComponent { public VolumeParameter<float> threshold = new VolumeParameter<float>(), intensity = new VolumeParameter<float>(), scatter = new VolumeParameter<float>(); }
    public class UniversalRenderPipelineAsset : RenderPipelineAsset { public override RenderPipeline CreatePipeline() => null; public float renderScale; public int msaaSampleCount; public float shadowDistance; public bool supportsHDR; }
    public enum TonemappingMode { None, Neutral, ACES }
    public class Tonemapping : VolumeComponent { public VolumeParameter<TonemappingMode> mode = new VolumeParameter<TonemappingMode>(); }
    public class UniversalAdditionalCameraData : MonoBehaviour { public bool renderPostProcessing; }
    public static class CameraExtensions { public static UniversalAdditionalCameraData GetUniversalAdditionalCameraData(this Camera camera) => null; }
}
