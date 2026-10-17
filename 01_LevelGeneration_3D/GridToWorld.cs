using UnityEngine;

public static class GridToWorld
{
    // 1 grid cell = 1 Unity unit (기존 Tilemap 1:1 대응)
    public static Vector3 CellToWorld(Vector2Int cell, float cellSize = 1f, float groundY = 0f)
        => new Vector3(cell.x * cellSize, groundY, cell.y * cellSize);

    public static Vector3 CellCenterToWorld(Vector2Int cell, float cellSize = 1f, float groundY = 0f)
        => new Vector3((cell.x + 0.5f) * cellSize, groundY, (cell.y + 0.5f) * cellSize);

    public static Vector2Int WorldToCell(Vector3 world, float cellSize = 1f)
        => new Vector2Int(Mathf.FloorToInt(world.x / cellSize), Mathf.FloorToInt(world.z / cellSize));
}