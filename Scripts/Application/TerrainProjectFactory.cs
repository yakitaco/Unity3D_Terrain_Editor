using TerrainEditor.Domain;

namespace TerrainEditor.Application
{
    public static class TerrainProjectFactory
    {
        public static bool TryCreate(TerrainSettings settings, out TerrainProject project, out string error)
        {
            project = null;

            if (settings == null)
            {
                error = "地形設定が指定されていません。";
                return false;
            }

            if (!settings.Validate(out error))
                return false;

            try
            {
                var heightmap = new HeightmapData(settings.HeightmapResolution);
                project = new TerrainProject(settings, heightmap);
                error = null;
                return true;
            }
            catch (System.Exception ex)
            {
                error = "地形データを作成できませんでした: " + ex.Message;
                return false;
            }
        }
    }
}
