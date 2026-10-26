using System.Collections.Generic;
using TerrainEditor.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TerrainEditor.Presentation.Editing
{
    /// <summary>
    /// Runtime-generated terrain surface layers and a brush UI for painting them.
    /// The generated layers are runtime objects; persistent asset export is planned for a later phase.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SurfacePaintEditor : MonoBehaviour
    {
        private const int LayerCount = 5;
        private enum SurfaceKind { Grassland, Forest, Rock, Sand, Snow }

        [SerializeField] private float brushRadiusMeters = 55f;
        [SerializeField, Range(0.01f, 1f)] private float brushStrength = 0.65f;
        [SerializeField] private int historyLimit = 12;

        private TerrainEditorBootstrapper bootstrapper;
        private HeightBrushEditor heightEditor;
        private bool paintMode;
        private SurfaceKind selectedSurface = SurfaceKind.Grassland;
        private bool strokeActive;
        private float[,,] strokeBefore;
        private readonly Stack<float[,,]> undoStack = new Stack<float[,,]>();
        private readonly Stack<float[,,]> redoStack = new Stack<float[,,]>();
        private TerrainData configuredData;
        private TerrainLayer[] generatedLayers;
        private Texture2D[] generatedTextures;
        private string status = "地表ペイント待機中";
        private Vector3 lastHitPoint;
        private bool hasHit;

        private static readonly string[] SurfaceNames = { "草原", "森林", "岩肌", "砂浜", "雪" };

        private void Awake()
        {
            bootstrapper = FindObjectOfType<TerrainEditorBootstrapper>();
            heightEditor = FindObjectOfType<HeightBrushEditor>();
        }

        private void Update()
        {
            if (bootstrapper == null) bootstrapper = FindObjectOfType<TerrainEditorBootstrapper>();
            if (heightEditor == null) heightEditor = FindObjectOfType<HeightBrushEditor>();
            var terrain = bootstrapper != null && bootstrapper.RenderAdapter != null ? bootstrapper.RenderAdapter.CurrentTerrain : null;
            if (terrain == null || bootstrapper.CurrentProject == null) return;

            if (configuredData != terrain.terrainData)
                ConfigureLayers(terrain.terrainData);

            if (heightEditor != null) heightEditor.InputSuspended = paintMode;
            if (!paintMode) return;

            var mouse = Mouse.current;
            if (mouse == null) return;
            bool overUi = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || IsPointerOverPanel(mouse.position.ReadValue());
            hasHit = false;
            var camera = UnityEngine.Camera.main;
            if (camera != null && !overUi)
            {
                Ray ray = camera.ScreenPointToRay(mouse.position.ReadValue());
                if (Physics.Raycast(ray, out var hit, 1000000f) && hit.collider.GetComponent<Terrain>() == terrain)
                {
                    lastHitPoint = hit.point;
                    hasHit = true;
                }
            }

            if (mouse.leftButton.wasPressedThisFrame && !overUi && hasHit)
            {
                strokeActive = true;
                strokeBefore = CopyAlphamaps(terrain.terrainData);
                PaintAt(lastHitPoint);
            }
            else if (mouse.leftButton.isPressed && strokeActive && hasHit)
            {
                PaintAt(lastHitPoint);
            }

            if (mouse.leftButton.wasReleasedThisFrame && strokeActive)
            {
                strokeActive = false;
                if (strokeBefore != null)
                {
                    PushUndo(strokeBefore);
                    strokeBefore = null;
                    redoStack.Clear();
                    status = SurfaceNames[(int)selectedSurface] + "を塗りました";
                }
            }
        }

        private static bool IsPointerOverPanel(Vector2 screenPosition)
        {
            float guiY = Screen.height - screenPosition.y;
            return new Rect(318f, 12f, 292f, 360f).Contains(new Vector2(screenPosition.x, guiY));
        }

        private void ConfigureLayers(TerrainData data)
        {
            if (data == null) return;
            try
            {
                data.alphamapResolution = Mathf.Clamp(data.alphamapResolution, 16, 2048);
                generatedLayers = new TerrainLayer[LayerCount];
                generatedTextures = new Texture2D[LayerCount];
                Color[] baseColors = {
                    new Color(0.34f, 0.48f, 0.22f),
                    new Color(0.16f, 0.30f, 0.13f),
                    new Color(0.43f, 0.42f, 0.39f),
                    new Color(0.76f, 0.66f, 0.43f),
                    new Color(0.88f, 0.91f, 0.94f)
                };
                string[] layerNames = { "Grassland", "Forest", "Rock", "Sand", "Snow" };
                for (int i = 0; i < LayerCount; i++)
                {
                    var texture = CreatePatternTexture(baseColors[i], i);
                    generatedTextures[i] = texture;
                    generatedLayers[i] = new TerrainLayer
                    {
                        name = layerNames[i],
                        diffuseTexture = texture,
                        tileSize = new Vector2(18f, 18f),
                        tileOffset = Vector2.zero
                    };
                }
                data.terrainLayers = generatedLayers;
                int resolution = data.alphamapResolution;
                var maps = new float[resolution, resolution, LayerCount];
                for (int z = 0; z < resolution; z++)
                for (int x = 0; x < resolution; x++) maps[z, x, 0] = 1f;
                data.SetAlphamaps(0, 0, maps);
                configuredData = data;
                undoStack.Clear();
                redoStack.Clear();
                status = "地表レイヤーを準備しました";
            }
            catch (System.Exception ex)
            {
                status = "地表レイヤー準備エラー: " + ex.Message;
                Debug.LogError(status, this);
            }
        }

        private static Texture2D CreatePatternTexture(Color baseColor, int seed)
        {
            const int size = 64;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true);
            texture.name = "RuntimeSurfacePattern_" + seed;
            texture.wrapMode = TextureWrapMode.Repeat;
            texture.filterMode = FilterMode.Bilinear;
            var pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float wave = Mathf.Sin((x * 0.37f + y * 0.19f + seed * 11f)) * 0.035f;
                float grain = Mathf.PerlinNoise((x + seed * 37) * 0.13f, (y + seed * 17) * 0.13f) * 0.13f - 0.065f;
                float variation = wave + grain;
                pixels[y * size + x] = new Color(
                    Mathf.Clamp01(baseColor.r + variation),
                    Mathf.Clamp01(baseColor.g + variation),
                    Mathf.Clamp01(baseColor.b + variation), 1f);
            }
            texture.SetPixels(pixels);
            texture.Apply(true, false);
            return texture;
        }

        private void PaintAt(Vector3 worldPoint)
        {
            var terrain = bootstrapper.RenderAdapter.CurrentTerrain;
            var data = terrain.terrainData;
            int resolution = data.alphamapResolution;
            int layerIndex = (int)selectedSurface;
            Vector3 local = worldPoint - terrain.transform.position;
            int cx = Mathf.RoundToInt(Mathf.Clamp01(local.x / data.size.x) * (resolution - 1));
            int cz = Mathf.RoundToInt(Mathf.Clamp01(local.z / data.size.z) * (resolution - 1));
            float rx = Mathf.Max(1f, brushRadiusMeters / data.size.x * resolution);
            float rz = Mathf.Max(1f, brushRadiusMeters / data.size.z * resolution);
            int minX = Mathf.Max(0, Mathf.FloorToInt(cx - rx));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(cx + rx));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(cz - rz));
            int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt(cz + rz));
            var maps = data.GetAlphamaps(minX, minZ, maxX - minX + 1, maxZ - minZ + 1);
            float[,] original = new float[maps.GetLength(0), maps.GetLength(1)];
            for (int z = 0; z < maps.GetLength(0); z++)
            for (int x = 0; x < maps.GetLength(1); x++) original[z, x] = maps[z, x, layerIndex];

            for (int z = 0; z < maps.GetLength(0); z++)
            for (int x = 0; x < maps.GetLength(1); x++)
            {
                float nx = (minX + x - cx) / rx;
                float nz = (minZ + z - cz) / rz;
                float distance = Mathf.Sqrt(nx * nx + nz * nz);
                if (distance > 1f) continue;
                float falloff = 1f - distance;
                falloff = falloff * falloff * (3f - 2f * falloff);
                float blend = Mathf.Clamp01(brushStrength * falloff * 0.22f);
                float target = Mathf.Lerp(original[z, x], 1f, blend);
                float remaining = 1f - target;
                float oldOtherSum = 0f;
                for (int l = 0; l < LayerCount; l++) if (l != layerIndex) oldOtherSum += maps[z, x, l];
                maps[z, x, layerIndex] = target;
                if (oldOtherSum > 0.0001f)
                {
                    for (int l = 0; l < LayerCount; l++)
                        if (l != layerIndex) maps[z, x, l] = maps[z, x, l] / oldOtherSum * remaining;
                }
                else
                {
                    for (int l = 0; l < LayerCount; l++) if (l != layerIndex) maps[z, x, l] = 0f;
                }
            }
            data.SetAlphamaps(minX, minZ, maps);
        }

        private static float[,,] CopyAlphamaps(TerrainData data) => data.GetAlphamaps(0, 0, data.alphamapWidth, data.alphamapHeight);

        private void PushUndo(float[,,] snapshot)
        {
            undoStack.Push(snapshot);
            while (undoStack.Count > Mathf.Max(1, historyLimit))
            {
                var items = undoStack.ToArray();
                undoStack.Clear();
                for (int i = items.Length - 2; i >= 0; i--) undoStack.Push(items[i]);
            }
        }

        private void Undo()
        {
            var terrain = bootstrapper != null && bootstrapper.RenderAdapter != null ? bootstrapper.RenderAdapter.CurrentTerrain : null;
            if (terrain == null || undoStack.Count == 0) { status = "Undoできる履歴がありません"; return; }
            redoStack.Push(CopyAlphamaps(terrain.terrainData));
            terrain.terrainData.SetAlphamaps(0, 0, undoStack.Pop());
            status = "地表ペイントをUndoしました";
        }

        private void Redo()
        {
            var terrain = bootstrapper != null && bootstrapper.RenderAdapter != null ? bootstrapper.RenderAdapter.CurrentTerrain : null;
            if (terrain == null || redoStack.Count == 0) { status = "Redoできる履歴がありません"; return; }
            PushUndo(CopyAlphamaps(terrain.terrainData));
            terrain.terrainData.SetAlphamaps(0, 0, redoStack.Pop());
            status = "地表ペイントをRedoしました";
        }

        private void OnGUI()
        {
            if (bootstrapper == null) return;
            GUI.Box(new Rect(318, 12, 292, 330), "Terrain Editor — Phase 3");
            GUILayout.BeginArea(new Rect(330, 42, 268, 290));
            bool newMode = GUILayout.Toggle(paintMode, "地表ペイントモード");
            if (newMode != paintMode)
            {
                paintMode = newMode;
                if (heightEditor != null) heightEditor.InputSuspended = paintMode;
                status = paintMode ? "地表ペイント中：左ドラッグで塗る" : "高さ編集モードに戻りました";
            }
            GUILayout.Space(6);
            GUILayout.Label("地表タイプ");
            for (int i = 0; i < SurfaceNames.Length; i++)
            {
                var kind = (SurfaceKind)i;
                if (GUILayout.Button(selectedSurface == kind ? "✓ " + SurfaceNames[i] : SurfaceNames[i])) selectedSurface = kind;
            }
            GUILayout.Space(5);
            GUILayout.Label($"ブラシ半径: {brushRadiusMeters:0} m");
            brushRadiusMeters = GUILayout.HorizontalSlider(brushRadiusMeters, 5f, 250f);
            GUILayout.Label($"塗り強さ: {brushStrength:0.00}");
            brushStrength = GUILayout.HorizontalSlider(brushStrength, 0.05f, 1f);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Undo")) Undo();
            if (GUILayout.Button("Redo")) Redo();
            GUILayout.EndHorizontal();
            GUILayout.Label("左ドラッグ: 地表を塗る");
            GUILayout.Label("状態: " + status);
            if (hasHit) GUILayout.Label($"位置: X {lastHitPoint.x:0}, Z {lastHitPoint.z:0}");
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            if (generatedLayers != null)
                foreach (var layer in generatedLayers) if (layer != null) Destroy(layer);
            if (generatedTextures != null)
                foreach (var texture in generatedTextures) if (texture != null) Destroy(texture);
        }
    }
}
