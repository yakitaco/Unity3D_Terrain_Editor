using TerrainEditor.Domain;
using TerrainEditor.Application;
using TerrainEditor.Infrastructure.Rendering;
using TerrainEditor.Presentation.Camera;
using TerrainEditor.Presentation.Editing;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TerrainEditor.Application
{
    public sealed class TerrainEditorBootstrapper : MonoBehaviour
    {
        [SerializeField] private bool createTerrainOnStart = true;
        [SerializeField] private TerrainSettings initialSettings = new TerrainSettings();

        private TerrainRenderAdapter renderAdapter;
        private TerrainProject currentProject;
        private EditorCameraController cameraController;

        public TerrainProject CurrentProject => currentProject;
        public TerrainRenderAdapter RenderAdapter => renderAdapter;

        private void Awake()
        {
            renderAdapter = new TerrainRenderAdapter();
            EnsureSceneBasics();
        }

        private void Start()
        {
            if (createTerrainOnStart)
                CreateTerrain(initialSettings);
        }

        public bool CreateTerrain(TerrainSettings settings)
        {
            if (!TerrainProjectFactory.TryCreate(settings, out var newProject, out var validationError))
            {
                Debug.LogError(validationError, this);
                return false;
            }

            if (!renderAdapter.TryBuild(newProject, null, out var renderError))
            {
                Debug.LogError(renderError, this);
                return false;
            }

            currentProject = newProject;
            var terrainSettings = currentProject.Settings;
            cameraController.SetTerrainBounds(
                new Vector3(terrainSettings.WidthMeters * 0.5f, 0f, terrainSettings.LengthMeters * 0.5f),
                terrainSettings.WidthMeters,
                terrainSettings.LengthMeters,
                terrainSettings.HeightMeters);

            Debug.Log($"Terrain created: {terrainSettings.ProjectName} ({terrainSettings.WidthMeters} x {terrainSettings.LengthMeters} x {terrainSettings.HeightMeters} m)", this);
            return true;
        }

        private void EnsureSceneBasics()
        {
            var camera = UnityEngine.Camera.main;
            if (camera == null)
            {
                var cameraObject = new GameObject("Main Camera");
                cameraObject.tag = "MainCamera";
                camera = cameraObject.AddComponent<UnityEngine.Camera>();
                cameraObject.transform.position = new Vector3(1200f, 1000f, -1200f);
                cameraObject.transform.rotation = Quaternion.Euler(35f, 45f, 0f);
            }

            cameraController = camera.GetComponent<EditorCameraController>();
            if (cameraController == null)
                cameraController = camera.gameObject.AddComponent<EditorCameraController>();

            if (FindObjectOfType<Light>() == null)
            {
                var lightObject = new GameObject("Directional Light");
                var light = lightObject.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightObject.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            if (FindObjectOfType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                var eventObject = new GameObject("EventSystem");
                eventObject.AddComponent<UnityEngine.EventSystems.EventSystem>();
                // InputSystemUIInputModule should be added when a UI Canvas is introduced.
            }

            if (GetComponent<HeightBrushEditor>() == null)
                gameObject.AddComponent<HeightBrushEditor>();

            if (GetComponent<SurfacePaintEditor>() == null)
                gameObject.AddComponent<SurfacePaintEditor>();
        }

        private void OnDestroy()
        {
            if (renderAdapter != null)
                renderAdapter.Clear();
        }
    }
}
