using UnityEngine;
using UnityEngine.EventSystems;

namespace Skyforge
{
    /// <summary>
    /// Two floating thumb sticks. A stick appears where the thumb lands in its half of the screen
    /// and follows the thumb if it is dragged past the rim, so the player never has to look down.
    /// </summary>
    public sealed class TouchSticks
    {
        public struct Stick
        {
            public bool Active;
            public Vector2 Origin;
            public Vector2 Knob;
            public Vector2 Value;
            internal int FingerId;
        }

        const float DeadZone = 0.10f;

        public Stick Left, Right;

        public float RadiusPixels => Screen.height * 0.17f;

        public TouchSticks()
        {
            Left.FingerId = -1;
            Right.FingerId = -1;
        }

        public void Update()
        {
            for (int i = 0; i < Input.touchCount; i++)
            {
                Touch touch = Input.GetTouch(i);
                if (touch.phase == TouchPhase.Began)
                {
                    // A touch that starts on a button belongs to the button.
                    EventSystem events = EventSystem.current;
                    if (events != null && events.IsPointerOverGameObject(touch.fingerId)) continue;
                    if (touch.position.x < Screen.width * 0.5f) Begin(ref Left, touch);
                    else Begin(ref Right, touch);
                }
                else if (touch.fingerId == Left.FingerId && Left.Active) Track(ref Left, touch);
                else if (touch.fingerId == Right.FingerId && Right.Active) Track(ref Right, touch);
            }
            if (Input.touchCount == 0)
            {
                Release(ref Left);
                Release(ref Right);
            }
        }

        static void Begin(ref Stick stick, Touch touch)
        {
            if (stick.Active) return;
            stick.Active = true;
            stick.FingerId = touch.fingerId;
            stick.Origin = touch.position;
            stick.Knob = touch.position;
            stick.Value = Vector2.zero;
        }

        void Track(ref Stick stick, Touch touch)
        {
            if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
            {
                Release(ref stick);
                return;
            }

            float radius = RadiusPixels;
            Vector2 offset = touch.position - stick.Origin;
            if (offset.magnitude > radius)
            {
                stick.Origin = touch.position - offset.normalized * radius;
                offset = touch.position - stick.Origin;
            }
            stick.Knob = touch.position;
            stick.Value = Shape(offset / radius);
        }

        static void Release(ref Stick stick)
        {
            stick.Active = false;
            stick.FingerId = -1;
            stick.Value = Vector2.zero;
        }

        /// <summary>Dead zone in the middle, then a gentle curve for fine control near the centre.</summary>
        static Vector2 Shape(Vector2 raw)
        {
            float magnitude = Mathf.Min(raw.magnitude, 1f);
            if (magnitude < DeadZone) return Vector2.zero;
            float scaled = (magnitude - DeadZone) / (1f - DeadZone);
            float curved = Mathf.Lerp(scaled, scaled * scaled, 0.45f);
            return raw.normalized * curved;
        }
    }
}
