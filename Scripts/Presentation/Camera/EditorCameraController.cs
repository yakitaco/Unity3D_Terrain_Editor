using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;

namespace TerrainEditor.Presentation.Camera
{
    [DisallowMultipleComponent]
    public sealed class EditorCameraController : MonoBehaviour
    {
        [SerializeField] private float rotateSensitivity = 0.25f;
        [SerializeField] private float panSensitivity = 1f;
        [SerializeField] private float zoomSensitivity = 0.12f;
        [SerializeField] private float minPitch = 5f;
        [SerializeField] private float maxPitch = 85f;

        private Vector3 pivot;
        private float distance = 1400f;
        private float yaw = 45f;
        private float pitch = 45f;
        private Vector3 initialPivot;
        private float initialDistance;
        private float initialYaw;
        private float initialPitch;
        private bool initialized;
        private bool gridVisible = true;
        private GameObject gridObject;
        private float terrainWidth = 1000f;
        private float terrainLength = 1000f;
        private float terrainHeight = 300f;

        private void Start()
        {
            if (!initialized)
                ResetView();
        }

        private void LateUpdate()
        {
            var mouse = Mouse.current;
            var keyboard = Keyboard.current;
            if (mouse == null)
                return;

            bool pointerOverUi = EventSystem.current != null &&
                                 EventSystem.current.IsPointerOverGameObject();

            if (!pointerOverUi)
            {
                if (mouse.rightButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    yaw += delta.x * rotateSensitivity;
                    pitch -= delta.y * rotateSensitivity;
                    pitch = Mathf.Clamp(pitch, minPitch, maxPitch);
                }

                if (mouse.middleButton.isPressed)
                {
                    Vector2 delta = mouse.delta.ReadValue();
                    float scale = distance * 0.0015f * panSensitivity;
                    Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
                    Vector3 right = rotation * Vector3.right;
                    Vector3 up = rotation * Vector3.up;
                    pivot += (-right * delta.x + up * delta.y) * scale;
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                {
                    distance *= Mathf.Exp(-scroll * zoomSensitivity * 0.01f);
                    distance = Mathf.Clamp(distance, 1f, 1000000f);
                }
            }

            if (keyboard != null && !IsTextInputFocused())
            {
                if (keyboard.fKey.wasPressedThisFrame) FrameTerrain();
                if (keyboard.homeKey.wasPressedThisFrame) ResetView();
                if (keyboard.digit1Key.wasPressedThisFrame) SetTopView();
                if (keyboard.digit2Key.wasPressedThisFrame) SetDefaultView();
                if (keyboard.digit3Key.wasPressedThisFrame) SetSideView();
                if (keyboard.gKey.wasPressedThisFrame) ToggleGrid();
            }

            ApplyCameraTransform();
        }

        public void SetTerrainBounds(Vector3 center, float width, float length, float height)
        {
            pivot = center;
            terrainWidth = Mathf.Max(1f, width);
            terrainLength = Mathf.Max(1f, length);
            terrainHeight = Mathf.Max(1f, height);
            FrameTerrain();
            SaveInitialView();
        }

        public void FrameTerrain()
        {
            float verticalFov = UnityEngine.Camera.main != null
                ? UnityEngine.Camera.main.fieldOfView
                : 60f;
            float halfFovRadians = verticalFov * Mathf.Deg2Rad * 0.5f;
            float halfDiagonal = 0.5f * Mathf.Sqrt(
                terrainWidth * terrainWidth +
                terrainLength * terrainLength +
                terrainHeight * terrainHeight);

            // Add a safety margin so the terrain edges remain inside the view.
            distance = Mathf.Max(10f, halfDiagonal / Mathf.Sin(halfFovRadians) * 1.15f);
            distance = Mathf.Clamp(distance, 1f, Mathf.Max(100f, Mathf.Max(terrainWidth, terrainLength, terrainHeight) * 10f));
            ApplyCameraTransform();
        }

        public void ResetView()
        {
            yaw = 45f;
            pitch = 45f;
            pivot = new Vector3(terrainWidth * 0.5f, 0f, terrainLength * 0.5f);
            yaw = 45f;
            pitch = 45f;
            FrameTerrain();
            SaveInitialView();
            ApplyCameraTransform();
        }

        public void SetTopView()
        {
            pitch = 85f;
            ApplyCameraTransform();
        }

        public void SetDefaultView()
        {
            yaw = 45f;
            pitch = 45f;
            ApplyCameraTransform();
        }

        public void SetSideView()
        {
            yaw = 90f;
            pitch = 15f;
            ApplyCameraTransform();
        }

        public void ToggleGrid()
        {
            gridVisible = !gridVisible;
            if (gridObject != null)
                gridObject.SetActive(gridVisible);
        }

        public void SetGridObject(GameObject grid)
        {
            gridObject = grid;
            if (gridObject != null)
                gridObject.SetActive(gridVisible);
        }

        private void SaveInitialView()
        {
            initialPivot = pivot;
            initialDistance = distance;
            initialYaw = yaw;
            initialPitch = pitch;
            initialized = true;
        }

        private void ApplyCameraTransform()
        {
            Quaternion rotation = Quaternion.Euler(pitch, yaw, 0f);
            transform.rotation = rotation;
            transform.position = pivot - (rotation * Vector3.forward) * distance;
        }

        private static bool IsTextInputFocused()
        {
            if (EventSystem.current == null || EventSystem.current.currentSelectedGameObject == null)
                return false;

            var selected = EventSystem.current.currentSelectedGameObject;
            return selected.GetComponent<UnityEngine.UI.InputField>() != null ||
                   selected.GetComponent("TMP_InputField") != null;
        }
    }
}
