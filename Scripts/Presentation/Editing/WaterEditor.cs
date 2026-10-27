using System;
using System.Collections.Generic;
using TerrainEditor.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TerrainEditor.Presentation.Editing
{
    /// <summary>
    /// Runtime water editor: sea plane, circular lake meshes, and river ribbon meshes.
    /// Water bodies are intentionally independent from TerrainData.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WaterEditor : MonoBehaviour
    {
        private enum WaterMode { Sea, Lake, River }

        [SerializeField] private float waterLevel = 12f;
        [SerializeField] private float lakeRadius = 45f;
        [SerializeField] private float riverWidth = 18f;
        [SerializeField, Range(0.05f, 0.95f)] private float opacity = 0.62f;
        [SerializeField] private int riverSamplesMin = 2;
        [SerializeField] private int historyLimit = 20;

        private TerrainEditorBootstrapper bootstrapper;
        private HeightBrushEditor heightEditor;
        private SurfacePaintEditor surfaceEditor;
        private WaterMode mode = WaterMode.Lake;
        private bool waterModeEnabled;
        private bool drawingRiver;
        private readonly List<Vector3> riverPoints = new List<Vector3>();
        private readonly List<GameObject> waterObjects = new List<GameObject>();
        private readonly Stack<List<WaterRecord>> undoStack = new Stack<List<WaterRecord>>();
        private readonly Stack<List<WaterRecord>> redoStack = new Stack<List<WaterRecord>>();
        private Material waterMaterial;
        private string status = "水域編集待機中";
        private Vector3 lastHitPoint;
        private bool hasHit;
        private const string WaterRootName = "__TerrainEditorWater";

        private sealed class WaterRecord
        {
            public string name;
            public WaterMode kind;
            public Vector3[] points;
            public float level;
            public float size;
        }

        private void Awake()
        {
            bootstrapper = FindObjectOfType<TerrainEditorBootstrapper>();
            heightEditor = FindObjectOfType<HeightBrushEditor>();
            surfaceEditor = FindObjectOfType<SurfacePaintEditor>();
            CreateMaterial();
        }

        private void Update()
        {
            if (bootstrapper == null) bootstrapper = FindObjectOfType<TerrainEditorBootstrapper>();
            if (heightEditor == null) heightEditor = FindObjectOfType<HeightBrushEditor>();
            if (surfaceEditor == null) surfaceEditor = FindObjectOfType<SurfacePaintEditor>();

            if (heightEditor != null) heightEditor.InputSuspended = waterModeEnabled;
            if (surfaceEditor != null) surfaceEditor.InputSuspended = waterModeEnabled;
            if (!waterModeEnabled || bootstrapper == null || bootstrapper.CurrentProject == null) return;

            var terrain = bootstrapper.RenderAdapter != null ? bootstrapper.RenderAdapter.CurrentTerrain : null;
            var mouse = Mouse.current;
            var camera = UnityEngine.Camera.main;
            if (terrain == null || mouse == null || camera == null) return;

            Vector2 mousePosition = mouse.position.ReadValue();
            bool overUi = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
                || IsPointerOverPanel(mousePosition);
            hasHit = false;
            if (!overUi)
            {
                Ray ray = camera.ScreenPointToRay(mousePosition);
                if (Physics.Raycast(ray, out var hit, 1000000f) && hit.collider.GetComponent<Terrain>() == terrain)
                {
                    lastHitPoint = hit.point;
                    hasHit = true;
                }
            }

            if (mode == WaterMode.Lake && mouse.leftButton.wasPressedThisFrame && !overUi && hasHit)
            {
                SaveUndo();
                CreateLake(lastHitPoint, lakeRadius, waterLevel);
                redoStack.Clear();
                status = "湖を作成しました";
            }
            else if (mode == WaterMode.River)
            {
                if (mouse.leftButton.wasPressedThisFrame && !overUi && hasHit)
                {
                    drawingRiver = true;
                    riverPoints.Clear();
                    riverPoints.Add(WithLevel(lastHitPoint, waterLevel));
                }
                else if (mouse.leftButton.isPressed && drawingRiver && hasHit)
                {
                    Vector3 p = WithLevel(lastHitPoint, waterLevel);
                    if (riverPoints.Count == 0 || Vector3.Distance(riverPoints[riverPoints.Count - 1], p) >= Mathf.Max(2f, riverWidth * 0.2f))
                        riverPoints.Add(p);
                }
                if (mouse.leftButton.wasReleasedThisFrame && drawingRiver)
                {
                    drawingRiver = false;
                    if (riverPoints.Count >= riverSamplesMin)
                    {
                        SaveUndo();
                        CreateRiver(riverPoints.ToArray(), riverWidth, waterLevel);
                        redoStack.Clear();
                        status = "川を作成しました";
                    }
                    else status = "川を作成するには、地形上を長めにドラッグしてください";
                    riverPoints.Clear();
                }
            }
        }

        private static Vector3 WithLevel(Vector3 p, float level) => new Vector3(p.x, level, p.z);

        private static bool IsPointerOverPanel(Vector2 screenPosition)
        {
            float guiY = Screen.height - screenPosition.y;
            return new Rect(Screen.width - 316f, 0f, 316f, 470f).Contains(new Vector2(screenPosition.x, guiY));
        }

        private void CreateMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null) shader = Shader.Find("Unlit/Color");
            if (shader == null) shader = Shader.Find("Standard");
            waterMaterial = new Material(shader);
            ApplyMaterialOpacity();
        }

        private void ApplyMaterialOpacity()
        {
            if (waterMaterial == null) return;
            Color c = new Color(0.12f, 0.48f, 0.78f, opacity);
            if (waterMaterial.HasProperty("_BaseColor")) waterMaterial.SetColor("_BaseColor", c);
            if (waterMaterial.HasProperty("_Color")) waterMaterial.SetColor("_Color", c);
            // Configure common built-in/URP shaders for alpha blending.
            waterMaterial.SetOverrideTag("RenderType", "Transparent");
            if (waterMaterial.HasProperty("_Surface")) waterMaterial.SetFloat("_Surface", 1f);
            if (waterMaterial.HasProperty("_Mode")) waterMaterial.SetFloat("_Mode", 3f);
            if (waterMaterial.HasProperty("_Blend")) waterMaterial.SetFloat("_Blend", 0f);
            waterMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            waterMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            waterMaterial.SetInt("_ZWrite", 0);
            waterMaterial.DisableKeyword("_ALPHATEST_ON");
            waterMaterial.EnableKeyword("_ALPHABLEND_ON");
            waterMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            waterMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        }

        private GameObject CreateWaterObject(string objectName, Mesh mesh)
        {
            var go = new GameObject(objectName);
            go.transform.position = Vector3.zero;
            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = waterMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            waterObjects.Add(go);
            return go;
        }

        private void CreateSea()
        {
            var settings = bootstrapper.CurrentProject.Settings;
            SaveUndo();
            RemoveSeaObjects();
            var mesh = new Mesh { name = "SeaSurfaceMesh" };
            mesh.vertices = new[] {
                new Vector3(0f, waterLevel, 0f),
                new Vector3(settings.WidthMeters, waterLevel, 0f),
                new Vector3(0f, waterLevel, settings.LengthMeters),
                new Vector3(settings.WidthMeters, waterLevel, settings.LengthMeters)
            };
            mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
            mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
            mesh.RecalculateNormals();
            CreateWaterObject("Sea_Surface", mesh);
            redoStack.Clear();
            status = "海面を作成・更新しました";
        }

        private void RemoveSeaObjects()
        {
            for (int i = waterObjects.Count - 1; i >= 0; i--)
            {
                if (waterObjects[i] != null && waterObjects[i].name == "Sea_Surface")
                {
                    Destroy(waterObjects[i].GetComponent<MeshFilter>().sharedMesh);
                    Destroy(waterObjects[i]);
                    waterObjects.RemoveAt(i);
                }
            }
        }

        private void CreateLake(Vector3 center, float radius, float level)
        {
            int segments = 48;
            var vertices = new Vector3[segments + 1];
            var uv = new Vector2[vertices.Length];
            vertices[0] = new Vector3(center.x, level, center.z);
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i < segments; i++)
            {
                float angle = i * Mathf.PI * 2f / segments;
                float x = center.x + Mathf.Cos(angle) * radius;
                float z = center.z + Mathf.Sin(angle) * radius;
                vertices[i + 1] = new Vector3(x, level, z);
                uv[i + 1] = new Vector2(0.5f + Mathf.Cos(angle) * 0.5f, 0.5f + Mathf.Sin(angle) * 0.5f);
            }
            var triangles = new int[segments * 3];
            for (int i = 0; i < segments; i++)
            {
                int a = i + 1, b = ((i + 1) % segments) + 1;
                triangles[i * 3] = 0; triangles[i * 3 + 1] = b; triangles[i * 3 + 2] = a;
            }
            var mesh = new Mesh { name = "LakeMesh" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals();
            var go = CreateWaterObject("Lake_" + waterObjects.Count, mesh);
            go.transform.position = Vector3.zero;
        }

        private void CreateRiver(Vector3[] points, float width, float level)
        {
            if (points == null || points.Length < 2) return;
            var vertices = new Vector3[points.Length * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(points.Length - 1) * 6];
            float distance = 0f;
            for (int i = 0; i < points.Length; i++)
            {
                Vector3 tangent;
                if (i == 0) tangent = (points[1] - points[0]).normalized;
                else if (i == points.Length - 1) tangent = (points[i] - points[i - 1]).normalized;
                else tangent = (points[i + 1] - points[i - 1]).normalized;
                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x).normalized * (width * 0.5f);
                vertices[i * 2] = new Vector3(points[i].x + side.x, level, points[i].z + side.z);
                vertices[i * 2 + 1] = new Vector3(points[i].x - side.x, level, points[i].z - side.z);
                if (i > 0) distance += Vector3.Distance(points[i - 1], points[i]);
                uv[i * 2] = new Vector2(0f, distance / Mathf.Max(1f, width));
                uv[i * 2 + 1] = new Vector2(1f, distance / Mathf.Max(1f, width));
                if (i < points.Length - 1)
                {
                    int t = i * 6, v = i * 2;
                    triangles[t] = v; triangles[t + 1] = v + 2; triangles[t + 2] = v + 1;
                    triangles[t + 3] = v + 1; triangles[t + 4] = v + 2; triangles[t + 5] = v + 3;
                }
            }
            var mesh = new Mesh { name = "RiverRibbonMesh" };
            mesh.vertices = vertices; mesh.uv = uv; mesh.triangles = triangles; mesh.RecalculateNormals();
            CreateWaterObject("River_" + waterObjects.Count, mesh);
        }

        private List<WaterRecord> CaptureRecords()
        {
            var records = new List<WaterRecord>();
            foreach (var go in waterObjects)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf == null || mf.sharedMesh == null) continue;
                // Preserve mesh vertices and infer the primitive from its object name.
                var mesh = mf.sharedMesh;
                WaterMode kind = go.name.StartsWith("Sea_") ? WaterMode.Sea :
                    go.name.StartsWith("River_") ? WaterMode.River : WaterMode.Lake;
                records.Add(new WaterRecord {
                    name = go.name, kind = kind, points = mesh.vertices, level = waterLevel,
                    size = kind == WaterMode.River ? riverWidth : lakeRadius
                });
            }
            return records;
        }

        private void SaveUndo()
        {
            undoStack.Push(CaptureRecords());
            while (undoStack.Count > historyLimit)
            {
                // Stack bottom pruning is unnecessary for this small runtime-only history.
                var temp = undoStack.ToArray();
                undoStack.Clear();
                for (int i = temp.Length - 2; i >= 0; i--) undoStack.Push(temp[i]);
            }
        }

        private void Undo()
        {
            if (undoStack.Count == 0) { status = "Undoできる操作がありません"; return; }
            redoStack.Push(CaptureRecords());
            RestoreRecords(undoStack.Pop());
            status = "水域操作をUndoしました";
        }

        private void Redo()
        {
            if (redoStack.Count == 0) { status = "Redoできる操作がありません"; return; }
            undoStack.Push(CaptureRecords());
            RestoreRecords(redoStack.Pop());
            status = "水域操作をRedoしました";
        }

        private void RestoreRecords(List<WaterRecord> records)
        {
            ClearAllWaterObjects();
            foreach (var record in records)
            {
                if (record.kind == WaterMode.Sea)
                {
                    var settings = bootstrapper.CurrentProject.Settings;
                    var mesh = new Mesh { name = "SeaSurfaceMesh" };
                    mesh.vertices = record.points;
                    mesh.uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one };
                    mesh.triangles = new[] { 0, 2, 1, 1, 2, 3 };
                    mesh.RecalculateNormals();
                    CreateWaterObject("Sea_Surface", mesh);
                }
                else
                {
                    var mesh = new Mesh { name = record.kind == WaterMode.River ? "RiverMesh" : "LakeMesh" };
                    mesh.vertices = record.points;
                    mesh.uv = new Vector2[record.points.Length];
                    // Rebuild topology based on primitive mesh vertex pattern.
                    if (record.kind == WaterMode.Lake)
                    {
                        int segments = record.points.Length - 1;
                        var tris = new int[segments * 3];
                        for (int i = 0; i < segments; i++) { tris[i * 3] = 0; tris[i * 3 + 1] = ((i + 1) % segments) + 1; tris[i * 3 + 2] = i + 1; }
                        mesh.triangles = tris;
                    }
                    else
                    {
                        int count = record.points.Length / 2;
                        var tris = new int[(count - 1) * 6];
                        for (int i = 0; i < count - 1; i++)
                        {
                            int v = i * 2, t = i * 6;
                            tris[t] = v; tris[t + 1] = v + 2; tris[t + 2] = v + 1;
                            tris[t + 3] = v + 1; tris[t + 4] = v + 2; tris[t + 5] = v + 3;
                        }
                        mesh.triangles = tris;
                    }
                    mesh.RecalculateNormals();
                    CreateWaterObject(record.name, mesh);
                }
            }
        }

        private void ClearAllWaterObjects()
        {
            foreach (var go in waterObjects)
            {
                if (go == null) continue;
                var mf = go.GetComponent<MeshFilter>();
                if (mf != null && mf.sharedMesh != null) Destroy(mf.sharedMesh);
                Destroy(go);
            }
            waterObjects.Clear();
        }

        private void OnGUI()
        {
            if (bootstrapper == null || bootstrapper.CurrentProject == null) return;
            float x = Screen.width - 310f;
            GUI.Box(new Rect(x, 12f, 292f, 450f), "Terrain Editor — Phase 4");
            GUILayout.BeginArea(new Rect(x + 12f, 42f, 268f, 410f));
            bool enabled = GUILayout.Toggle(waterModeEnabled, "水域編集モード");
            if (enabled != waterModeEnabled)
            {
                waterModeEnabled = enabled;
                status = waterModeEnabled ? "水域編集モード：操作を選択してください" : "水域編集モードを終了しました";
            }
            GUILayout.Space(5f);
            GUILayout.Label("水域タイプ");
            GUILayout.BeginHorizontal();
            if (GUILayout.Toggle(mode == WaterMode.Sea, "海", GUI.skin.button)) mode = WaterMode.Sea;
            if (GUILayout.Toggle(mode == WaterMode.Lake, "湖", GUI.skin.button)) mode = WaterMode.Lake;
            if (GUILayout.Toggle(mode == WaterMode.River, "川", GUI.skin.button)) mode = WaterMode.River;
            GUILayout.EndHorizontal();
            GUILayout.Label($"水面の高さ: {waterLevel:0.0} m");
            waterLevel = GUILayout.HorizontalSlider(waterLevel, 0f, Mathf.Max(1f, bootstrapper.CurrentProject.Settings.HeightMeters));
            GUILayout.Label($"湖の半径: {lakeRadius:0} m");
            lakeRadius = GUILayout.HorizontalSlider(lakeRadius, 5f, 200f);
            GUILayout.Label($"川幅: {riverWidth:0} m");
            riverWidth = GUILayout.HorizontalSlider(riverWidth, 3f, 80f);
            GUILayout.Label($"透明度: {opacity:0.00}");
            float newOpacity = GUILayout.HorizontalSlider(opacity, 0.05f, 0.95f);
            if (!Mathf.Approximately(newOpacity, opacity)) { opacity = newOpacity; ApplyMaterialOpacity(); }
            GUILayout.Space(5f);
            if (mode == WaterMode.Sea && GUILayout.Button("海を作成 / 更新")) CreateSea();
            if (GUILayout.Button("Undo")) Undo();
            if (GUILayout.Button("Redo")) Redo();
            GUILayout.Space(4f);
            if (mode == WaterMode.Sea) GUILayout.Label("海：ボタンで地形全体に水面を作成");
            else if (mode == WaterMode.Lake) GUILayout.Label("湖：地形上をクリックして配置");
            else GUILayout.Label("川：地形上を左ドラッグして描画");
            GUILayout.Label("状態: " + status);
            GUILayout.EndArea();
        }

        private void OnDestroy()
        {
            ClearAllWaterObjects();
            if (waterMaterial != null) Destroy(waterMaterial);
        }
    }
}
