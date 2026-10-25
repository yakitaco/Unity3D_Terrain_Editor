using System;
using TerrainEditor.Domain;
using UnityEngine;

namespace TerrainEditor.Infrastructure.Rendering
{
    public sealed class TerrainRenderAdapter
    {
        private Terrain terrain;
        private TerrainData terrainData;

        public Terrain CurrentTerrain => terrain;
        public TerrainData CurrentTerrainData => terrainData;

        public void ApplyHeights(float[,] heights)
        {
            if (terrainData == null || heights == null) return;
            terrainData.SetHeightsDelayLOD(0, 0, heights);
            terrainData.SyncHeightmap();
        }

        public bool TryBuild(TerrainProject project, Transform parent, out string error)
        {
            if (project == null)
            {
                error = "プロジェクトが指定されていません。";
                return false;
            }

            TerrainData newData = null;
            GameObject newObject = null;

            try
            {
                var settings = project.Settings;
                newData = new TerrainData
                {
                    heightmapResolution = settings.HeightmapResolution,
                    size = new Vector3(settings.WidthMeters, settings.HeightMeters, settings.LengthMeters)
                };

                newData.SetHeights(0, 0, project.Heightmap.CopyHeights());

                newObject = Terrain.CreateTerrainGameObject(newData);
                newObject.name = "Terrain_" + settings.ProjectName;
                newObject.transform.position = Vector3.zero;

                if (parent != null)
                    newObject.transform.SetParent(parent, true);

                var newTerrain = newObject.GetComponent<Terrain>();
                var collider = newObject.GetComponent<TerrainCollider>();

                if (newTerrain == null || collider == null)
                    throw new InvalidOperationException("TerrainまたはTerrainColliderを生成できませんでした。");

                newTerrain.drawHeightmap = true;
                newTerrain.drawTreesAndFoliage = true;

                // Commit only after the new terrain has been created successfully.
                var oldObject = terrain != null ? terrain.gameObject : null;
                var oldData = terrainData;

                terrain = newTerrain;
                terrainData = newData;

                if (oldObject != null)
                    UnityEngine.Object.Destroy(oldObject);
                if (oldData != null)
                    UnityEngine.Object.Destroy(oldData);

                error = null;
                return true;
            }
            catch (Exception ex)
            {
                if (newObject != null)
                    UnityEngine.Object.Destroy(newObject);
                if (newData != null)
                    UnityEngine.Object.Destroy(newData);

                error = "Terrain生成に失敗しました: " + ex.Message;
                return false;
            }
        }

        public void Clear()
        {
            if (terrain != null)
                UnityEngine.Object.Destroy(terrain.gameObject);
            if (terrainData != null)
                UnityEngine.Object.Destroy(terrainData);

            terrain = null;
            terrainData = null;
        }
    }
}
