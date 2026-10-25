using System.Collections.Generic;
using TerrainEditor.Application;
using TerrainEditor.Domain;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TerrainEditor.Presentation.Editing
{
    /// <summary>Phase 2 height editing UI and brush input. Uses Unity's legacy IMGUI for zero-prefab setup.</summary>
    [DisallowMultipleComponent]
    public sealed class HeightBrushEditor : MonoBehaviour
    {
        private enum BrushMode { Raise, Lower, Smooth, Flatten }
        [SerializeField] private float brushRadiusMeters = 60f;
        [SerializeField, Range(0.01f, 1f)] private float brushStrength = 0.25f;
        [SerializeField] private float flattenTargetNormalized = 0.15f;
        [SerializeField] private int historyLimit = 20;

        private TerrainEditorBootstrapper bootstrapper;
        private BrushMode mode = BrushMode.Raise;
        private bool editingEnabled = true;
        private bool isStrokeActive;
        private float[,] strokeBefore;
        private readonly Stack<float[,]> undoStack = new Stack<float[,]>();
        private readonly Stack<float[,]> redoStack = new Stack<float[,]>();
        private string status = "Ready";
        private Vector3 lastHitPoint;
        private bool hasHit;

        private void Awake()
        {
            bootstrapper = FindObjectOfType<TerrainEditorBootstrapper>();
            if (bootstrapper == null) status = "TerrainEditorBootstrapper not found";
        }

        private void Update()
        {
            if (!editingEnabled || bootstrapper == null || bootstrapper.CurrentProject == null) return;
            var mouse = Mouse.current;
            if (mouse == null) return;

            bool overUi = (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) || IsPointerOverPanel(mouse.position.ReadValue());
            hasHit = false;
            var camera = UnityEngine.Camera.main;
            var terrain = bootstrapper.RenderAdapter != null ? bootstrapper.RenderAdapter.CurrentTerrain : null;
            if (camera != null && terrain != null && !overUi)
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
                isStrokeActive = true;
                strokeBefore = bootstrapper.CurrentProject.Heightmap.CopyHeights();
                ApplyBrush(lastHitPoint, true);
            }
            else if (mouse.leftButton.isPressed && isStrokeActive && hasHit)
            {
                ApplyBrush(lastHitPoint, false);
            }

            if (mouse.leftButton.wasReleasedThisFrame && isStrokeActive)
            {
                isStrokeActive = false;
                if (strokeBefore != null)
                {
                    PushUndo(strokeBefore);
                    strokeBefore = null;
                    redoStack.Clear();
                    status = "Height edited";
                }
            }
        }


        private static bool IsPointerOverPanel(Vector2 screenPosition)
        {
            float guiY = Screen.height - screenPosition.y;
            return new Rect(0f, 0f, 315f, 420f).Contains(new Vector2(screenPosition.x, guiY));
        }

        private void ApplyBrush(Vector3 worldPoint, bool firstSample)
        {
            var project = bootstrapper.CurrentProject;
            var settings = project.Settings;
            var data = project.Heightmap;
            int resolution = data.Resolution;
            float[,] before = data.CopyHeights();
            float[,] next = data.CopyHeights();
            float radiusX = Mathf.Max(0.01f, brushRadiusMeters / settings.WidthMeters * (resolution - 1));
            float radiusZ = Mathf.Max(0.01f, brushRadiusMeters / settings.LengthMeters * (resolution - 1));
            int centerX = Mathf.RoundToInt(Mathf.Clamp01(worldPoint.x / settings.WidthMeters) * (resolution - 1));
            int centerZ = Mathf.RoundToInt(Mathf.Clamp01(worldPoint.z / settings.LengthMeters) * (resolution - 1));
            int minX = Mathf.Max(0, Mathf.FloorToInt(centerX - radiusX));
            int maxX = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerX + radiusX));
            int minZ = Mathf.Max(0, Mathf.FloorToInt(centerZ - radiusZ));
            int maxZ = Mathf.Min(resolution - 1, Mathf.CeilToInt(centerZ + radiusZ));
            float dt = Mathf.Max(Time.deltaTime, 1f / 120f);
            float delta = brushStrength * dt * 0.12f;

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
            {
                float nx = (x - centerX) / radiusX;
                float nz = (z - centerZ) / radiusZ;
                float distance = Mathf.Sqrt(nx * nx + nz * nz);
                if (distance > 1f) continue;
                float falloff = 1f - distance;
                falloff = falloff * falloff * (3f - 2f * falloff);
                float current = before[z, x];
                float value = current;
                switch (mode)
                {
                    case BrushMode.Raise: value += delta * falloff; break;
                    case BrushMode.Lower: value -= delta * falloff; break;
                    case BrushMode.Flatten: value = Mathf.Lerp(current, flattenTargetNormalized, Mathf.Clamp01(brushStrength * falloff * 0.15f)); break;
                    case BrushMode.Smooth:
                        float sum = 0f; int count = 0;
                        for (int dz = -1; dz <= 1; dz++)
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int sx = Mathf.Clamp(x + dx, 0, resolution - 1);
                            int sz = Mathf.Clamp(z + dz, 0, resolution - 1);
                            sum += before[sz, sx]; count++;
                        }
                        value = Mathf.Lerp(current, sum / count, Mathf.Clamp01(brushStrength * falloff * 0.25f));
                        break;
                }
                next[z, x] = Mathf.Clamp01(value);
            }

            for (int z = minZ; z <= maxZ; z++)
            for (int x = minX; x <= maxX; x++)
                data.Set(x, z, next[z, x]);

            bootstrapper.RenderAdapter.ApplyHeights(data.CopyHeights());
        }

        private void PushUndo(float[,] snapshot)
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
            if (bootstrapper == null || bootstrapper.CurrentProject == null || undoStack.Count == 0) { status = "Nothing to undo"; return; }
            redoStack.Push(bootstrapper.CurrentProject.Heightmap.CopyHeights());
            Restore(undoStack.Pop()); status = "Undo";
        }

        private void Redo()
        {
            if (bootstrapper == null || bootstrapper.CurrentProject == null || redoStack.Count == 0) { status = "Nothing to redo"; return; }
            PushUndo(bootstrapper.CurrentProject.Heightmap.CopyHeights());
            Restore(redoStack.Pop()); status = "Redo";
        }

        private void Restore(float[,] snapshot)
        {
            var map = bootstrapper.CurrentProject.Heightmap;
            for (int z = 0; z < map.Resolution; z++)
            for (int x = 0; x < map.Resolution; x++) map.Set(x, z, snapshot[z, x]);
            bootstrapper.RenderAdapter.ApplyHeights(map.CopyHeights());
        }

        private void OnGUI()
        {
            if (bootstrapper == null) return;
            GUI.Box(new Rect(12, 12, 290, 390), "Terrain Editor — Phase 2");
            GUILayout.BeginArea(new Rect(24, 42, 266, 350));
            editingEnabled = GUILayout.Toggle(editingEnabled, "高さ編集を有効にする");
            GUILayout.Space(8);
            GUILayout.Label("ブラシモード");
            if (GUILayout.Button(mode == BrushMode.Raise ? "✓ 盛り上げ" : "盛り上げ")) mode = BrushMode.Raise;
            if (GUILayout.Button(mode == BrushMode.Lower ? "✓ 削る" : "削る")) mode = BrushMode.Lower;
            if (GUILayout.Button(mode == BrushMode.Smooth ? "✓ 滑らかにする" : "滑らかにする")) mode = BrushMode.Smooth;
            if (GUILayout.Button(mode == BrushMode.Flatten ? "✓ 平らにする" : "平らにする")) mode = BrushMode.Flatten;
            GUILayout.Space(8);
            GUILayout.Label($"ブラシ半径: {brushRadiusMeters:0} m");
            brushRadiusMeters = GUILayout.HorizontalSlider(brushRadiusMeters, 5f, 250f);
            GUILayout.Label($"強さ: {brushStrength:0.00}");
            brushStrength = GUILayout.HorizontalSlider(brushStrength, 0.01f, 1f);
            GUILayout.Label($"平坦化目標: {flattenTargetNormalized * bootstrapper.CurrentProject.Settings.HeightMeters:0.0} m");
            flattenTargetNormalized = GUILayout.HorizontalSlider(flattenTargetNormalized, 0f, 1f);
            GUILayout.Space(8);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Undo")) Undo();
            if (GUILayout.Button("Redo")) Redo();
            GUILayout.EndHorizontal();
            GUILayout.Label("左ドラッグ: 編集 / 右ドラッグ: 視点回転");
            GUILayout.Label("中ドラッグ: 移動 / ホイール: ズーム");
            GUILayout.Space(5);
            GUILayout.Label("状態: " + status);
            if (hasHit) GUILayout.Label($"位置: X {lastHitPoint.x:0}, Z {lastHitPoint.z:0}");
            GUILayout.EndArea();
        }
    }
}
