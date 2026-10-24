using System;
using UnityEngine;

namespace TerrainEditor.Domain
{
    [Serializable]
    public sealed class HeightmapData
    {
        public int Resolution { get; private set; }
        private float[,] heights;

        public HeightmapData(int resolution)
        {
            if (!TerrainSettings.IsSupportedHeightmapResolution(resolution))
                throw new ArgumentOutOfRangeException(nameof(resolution), "Unsupported Unity Terrain heightmap resolution.");

            Resolution = resolution;
            heights = new float[resolution, resolution];
            // Initial flat terrain at normalized height 0.
        }

        public float[,] CopyHeights()
        {
            var copy = new float[Resolution, Resolution];
            Array.Copy(heights, copy, heights.Length);
            return copy;
        }

        public float Get(int x, int z)
        {
            ValidateIndex(x, z);
            return heights[z, x];
        }

        public void Set(int x, int z, float value)
        {
            ValidateIndex(x, z);
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Height must be finite.");

            heights[z, x] = Mathf.Clamp01(value);
        }

        private void ValidateIndex(int x, int z)
        {
            if (x < 0 || x >= Resolution || z < 0 || z >= Resolution)
                throw new ArgumentOutOfRangeException("Heightmap index is outside the map.");
        }
    }
}
