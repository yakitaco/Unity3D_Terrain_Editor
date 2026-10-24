using System;
using UnityEngine;

namespace TerrainEditor.Domain
{
    [Serializable]
    public sealed class TerrainSettings
    {
        public string ProjectName = "Untitled Terrain";
        public float WidthMeters = 1000f;
        public float LengthMeters = 1000f;
        public float HeightMeters = 300f;
        public int HeightmapResolution = 513;
        public float SeaLevelMeters = 30f;

        public bool Validate(out string error)
        {
            if (string.IsNullOrWhiteSpace(ProjectName))
            {
                error = "地形名を入力してください。";
                return false;
            }

            if (!IsFinite(WidthMeters) || WidthMeters < 1f || WidthMeters > 100000f)
            {
                error = "幅は1～100000 mの範囲で指定してください。";
                return false;
            }

            if (!IsFinite(LengthMeters) || LengthMeters < 1f || LengthMeters > 100000f)
            {
                error = "奥行きは1～100000 mの範囲で指定してください。";
                return false;
            }

            if (!IsFinite(HeightMeters) || HeightMeters < 1f || HeightMeters > 10000f)
            {
                error = "高さ範囲は1～10000 mの範囲で指定してください。";
                return false;
            }

            if (!IsFinite(SeaLevelMeters) || SeaLevelMeters < 0f || SeaLevelMeters > HeightMeters)
            {
                error = "海面標高は0～高さ範囲の間で指定してください。";
                return false;
            }

            if (!IsSupportedHeightmapResolution(HeightmapResolution))
            {
                error = "Heightmap解像度はUnity Terrainでサポートされる値（33, 65, 129, 257, 513, 1025, 2049, 4097）から選択してください。";
                return false;
            }

            error = null;
            return true;
        }

        public static bool IsSupportedHeightmapResolution(int value)
        {
            switch (value)
            {
                case 33:
                case 65:
                case 129:
                case 257:
                case 513:
                case 1025:
                case 2049:
                case 4097:
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
