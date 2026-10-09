using UnityEngine;

namespace Skyforge
{
    /// <summary>
    /// Stage 0 check scene: proves that the CI build starts on a phone, that URP renders lit
    /// geometry with shadows, and shows the frame rate. Replaced by the flight scene in stage 1.
    /// </summary>
    public sealed class SmokeScene : MonoBehaviour
    {
        Transform _cube;
        GUIStyle _style;
        float _fps;

        void Start()
        {
            var lit = Resources.Load<Material>("Materials/Lit");

            var cameraObject = new GameObject("Camera");
            var cam = cameraObject.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.07f, 0.10f, 0.16f);
            cam.fieldOfView = 55f;
            cameraObject.transform.position = new Vector3(0f, 2.2f, -6f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.9f, 0f));

            var lightObject = new GameObject("Sun");
            var sun = lightObject.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.intensity = 1.4f;
            sun.color = new Color(1f, 0.95f, 0.86f);
            sun.shadows = LightShadows.Soft;
            lightObject.transform.rotation = Quaternion.Euler(42f, -35f, 0f);

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.30f, 0.34f, 0.42f);

            var ground = CreateBox("Ground", lit, new Color(0.20f, 0.23f, 0.27f));
            ground.localScale = new Vector3(14f, 0.2f, 14f);
            ground.position = new Vector3(0f, -0.1f, 0f);

            _cube = CreateBox("Cube", lit, new Color(0.95f, 0.55f, 0.15f));
            _cube.position = new Vector3(0f, 1.1f, 0f);
        }

        static Transform CreateBox(string name, Material source, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            Destroy(box.GetComponent<BoxCollider>());
            var renderer = box.GetComponent<MeshRenderer>();
            if (source != null)
            {
                var material = new Material(source);
                material.SetColor("_BaseColor", color);
                renderer.sharedMaterial = material;
            }
            return box.transform;
        }

        void Update()
        {
            _cube.Rotate(new Vector3(20f, 45f, 0f) * Time.deltaTime, Space.World);
            if (Time.unscaledDeltaTime > 0f)
                _fps = Mathf.Lerp(_fps, 1f / Time.unscaledDeltaTime, 0.05f);
        }

        void OnGUI()
        {
            _style ??= new GUIStyle(GUI.skin.label) { fontStyle = FontStyle.Bold };
            _style.fontSize = Mathf.Max(18, Screen.height / 22);
            _style.normal.textColor = Color.white;

            float pad = Screen.height * 0.04f;
            var area = new Rect(pad + Screen.safeArea.x, pad, Screen.width - 2f * pad, Screen.height - 2f * pad);
            GUI.Label(area,
                "SKYFORGE  " + Application.version + "\n" +
                Mathf.RoundToInt(_fps) + " FPS   " + Screen.width + "x" + Screen.height + "\n" +
                SystemInfo.deviceModel + "\n" +
                SystemInfo.graphicsDeviceType,
                _style);
        }
    }
}
