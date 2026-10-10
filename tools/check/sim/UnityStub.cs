// A tiny stand-in for the parts of UnityEngine the game's core uses, so its tests can run
// outside Unity. Perlin noise is not Unity's, so the islands here are siblings of the real ones.
using System;
using System.Collections.Generic;
using System.Text.Json;
namespace UnityEngine
{
    public struct Vector2
    {
        public float x, y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float magnitude => MathF.Sqrt(x * x + y * y);
        public float sqrMagnitude => x * x + y * y;
        public Vector2 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector2(x / m, y / m) : zero; } }
        public static Vector2 zero => new Vector2(0, 0);
        public static Vector2 one => new Vector2(1, 1);
        public static Vector2 up => new Vector2(0, 1);
        public static Vector2 right => new Vector2(1, 0);
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static float Dot(Vector2 a, Vector2 b) => a.x * b.x + a.y * b.y;
        public static float Distance(Vector2 a, Vector2 b) => (a - b).magnitude;
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) { t = Mathf.Clamp01(t); return new Vector2(a.x + (b.x - a.x) * t, a.y + (b.y - a.y) * t); }
        public static Vector2 ClampMagnitude(Vector2 v, float max) => v.magnitude > max ? v.normalized * max : v;
        public static float Angle(Vector2 a, Vector2 b)
        {
            float d = MathF.Sqrt(a.sqrMagnitude * b.sqrMagnitude);
            if (d < 1e-15f) return 0f;
            return MathF.Acos(Mathf.Clamp(Dot(a, b) / d, -1f, 1f)) * Mathf.Rad2Deg;
        }
        public override string ToString() => $"({x:0.00}, {y:0.00})";
    }
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float magnitude => MathF.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { float m = magnitude; return m > 1e-5f ? new Vector3(x / m, y / m, z / m) : zero; } }
        public void Normalize() { this = normalized; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 down => new Vector3(0, -1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 back => new Vector3(0, 0, -1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public static Vector3 left => new Vector3(-1, 0, 0);
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static Vector3 Cross(Vector3 a, Vector3 b) => new Vector3(a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 ProjectOnPlane(Vector3 v, Vector3 n) => v - n * (Dot(v, n) / Mathf.Max(1e-8f, n.sqrMagnitude));
        public override string ToString() => $"({x:0.00}, {y:0.00}, {z:0.00})";
    }
    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b, float a = 1f) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1, 1, 1, 1);
        public static Color black => new Color(0, 0, 0, 1);
        public static Color Lerp(Color x, Color y, float t) { t = Mathf.Clamp01(t); return new Color(x.r + (y.r - x.r) * t, x.g + (y.g - x.g) * t, x.b + (y.b - x.b) * t, x.a + (y.a - x.a) * t); }
        public static Color operator *(Color c, float d) => new Color(c.r * d, c.g * d, c.b * d, c.a * d);
    }
    public static class Mathf
    {
        public const float PI = MathF.PI, Deg2Rad = MathF.PI / 180f, Rad2Deg = 180f / MathF.PI, Infinity = float.PositiveInfinity, Epsilon = float.Epsilon;
        public static float Sin(float f) => MathF.Sin(f);
        public static float Cos(float f) => MathF.Cos(f);
        public static float Tan(float f) => MathF.Tan(f);
        public static float Atan(float f) => MathF.Atan(f);
        public static float Atan2(float y, float x) => MathF.Atan2(y, x);
        public static float Acos(float f) => MathF.Acos(f);
        public static float Sqrt(float f) => MathF.Sqrt(f);
        public static float Abs(float f) => MathF.Abs(f);
        public static int Abs(int f) => Math.Abs(f);
        public static float Exp(float f) => MathF.Exp(f);
        public static float Pow(float f, float p) => MathF.Pow(f, p);
        public static float Sign(float f) => f >= 0f ? 1f : -1f;
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Clamp(float v, float min, float max) => v < min ? min : v > max ? max : v;
        public static int Clamp(int v, int min, int max) => v < min ? min : v > max ? max : v;
        public static float Clamp01(float v) => v < 0f ? 0f : v > 1f ? 1f : v;
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a != b ? Clamp01((v - a) / (b - a)) : 0f;
        public static float SmoothStep(float from, float to, float t) { t = Clamp01(t); t = -2f * t * t * t + 3f * t * t; return to * t + from * (1f - t); }
        public static float Repeat(float t, float length) => Clamp(t - MathF.Floor(t / length) * length, 0f, length);
        public static float DeltaAngle(float current, float target) { float d = Repeat(target - current, 360f); if (d > 180f) d -= 360f; return d; }
        public static float LerpAngle(float a, float b, float t) => a + DeltaAngle(a, b) * Clamp01(t);
        public static float MoveTowards(float current, float target, float maxDelta) => Abs(target - current) <= maxDelta ? target : current + Sign(target - current) * maxDelta;
        public static int RoundToInt(float f) => (int)Math.Round(f, MidpointRounding.ToEven);
        public static int FloorToInt(float f) => (int)MathF.Floor(f);
        public static int CeilToInt(float f) => (int)MathF.Ceiling(f);
        public static float Floor(float f) => MathF.Floor(f);
        public static float Round(float f) => MathF.Round(f);

        // Unity's Mathf.PerlinNoise: Ken Perlin's improved noise in two dimensions with his reference
        // permutation, rescaled from about [-0.69, 0.79] to [0, 1]. Checked against the game itself:
        // with this the stub's islands have the pad heights the CI build logs for the real ones.
        static readonly int[] Perm = BuildPerm();
        static int[] BuildPerm()
        {
            int[] source =
            {
                151, 160, 137, 91, 90, 15, 131, 13, 201, 95, 96, 53, 194, 233, 7, 225, 140, 36, 103, 30, 69, 142, 8, 99, 37, 240, 21, 10, 23,
                190, 6, 148, 247, 120, 234, 75, 0, 26, 197, 62, 94, 252, 219, 203, 117, 35, 11, 32, 57, 177, 33, 88, 237, 149, 56, 87, 174,
                20, 125, 136, 171, 168, 68, 175, 74, 165, 71, 134, 139, 48, 27, 166, 77, 146, 158, 231, 83, 111, 229, 122, 60, 211, 133,
                230, 220, 105, 92, 41, 55, 46, 245, 40, 244, 102, 143, 54, 65, 25, 63, 161, 1, 216, 80, 73, 209, 76, 132, 187, 208, 89, 18,
                169, 200, 196, 135, 130, 116, 188, 159, 86, 164, 100, 109, 198, 173, 186, 3, 64, 52, 217, 226, 250, 124, 123, 5, 202, 38,
                147, 118, 126, 255, 82, 85, 212, 207, 206, 59, 227, 47, 16, 58, 17, 182, 189, 28, 42, 223, 183, 170, 213, 119, 248, 152, 2,
                44, 154, 163, 70, 221, 153, 101, 155, 167, 43, 172, 9, 129, 22, 39, 253, 19, 98, 108, 110, 79, 113, 224, 232, 178, 185, 112,
                104, 218, 246, 97, 228, 251, 34, 242, 193, 238, 210, 144, 12, 191, 179, 162, 241, 81, 51, 145, 235, 249, 14, 239, 107, 49,
                192, 214, 31, 181, 199, 106, 157, 184, 84, 204, 176, 115, 121, 50, 45, 127, 4, 150, 254, 138, 236, 205, 93, 222, 114, 67,
                29, 24, 72, 243, 141, 128, 195, 78, 66, 215, 61, 156, 180,
            };
            var p = new int[514];
            for (int i = 0; i < 514; i++) p[i] = source[i & 255];
            return p;
        }
        static float Fade(float t) => t * t * t * (t * (t * 6f - 15f) + 10f);
        static float Grad(int hash, float x, float y)
        {
            int h = hash & 15;
            float u = h < 8 ? x : y;
            float v = h < 4 ? y : h == 12 || h == 14 ? x : 0f;
            return ((h & 1) == 0 ? u : -u) + ((h & 2) == 0 ? v : -v);
        }
        static float Mix(float t, float a, float b) => a + t * (b - a);
        public static float PerlinNoise(float x, float y)
        {
            x = MathF.Abs(x); y = MathF.Abs(y);
            int X = (int)MathF.Floor(x) & 255, Y = (int)MathF.Floor(y) & 255;
            x -= MathF.Floor(x); y -= MathF.Floor(y);
            float u = Fade(x), v = Fade(y);
            int A = Perm[X] + Y, AA = Perm[A], AB = Perm[A + 1], B = Perm[X + 1] + Y, BA = Perm[B], BB = Perm[B + 1];
            float value = Mix(v, Mix(u, Grad(Perm[AA], x, y), Grad(Perm[BA], x - 1f, y)),
                Mix(u, Grad(Perm[AB], x, y - 1f), Grad(Perm[BB], x - 1f, y - 1f)));
            return (value + 0.69f) / (0.793f + 0.69f);
        }
    }
    public static class Debug
    {
        public static void Log(object o) => Console.WriteLine(o);
        public static void LogWarning(object o) => Console.WriteLine("WARN " + o);
        public static void LogError(object o) => Console.WriteLine("ERROR " + o);
    }
    public static class JsonUtility
    {
        static readonly JsonSerializerOptions Options = new JsonSerializerOptions { IncludeFields = true, WriteIndented = true, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
        public static T FromJson<T>(string json)
        {
            try { return JsonSerializer.Deserialize<T>(json, Options); }
            catch (JsonException e) { throw new ArgumentException(e.Message); }
        }
        public static string ToJson(object o, bool pretty = false) => JsonSerializer.Serialize(o, o.GetType(), Options);
    }
    public enum SystemLanguage { English, Russian, Ukrainian, Belarusian, Unknown }
    public static class Application
    {
        public static string dataPath = Environment.GetEnvironmentVariable("SKYFORGE_ASSETS") ?? "Assets";
        public static string persistentDataPath = System.IO.Path.GetTempPath();
        public static string temporaryCachePath = System.IO.Path.GetTempPath();
        public static SystemLanguage systemLanguage = SystemLanguage.English;
    }
    public static class Time { public static float deltaTime = 1f / 60f; public static float time = 0f; public static float realtimeSinceStartup = 0f; }
}
