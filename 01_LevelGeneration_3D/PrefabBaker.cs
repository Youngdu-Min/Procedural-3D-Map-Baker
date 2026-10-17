using UnityEngine;

public sealed class PrefabBakeConfig
{
    public GameObject FloorPrefab;
    public GameObject WallPrefab;
    public float CellSize = 1f;
    public float GroundY = 0f;
    public bool BakeWalls = true;
}

public static class PrefabBaker
{
    public static void Bake(MapGrid grid, PrefabBakeConfig config, Transform floorRoot, Transform wallRoot)
    {
        ClearChildren(floorRoot);
        ClearChildren(wallRoot);

        // 1패스: floor
        for (int x = 0; x < grid.Size.x; x++)
        {
            for (int y = 0; y < grid.Size.y; y++)
            {
                var pos = new Vector2Int(x, y);
                var cell = grid[pos];
                if (cell != MapCellType.Room && cell != MapCellType.Corridor) continue;
                if (config.FloorPrefab == null) continue;

                var world = GridToWorld.CellCenterToWorld(pos, config.CellSize, config.GroundY);
                Object.Instantiate(config.FloorPrefab, world, Quaternion.identity, floorRoot);
            }
        }

        // 2패스: wall
        if (!config.BakeWalls || wallRoot == null || config.WallPrefab == null) return;

        for (int x = 0; x < grid.Size.x; x++)
        {
            for (int y = 0; y < grid.Size.y; y++)
            {
                var pos = new Vector2Int(x, y);
                if (grid[pos] != MapCellType.None) continue;

                var world = GridToWorld.CellCenterToWorld(pos, config.CellSize, config.GroundY);
                Object.Instantiate(config.WallPrefab, world, Quaternion.identity, wallRoot);
            }
        }
    }

    private static void ClearChildren(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
            Object.Destroy(root.GetChild(i).gameObject);
    }
}