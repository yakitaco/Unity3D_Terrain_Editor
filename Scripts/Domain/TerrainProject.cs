using System;

namespace TerrainEditor.Domain
{
    [Serializable]
    public sealed class TerrainProject
    {
        public string ProjectId { get; private set; }
        public int SchemaVersion { get; private set; }
        public TerrainSettings Settings { get; private set; }
        public HeightmapData Heightmap { get; private set; }

        public TerrainProject(TerrainSettings settings, HeightmapData heightmap)
        {
            ProjectId = Guid.NewGuid().ToString("N");
            SchemaVersion = 1;
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Heightmap = heightmap ?? throw new ArgumentNullException(nameof(heightmap));
        }
    }
}
