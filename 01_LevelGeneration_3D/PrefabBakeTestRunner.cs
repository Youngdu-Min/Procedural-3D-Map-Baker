using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Serialization;

public class PrefabBakeTestRunner : MonoBehaviour
{
    [System.Serializable]
    public struct PropEntry
    {
        [Tooltip("프롭 프리팹")]
        public GameObject Prefab;
        [Tooltip("배치 오프셋")]
        public Vector3 Offset;
    }

    [System.Serializable]
    public struct WallpaperEntry
    {
        [Tooltip("벽지 프리팹")]
        public GameObject Prefab;
        [Tooltip("배치 오프셋")]
        public Vector3 Offset;
    }

    [System.Serializable]
    public struct WeightedPrefab
    {
        [Tooltip("배치할 조합 프리팹 (루트에 LevelModule 필요)")]
        public LevelModule Prefab;
        [Tooltip("가중치 (0 이하는 선택되지 않음)")]
        public float Weight;
    }

    private struct ModuleCandidate
    {
        public LevelModule Module;
        public bool Rotated;
        public float Weight;
    }

    private readonly struct EdgeRun
    {
        public readonly int FixedCoord;
        public readonly int Start;
        public readonly int Length;

        public EdgeRun(int fixedCoord, int start, int length)
        {
            FixedCoord = fixedCoord;
            Start = start;
            Length = length;
        }
    }

    private readonly struct DoorRunPlan
    {
        public readonly Vector2Int Dir;
        public readonly Quaternion Rotation;
        public readonly EdgeRun Run;
        public readonly LevelModule Door;

        // door가 null이면 문틀이 들어가지 않는 짧은 경계
        public DoorRunPlan(Vector2Int dir, Quaternion rotation, EdgeRun run, LevelModule door)
        {
            Dir = dir;
            Rotation = rotation;
            Run = run;
            Door = door;
        }

        public int DoorStart => Run.Start + (Run.Length - Door.Size.x) / 2;
    }

    private const int NAV_AREA_NOT_WALKABLE = 1;
    private const int FNV_OFFSET_BASIS = -2128831035;
    private const int FNV_PRIME = 16777619;

    // 경계 방향(바닥 셀 → 바깥)별 회전 — 모듈 마감면(로컬 +Z)이 방 안쪽(-dir)을 향하도록
    private static readonly (Vector2Int dir, Quaternion rotation)[] WallDirections =
    {
        (Vector2Int.up,    Quaternion.Euler(0f, 180f, 0f)),
        (Vector2Int.down,  Quaternion.Euler(0f, 0f,   0f)),
        (Vector2Int.right, Quaternion.Euler(0f, 270f, 0f)),
        (Vector2Int.left,  Quaternion.Euler(0f, 90f,  0f)),
    };

    // 격자 꼭짓점에서 뻗는 네 반변 E·W·N·S — 꼭짓점 기준 경계 키 오프셋과 뻗는 방향. (i ^ 1)이 반대쪽 반변, i < 2가 가로
    private static readonly Vector3Int[] HalfEdgeKeyOffsets =
    {
        new Vector3Int(0, 0, 0), new Vector3Int(-1, 0, 0), new Vector3Int(0, 0, 1), new Vector3Int(0, -1, 1),
    };
    private static readonly Vector2Int[] HalfEdgeDirections = { Vector2Int.right, Vector2Int.left, Vector2Int.up, Vector2Int.down };

    [Header("── Reference ─────────────────")]
    [Tooltip("8단계 Validation 결과 기준 Grid 사용")]
    [SerializeField] private ValidationTestRunner m_validationRef;

    [Tooltip("4단계 필수 통로 결과 참조")]
    [SerializeField] private MandatoryCorridorTestRunner m_mandatoryRef;

    [Tooltip("5단계 추가 통로 결과 참조")]
    [SerializeField] private OptionalCorridorTestRunner m_optionalRef;

    [Tooltip("스폰된 오브젝트 위치 참조 (프롭 겹침 방지)")]
    [SerializeField] private ObjectiveStateController m_objectiveStateControllerRef;

    [Header("── Roots ────────────────────")]
    [SerializeField] private Transform m_floorRoot;
    [SerializeField] private Transform m_wallRoot;
    [SerializeField] private Transform m_wallpaperRoot;
    [SerializeField] private Transform m_ceilingRoot;
    [SerializeField] private Transform m_propRoot;

    [Header("── Prefabs ───────────────────")]
    [Tooltip("바닥 모듈 목록 (Kind=Floor, 2D 패킹, 1x1 필수)")]
    [SerializeField] private WeightedPrefab[] m_floorVariants;

    [Tooltip("천장 모듈 목록 (Kind=Floor, 벽 높이에 2D 패킹, 1x1 필수)")]
    [SerializeField] private WeightedPrefab[] m_ceilingVariants;

    [Tooltip("벽 모듈 목록 (Kind=Wall, 벽 구간 채우기, 폭 1 필수)")]
    [SerializeField] private WeightedPrefab[] m_wallVariants;

    [Tooltip("Room-Corridor 경계 문틀 모듈 목록 (Kind=DoorFrame, 양옆은 벽 모듈로 채움)")]
    [SerializeField] private WeightedPrefab[] m_doorFrameVariants;

    [Tooltip("벽 끝이 보행 공간에 노출되는 곳에 붙일 캡 목록 (Kind=EndCap, 벽과 같은 스타일) — BR 벽 메쉬는 끝단 면이 없음")]
    [SerializeField] private WeightedPrefab[] m_endCapVariants;

    [Header("── Blocked Floor ─────────────")]
    [Tooltip("통행 불가 바닥 모듈(구멍)을 방 가장자리에서 최소 몇 칸 안쪽에만 배치할지. 테두리가 비어 있어 방의 모든 문이 연결 유지됨 (NavMesh 에이전트 반경 0.5 기준 2 권장)")]
    [Range(1, 4)]
    [SerializeField] private int m_holeRoomMargin = 2;

    [Header("── Border ────────────────────")]
    [Tooltip("전체 맵 외곽 테두리 확장 두께 (타일 단위)")]
    [Range(0, 10)]
    [SerializeField] private int m_mapBorderPadding = 1;

    [Header("── Wallpaper ─────────────────")]
    [Tooltip("룸 인접 벽 그룹 교체 확률 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float m_roomWallpaperChance = 0.3f;

    [Tooltip("통로 인접 벽 그룹 교체 확률 (0~1)")]
    [Range(0f, 1f)]
    [SerializeField] private float m_corridorWallpaperChance = 0.15f;

    [Tooltip("벽지 프리팹 후보 목록")]
    [SerializeField] private WallpaperEntry[] m_wallpaperEntries;

    [Header("── Props ─────────────────────")]
    [Tooltip("방 안에 배치할 프롭 프리팹 후보 목록")]
    [SerializeField] private PropEntry[] m_propEntries;

    [Tooltip("방 1개당 프롭 최대 배치 개수")]
    [Range(0, 20)]
    [SerializeField] private int m_maxPropsPerRoom = 3;

    [Tooltip("프롭 간 최소 거리 (타일 단위)")]
    [Range(1, 5)]
    [SerializeField] private int m_minPropDistance = 2;

    [Header("── Config ────────────────────")]
    [Tooltip("벽 굽기 여부")]
    [SerializeField] private bool m_bakeWalls = true;

    [Tooltip("천장 굽기 여부")]
    [SerializeField] private bool m_bakeCeiling = true;

    [Tooltip("1 grid cell 당 Unity unit 크기")]
    [SerializeField] private float m_cellSize = 1f;

    [Tooltip("벽·문틀 모듈을 길이 방향으로 늘려 이웃과 겹치게 할 총 길이(유닛). 끝단 면이 없는 BR 벽의 이음선 균열 방지")]
    [Range(0f, 0.05f)]
    [SerializeField] private float m_seamOverlap = 0.005f;

    [Tooltip("바닥 Y 위치")]
    [SerializeField] private const float m_groundY = 0f;

    public MapGrid Grid => m_validationRef?.Grid;

    private NavMeshData _navMeshData;
    private NavMeshDataInstance _navMeshDataInstance;
    [Tooltip("[레거시 폴백] 구버전 단일 벽 프리팹용 Z축(두께) 강제 스케일. 0이면 미적용(신규 벽 모듈은 원본 스케일 유지). 실제 두께가 모델링된 모듈러 벽에는 사용하지 말 것.")]
    [FormerlySerializedAs("m_wallThickness")]
    [SerializeField] private float m_legacyWallThicknessFallback = 0;

    private readonly List<ModuleCandidate> m_candidates = new List<ModuleCandidate>();

    // 벽이 놓인 셀 경계 한 칸 → 두께가 나간 방향(+1/-1). 키는 ToEdgeKey 참고
    private readonly Dictionary<Vector3Int, int> m_wallEdges = new Dictionary<Vector3Int, int>();
    private readonly Dictionary<LevelModule, float> m_moduleThickness = new Dictionary<LevelModule, float>();
    private int m_cornerExtensionCount;
    private IRandomSource m_bakeRandom;
    private int m_placementHash;
    private int m_placementCount;

    [ContextMenu("Bake Prefabs")]
    public void Bake()
    {
        RemoveNavMesh();

        if (m_validationRef == null || m_validationRef.Grid == null)
        {
            Debug.LogWarning("[PrefabBake] Run ValidationTestRunner first.");
            return;
        }

        MapGenerationResult mapResult = MapGenerationMasterRunner.LastResult;
        if (mapResult == null) return;

        // 전역 UnityEngine.Random과 분리 — 생성된 프리팹의 Awake/OnEnable이 난수 순서를 흐트러뜨리지 않도록
        m_bakeRandom = new SystemRandomSource(mapResult.PropBakeSeed);
        m_placementHash = FNV_OFFSET_BASIS;
        m_placementCount = 0;
        m_wallEdges.Clear();
        m_cornerExtensionCount = 0;

        MapGrid grid = m_validationRef.Grid;

        Clear();
        ValidateVariants();

        BakeFloor(grid, mapResult);
        BakeCeiling(grid);
        BakeWalls(grid);
        BakeDoorFrames(grid);
        BakeWallEndCaps(grid);
        Debug.Log($"[PrefabBake] 안쪽 모서리 벽 끝 연장 {m_cornerExtensionCount}개.");
        BakeWallpaper(grid);
        BakeProps(grid);

        Debug.Log($"[PrefabBake] Done. CellSize={m_cellSize} GroundY={m_groundY} Placed={m_placementCount} PlacementHash={m_placementHash:X8}");

        BakeNavMesh();
    }

    private void BakeNavMesh()
    {
        var sources = new List<NavMeshBuildSource>();
        var markups = new List<NavMeshBuildMarkup>();

        NavMeshBuilder.CollectSources(m_floorRoot, ~0, NavMeshCollectGeometry.RenderMeshes, 0, markups, sources);

        // 벽을 비보행 영역으로 포함 — 바닥만 수집하면 Room-Corridor 경계의 채움 벽을 AI가 통과함
        if (m_wallRoot != null)
        {
            List<NavMeshBuildSource> wallSources = new List<NavMeshBuildSource>();
            NavMeshBuilder.CollectSources(m_wallRoot, ~0, NavMeshCollectGeometry.RenderMeshes, NAV_AREA_NOT_WALKABLE, markups, wallSources);
            sources.AddRange(wallSources);
        }

        var bounds = new Bounds(
            new Vector3(m_validationRef.Grid.Size.x * m_cellSize / 2f, 0f, m_validationRef.Grid.Size.y * m_cellSize / 2f),
            new Vector3(m_validationRef.Grid.Size.x * m_cellSize + m_mapBorderPadding * 2f, 10f, m_validationRef.Grid.Size.y * m_cellSize + m_mapBorderPadding * 2f));

        if (sources.Count == 0)
        {
            Debug.LogWarning("[PrefabBake] NavMesh 소스 없음 — 메시 렌더러 확인 필요.");
            return;
        }

        _navMeshData = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), sources, bounds, Vector3.zero, Quaternion.identity);

        if (_navMeshData == null)
        {
            Debug.LogWarning("[PrefabBake] NavMeshData 생성 실패.");
            return;
        }

        _navMeshDataInstance = NavMesh.AddNavMeshData(_navMeshData);
        Debug.Log($"[PrefabBake] NavMesh Bake 완료. Sources={sources.Count}");
    }

    private void BakeFloor(MapGrid grid, MapGenerationResult mapResult)
    {
        mapResult.BlockedCells.Clear();
        if (m_floorRoot == null) return;

        bool[,] blockedModuleMask = BuildBlockedModuleMask(grid, mapResult);
        PackFloorModules(grid, m_floorVariants, m_groundY, m_floorRoot, "Floor", blockedModuleMask, mapResult.BlockedCells);
    }

    private void BakeCeiling(MapGrid grid)
    {
        if (!m_bakeCeiling || m_ceilingRoot == null) return;
        PackFloorModules(grid, m_ceilingVariants, m_groundY + LevelModule.WALL_HEIGHT, m_ceilingRoot, "Ceiling", null, null);
    }

    // 통행 불가 모듈 허용 영역: 시작/목표 후보 방을 제외한 방에서 가장자리 m_holeRoomMargin칸 안쪽
    // 테두리 띠가 항상 비어 있으므로 방의 모든 출입구가 서로 연결된 상태로 유지됨
    private bool[,] BuildBlockedModuleMask(MapGrid grid, MapGenerationResult mapResult)
    {
        bool[,] mask = new bool[grid.Size.x, grid.Size.y];
        if (mapResult.Rooms == null) return mask;

        HashSet<int> excludedRooms = new HashSet<int>();
        StartGoalResult startGoal = mapResult.StartGoalResult;
        if (startGoal != null)
        {
            excludedRooms.Add(startGoal.StartRoomIndex);
            foreach (int goalRoomIndex in startGoal.GoalRoomIndices)
                excludedRooms.Add(goalRoomIndex);
        }

        for (int roomIndex = 0; roomIndex < mapResult.Rooms.Count; roomIndex++)
        {
            if (excludedRooms.Contains(roomIndex)) continue;

            RectInt bounds = mapResult.Rooms[roomIndex].Bounds;
            for (int cellX = bounds.xMin + m_holeRoomMargin; cellX < bounds.xMax - m_holeRoomMargin; cellX++)
                for (int cellY = bounds.yMin + m_holeRoomMargin; cellY < bounds.yMax - m_holeRoomMargin; cellY++)
                {
                    Vector2Int cell = new Vector2Int(cellX, cellY);
                    if (grid.InBounds(cell) && grid[cell] == MapCellType.Room)
                        mask[cellX, cellY] = true;
                }
        }

        return mask;
    }

    // 행 우선 스캔으로 첫 빈 셀을 최소 코너로 삼아, 들어가는 모듈 중 가중 랜덤 선택 — 1x1이 있으면 빈칸 없이 채워짐
    // blockedModuleMask가 null이면(천장) 통행 불가 모듈도 제한 없이 배치하고 기록하지 않음
    private void PackFloorModules(MapGrid grid, WeightedPrefab[] variants, float height, Transform root, string label,
        bool[,] blockedModuleMask, HashSet<Vector2Int> blockedCells)
    {
        if (variants == null || variants.Length == 0) return;

        bool[,] filled = new bool[grid.Size.x, grid.Size.y];
        int floorCellCount = 0;
        int coveredCellCount = 0;
        int blockedModuleCount = 0;

        for (int cellY = 0; cellY < grid.Size.y; cellY++)
        {
            for (int cellX = 0; cellX < grid.Size.x; cellX++)
            {
                if (!IsFloor(grid[new Vector2Int(cellX, cellY)])) continue;
                floorCellCount++;
                if (filled[cellX, cellY]) continue;

                m_candidates.Clear();
                foreach (WeightedPrefab variant in variants)
                {
                    if (!IsUsable(variant, ModuleKind.Floor)) continue;

                    Vector2Int size = variant.Prefab.Size;
                    bool[,] requiredMask = variant.Prefab.BlocksMovement ? blockedModuleMask : null;
                    if (variant.Prefab.AllowRotate90)
                    {
                        float halfWeight = variant.Weight * 0.5f;
                        if (FitsFloor(grid, filled, requiredMask, cellX, cellY, size.x, size.y)) AddCandidate(variant.Prefab, false, halfWeight);
                        if (FitsFloor(grid, filled, requiredMask, cellX, cellY, size.y, size.x)) AddCandidate(variant.Prefab, true, halfWeight);
                    }
                    else if (FitsFloor(grid, filled, requiredMask, cellX, cellY, size.x, size.y))
                    {
                        AddCandidate(variant.Prefab, false, variant.Weight);
                    }
                }

                if (!TryPickCandidate(out ModuleCandidate picked)) continue;

                int width = picked.Rotated ? picked.Module.Size.y : picked.Module.Size.x;
                int depth = picked.Rotated ? picked.Module.Size.x : picked.Module.Size.y;
                bool recordBlocked = blockedCells != null && picked.Module.BlocksMovement;

                for (int offsetX = 0; offsetX < width; offsetX++)
                    for (int offsetY = 0; offsetY < depth; offsetY++)
                    {
                        filled[cellX + offsetX, cellY + offsetY] = true;
                        if (recordBlocked) blockedCells.Add(new Vector2Int(cellX + offsetX, cellY + offsetY));
                    }
                coveredCellCount += width * depth;
                if (recordBlocked) blockedModuleCount++;

                Vector3 center = new Vector3((cellX + width * 0.5f) * m_cellSize, height, (cellY + depth * 0.5f) * m_cellSize);
                Quaternion rotation = picked.Rotated ? Quaternion.Euler(0f, 90f, 0f) : Quaternion.identity;
                Spawn(picked.Module.gameObject, center, rotation, root);
            }
        }

        if (coveredCellCount != floorCellCount)
            Debug.LogWarning($"[PrefabBake] {label} 패킹 불완전: 셀 {floorCellCount}개 중 {coveredCellCount}개만 채움 — 1x1 모듈 확인 필요.");

        if (blockedCells != null)
            Debug.Log($"[PrefabBake] {label} 통행 불가 모듈 {blockedModuleCount}개 배치, 막힌 셀 {blockedCells.Count}개.");
    }

    private void BakeWalls(MapGrid grid)
    {
        if (!CanBakeWalls()) return;

        foreach ((Vector2Int dir, Quaternion rotation) in WallDirections)
            foreach (EdgeRun run in CollectRuns(grid, dir, IsWallEdge))
                FillRunWithWalls(dir, rotation, run.FixedCoord, run.Start, run.Length,
                    IsInnerCornerEnd(grid, dir, run.FixedCoord, run.Start - 1),
                    IsInnerCornerEnd(grid, dir, run.FixedCoord, run.Start + run.Length));
    }

    // Room→Corridor 경계: 문틀이 들어가는 구간은 정중앙에 문틀 + 양옆 벽
    // 문틀이 안 들어가는 짧은 구간은 벽으로 막되, 막으면 방·통로가 고립되는 경우에만 열어둠
    private void BakeDoorFrames(MapGrid grid)
    {
        if (m_wallRoot == null || m_doorFrameVariants == null || m_doorFrameVariants.Length == 0) return;

        List<DoorRunPlan> plans = PlanDoorRuns(grid);
        HashSet<int> keptOpenPlans = FindShortRunsToKeepOpen(grid, plans);

        int closedCount = 0;
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            DoorRunPlan plan = plans[planIndex];
            if (plan.Door != null)
            {
                PlaceDoorRun(grid, plan);
                continue;
            }

            if (keptOpenPlans.Contains(planIndex) || !CanBakeWalls()) continue;

            EdgeRun run = plan.Run;
            FillRunWithWalls(plan.Dir, plan.Rotation, run.FixedCoord, run.Start, run.Length,
                IsInnerCornerEnd(grid, plan.Dir, run.FixedCoord, run.Start - 1),
                IsInnerCornerEnd(grid, plan.Dir, run.FixedCoord, run.Start + run.Length));
            closedCount++;
        }

        Debug.Log($"[PrefabBake] 문틀 없는 방-통로 경계: 벽으로 막음 {closedCount}개, 연결 유지를 위해 열어둠 {keptOpenPlans.Count}개.");
    }

    private List<DoorRunPlan> PlanDoorRuns(MapGrid grid)
    {
        List<DoorRunPlan> plans = new List<DoorRunPlan>();
        foreach ((Vector2Int dir, Quaternion rotation) in WallDirections)
        {
            foreach (EdgeRun run in CollectRuns(grid, dir, IsDoorEdge))
            {
                LevelModule door = null;

                // 길이 1 구간은 모서리 접촉 등 문틀을 둘 수 없는 경우
                if (run.Length >= 2)
                {
                    m_candidates.Clear();
                    foreach (WeightedPrefab variant in m_doorFrameVariants)
                        if (IsUsable(variant, ModuleKind.DoorFrame) && variant.Prefab.Size.x <= run.Length)
                            AddCandidate(variant.Prefab, false, variant.Weight);

                    if (TryPickCandidate(out ModuleCandidate picked)) door = picked.Module;
                }

                plans.Add(new DoorRunPlan(dir, rotation, run, door));
            }
        }
        return plans;
    }

    // 짧은 경계를 모두 막은 상태에서 시작해, 양쪽이 다른 연결 요소일 때만 열어둠
    // (크루스칼 방식 — 짧은 경계를 전부 열었을 때와 연결 요소가 같아짐)
    private static HashSet<int> FindShortRunsToKeepOpen(MapGrid grid, List<DoorRunPlan> plans)
    {
        int[] parent = new int[grid.Size.x * grid.Size.y];
        for (int i = 0; i < parent.Length; i++) parent[i] = i;

        // 방-통로 경계 중 문틀 칸만 통과 가능
        HashSet<Vector3Int> doorEdges = new HashSet<Vector3Int>();
        foreach (DoorRunPlan plan in plans)
        {
            if (plan.Door == null) continue;
            for (int variableCoord = plan.DoorStart; variableCoord < plan.DoorStart + plan.Door.Size.x; variableCoord++)
                doorEdges.Add(ToEdgeKey(RunCell(plan.Dir, plan.Run.FixedCoord, variableCoord), plan.Dir));
        }

        Vector2Int[] forwardDirections = { Vector2Int.right, Vector2Int.up };
        for (int cellX = 0; cellX < grid.Size.x; cellX++)
            for (int cellY = 0; cellY < grid.Size.y; cellY++)
            {
                Vector2Int cell = new Vector2Int(cellX, cellY);
                if (!IsFloor(grid[cell])) continue;

                foreach (Vector2Int forward in forwardDirections)
                {
                    Vector2Int neighbor = cell + forward;
                    if (!grid.InBounds(neighbor) || !IsFloor(grid[neighbor])) continue;

                    bool isDoorBoundary = grid[cell] != grid[neighbor];
                    if (!isDoorBoundary || doorEdges.Contains(ToEdgeKey(cell, forward)))
                        Union(parent, CellIndex(grid, cell), CellIndex(grid, neighbor));
                }
            }

        HashSet<int> keptOpen = new HashSet<int>();
        for (int planIndex = 0; planIndex < plans.Count; planIndex++)
        {
            DoorRunPlan plan = plans[planIndex];
            if (plan.Door != null) continue;

            bool connectsSeparateAreas = false;
            for (int variableCoord = plan.Run.Start; variableCoord < plan.Run.Start + plan.Run.Length; variableCoord++)
            {
                Vector2Int cell = RunCell(plan.Dir, plan.Run.FixedCoord, variableCoord);
                if (Find(parent, CellIndex(grid, cell)) != Find(parent, CellIndex(grid, cell + plan.Dir)))
                    connectsSeparateAreas = true;
            }
            if (!connectsSeparateAreas) continue;

            keptOpen.Add(planIndex);
            for (int variableCoord = plan.Run.Start; variableCoord < plan.Run.Start + plan.Run.Length; variableCoord++)
            {
                Vector2Int cell = RunCell(plan.Dir, plan.Run.FixedCoord, variableCoord);
                Union(parent, CellIndex(grid, cell), CellIndex(grid, cell + plan.Dir));
            }
        }
        return keptOpen;
    }

    private void PlaceDoorRun(MapGrid grid, DoorRunPlan plan)
    {
        EdgeRun run = plan.Run;
        int doorWidth = plan.Door.Size.x;
        int doorStart = plan.DoorStart;
        int highSideStart = doorStart + doorWidth;
        int lowSideLength = doorStart - run.Start;
        int highSideLength = run.Start + run.Length - highSideStart;
        bool extendLow = IsInnerCornerEnd(grid, plan.Dir, run.FixedCoord, run.Start - 1);
        bool extendHigh = IsInnerCornerEnd(grid, plan.Dir, run.FixedCoord, run.Start + run.Length);

        // 문틀이 구간 끝에 닿아 있으면 문틀 자체를 모서리로 연장
        float doorLowExtension = lowSideLength == 0 && extendLow ? GetModuleThickness(plan.Door) : 0f;
        float doorHighExtension = highSideLength == 0 && extendHigh ? GetModuleThickness(plan.Door) : 0f;
        SpawnLinearModule(plan.Door, plan.Dir, plan.Rotation, run.FixedCoord, doorStart, doorWidth, doorLowExtension, doorHighExtension, 1f);

        if (!CanBakeWalls()) return;
        FillRunWithWalls(plan.Dir, plan.Rotation, run.FixedCoord, run.Start, lowSideLength, extendLow, false);
        FillRunWithWalls(plan.Dir, plan.Rotation, run.FixedCoord, highSideStart, highSideLength, false, extendHigh);
    }

    private void FillRunWithWalls(Vector2Int dir, Quaternion rotation, int fixedCoord, int start, int length, bool extendLow, bool extendHigh)
    {
        int cursor = start;
        int end = start + length;

        // [레거시 폴백] 0이 아닐 때만 Z축(두께 방향) 강제 스케일
        float thicknessScale = m_legacyWallThicknessFallback != 0f ? m_legacyWallThicknessFallback : 1f;

        while (cursor < end)
        {
            LevelModule wallModule = PickWallModule(end - cursor);
            if (wallModule == null)
            {
                Debug.LogWarning($"[PrefabBake] 폭 {end - cursor} 이하 벽 모듈 없음 — 폭 1 벽 모듈 확인 필요.");
                return;
            }

            int width = wallModule.Size.x;
            float lowExtension = extendLow && cursor == start ? GetModuleThickness(wallModule) : 0f;
            float highExtension = extendHigh && cursor + width == end ? GetModuleThickness(wallModule) : 0f;
            SpawnLinearModule(wallModule, dir, rotation, fixedCoord, cursor, width, lowExtension, highExtension, thicknessScale);

            cursor += width;
        }
    }

    // 벽·문틀 배치. BR 벽은 끝단 면이 없어 이음선 균열로 속이 비쳐 보이므로 길이 방향으로 늘려 이웃과 겹치게 하고,
    // 안쪽 모서리 끝은 두께만큼 더 늘려 모서리 빈칸을 채우고 서로의 끝을 파묻음
    private void SpawnLinearModule(LevelModule module, Vector2Int dir, Quaternion rotation, int fixedCoord, int start, int width,
        float lowExtension, float highExtension, float thicknessScale)
    {
        float variableCenter = start + width * 0.5f + (highExtension - lowExtension) * 0.5f / m_cellSize;
        Vector3 position = EdgeSegmentToWorld(dir, fixedCoord, variableCenter);
        GameObject instance = Spawn(module.gameObject, position, rotation, m_wallRoot);

        float lengthScale = (width + m_seamOverlap + lowExtension + highExtension) / width;
        instance.transform.localScale = new Vector3(lengthScale, 1f, thicknessScale);
        RegisterWallEdges(dir, fixedCoord, start, width);

        if (lowExtension > 0f) m_cornerExtensionCount++;
        if (highExtension > 0f) m_cornerExtensionCount++;
    }

    // 구간 끝 다음 칸이 벽 양쪽(바닥 쪽·두께 쪽) 모두 보행 불가면 안쪽 모서리
    // 이때 모서리 빈칸은 두께 쪽 보이드에 생기므로 연장해도 보행 공간을 침범하지 않음
    private static bool IsInnerCornerEnd(MapGrid grid, Vector2Int dir, int fixedCoord, int nextVariableCoord)
    {
        Vector2Int nextCell = RunCell(dir, fixedCoord, nextVariableCoord);
        return !IsFloor(grid[nextCell]) && !IsFloor(grid[nextCell + dir]);
    }

    // 모서리 연장 길이 = 모듈 두께 (자식 메쉬 bounds의 Z 크기, 모듈별 캐시)
    private float GetModuleThickness(LevelModule module)
    {
        if (m_moduleThickness.TryGetValue(module, out float cached)) return cached;

        MeshFilter meshFilter = module.GetComponentInChildren<MeshFilter>();
        float thickness = meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.bounds.size.z : 0f;
        m_moduleThickness[module] = thickness;
        return thickness;
    }

    private static Vector2Int RunCell(Vector2Int dir, int fixedCoord, int variableCoord)
        => dir.y != 0 ? new Vector2Int(variableCoord, fixedCoord) : new Vector2Int(fixedCoord, variableCoord);

    private static int CellIndex(MapGrid grid, Vector2Int cell) => cell.x + cell.y * grid.Size.x;

    private static int Find(int[] parent, int index)
    {
        int root = index;
        while (parent[root] != root) root = parent[root];
        while (parent[index] != root)
        {
            int next = parent[index];
            parent[index] = root;
            index = next;
        }
        return root;
    }

    private static void Union(int[] parent, int first, int second)
    {
        int firstRoot = Find(parent, first);
        int secondRoot = Find(parent, second);
        if (firstRoot != secondRoot) parent[secondRoot] = firstRoot;
    }

    // BR 벽 메쉬는 끝단 면이 없어, 벽 끝이 보행 공간에 드러나면 속이 비쳐 보임 — 그런 꼭짓점에만 캡을 붙임
    // 대표 사례: 방-통로 경계 벽(두께가 통로 쪽)과 일반 벽(두께가 보이드 쪽)이 한 직선에서 만나 두께 방향이 뒤집히는 모서리
    private void BakeWallEndCaps(MapGrid grid)
    {
        if (m_wallRoot == null || m_endCapVariants == null || m_endCapVariants.Length == 0) return;

        int capCount = 0;
        for (int vertexY = 0; vertexY <= grid.Size.y; vertexY++)
            for (int vertexX = 0; vertexX <= grid.Size.x; vertexX++)
                for (int halfEdge = 0; halfEdge < HalfEdgeKeyOffsets.Length; halfEdge++)
                {
                    Vector3Int vertex = new Vector3Int(vertexX, vertexY, 0);
                    if (IsExposedWallEnd(grid, vertex, halfEdge, out int bodySide) && PlaceEndCap(vertexX, vertexY, halfEdge, bodySide))
                        capCount++;
                }

        Debug.Log($"[PrefabBake] 노출된 벽 끝단 캡 {capCount}개 배치.");
    }

    // 끝단이 가려지는 경우: 같은 두께 방향으로 이어지는 벽 / 두께 쪽 수직 벽(몸체로 채우거나 면과 겹침) / 끝단 앞 셀이 보행 불가
    private bool IsExposedWallEnd(MapGrid grid, Vector3Int vertex, int halfEdge, out int bodySide)
    {
        if (!m_wallEdges.TryGetValue(vertex + HalfEdgeKeyOffsets[halfEdge], out bodySide)) return false;

        Vector3Int continuationKey = vertex + HalfEdgeKeyOffsets[halfEdge ^ 1];
        if (m_wallEdges.TryGetValue(continuationKey, out int continuationSide) && continuationSide == bodySide) return false;

        bool horizontal = halfEdge < 2;
        int perpendicular = horizontal ? (bodySide > 0 ? 2 : 3) : (bodySide > 0 ? 0 : 1);
        if (m_wallEdges.ContainsKey(vertex + HalfEdgeKeyOffsets[perpendicular])) return false;

        return IsFloor(grid[CellOnSide(continuationKey, bodySide)]);
    }

    private bool PlaceEndCap(int vertexX, int vertexY, int halfEdge, int bodySide)
    {
        LevelModule capModule = PickModule(m_endCapVariants, ModuleKind.EndCap);
        if (capModule == null) return false;

        Vector2Int bodyDirection = halfEdge < 2 ? new Vector2Int(0, bodySide) : new Vector2Int(bodySide, 0);
        Quaternion rotation = RotationForBodyDirection(bodyDirection);
        Vector3 position = new Vector3(vertexX * m_cellSize, m_groundY, vertexY * m_cellSize);
        GameObject cap = Spawn(capModule.gameObject, position, rotation, m_wallRoot);

        // 캡은 로컬 +X로 돌출 — 벽 반대쪽을 향해야 하므로 회전만으로 안 맞으면 X 반전
        Vector2Int protrude = Vector2Int.zero - HalfEdgeDirections[halfEdge];
        if (Vector3.Dot(rotation * Vector3.right, new Vector3(protrude.x, 0f, protrude.y)) < 0f)
            cap.transform.localScale = new Vector3(-1f, 1f, 1f);

        return true;
    }

    // 경계 키 양옆 셀 중 side(+1/-1) 쪽 셀
    private static Vector2Int CellOnSide(Vector3Int edgeKey, int side)
        => edgeKey.z == 0
            ? new Vector2Int(edgeKey.x, side > 0 ? edgeKey.y : edgeKey.y - 1)
            : new Vector2Int(side > 0 ? edgeKey.x : edgeKey.x - 1, edgeKey.y);

    private static Quaternion RotationForBodyDirection(Vector2Int bodyDirection)
    {
        foreach ((Vector2Int dir, Quaternion rotation) in WallDirections)
            if (dir == bodyDirection) return rotation;
        return Quaternion.identity;
    }

    private void RegisterWallEdges(Vector2Int dir, int fixedCoord, int start, int length)
    {
        bool alongX = dir.y != 0;
        int bodySide = alongX ? dir.y : dir.x;
        for (int variableCoord = start; variableCoord < start + length; variableCoord++)
        {
            Vector2Int cell = alongX ? new Vector2Int(variableCoord, fixedCoord) : new Vector2Int(fixedCoord, variableCoord);
            m_wallEdges[ToEdgeKey(cell, dir)] = bodySide;
        }
    }

    // 셀 경계 한 칸의 키 — z=0: 가로선 y 위의 [x, x+1] / z=1: 세로선 x 위의 [y, y+1]
    private static Vector3Int ToEdgeKey(Vector2Int cell, Vector2Int dir)
        => dir.y != 0
            ? new Vector3Int(cell.x, cell.y + Mathf.Max(dir.y, 0), 0)
            : new Vector3Int(cell.x + Mathf.Max(dir.x, 0), cell.y, 1);

    private void BakeOuterMapBorder(MapGrid grid)
    {
        if (!CanBakeWalls() || m_mapBorderPadding <= 0) return;

        int minX = -m_mapBorderPadding;
        int maxX = grid.Size.x + m_mapBorderPadding - 1;
        int minY = -m_mapBorderPadding;
        int maxY = grid.Size.y + m_mapBorderPadding - 1;

        for (int x = minX; x <= maxX; x++)
            for (int y = minY; y <= maxY; y++)
            {
                var pos = new Vector2Int(x, y);
                if (grid.InBounds(pos) && grid[pos] != MapCellType.None) continue;

                LevelModule wallModule = PickWallModule(1);
                if (wallModule != null)
                    Spawn(wallModule.gameObject, CellToWorld(pos), Quaternion.identity, m_wallRoot);
            }
    }

    private void BakeWallpaper(MapGrid grid)
    {
        if (m_wallpaperEntries == null || m_wallpaperEntries.Length == 0 || m_wallpaperRoot == null) return;

        MapGenerationResult mapResult = MapGenerationMasterRunner.LastResult;
        if (mapResult == null) return;

        foreach (var room in mapResult.Rooms)
        {
            if (m_bakeRandom.NextFloat() > m_roomWallpaperChance) continue;
            ApplyWallpaperToGroup(GetRoomAdjacentWalls(room, grid), grid);
        }

        if (m_mandatoryRef?.CorridorResult != null)
            foreach (var corridor in m_mandatoryRef.CorridorResult.Corridors)
            {
                if (m_bakeRandom.NextFloat() > m_corridorWallpaperChance) continue;
                ApplyWallpaperToGroup(GetCorridorAdjacentWalls(corridor.ExpandedCells, grid), grid);
            }

        if (m_optionalRef?.LastResult != null)
            foreach (var corridor in m_optionalRef.LastResult.Corridors)
            {
                if (m_bakeRandom.NextFloat() > m_corridorWallpaperChance) continue;
                ApplyWallpaperToGroup(GetCorridorAdjacentWalls(corridor.ExpandedCells, grid), grid);
            }
    }

    private void ApplyWallpaperToGroup(List<Vector2Int> wallCells, MapGrid grid)
    {
        foreach (var cell in wallCells)
        {
            var south = new Vector2Int(cell.x, cell.y - 1);
            if (!grid.InBounds(south)) continue;
            var southType = grid[south];
            if (southType != MapCellType.Room && southType != MapCellType.Corridor) continue;

            var left = new Vector2Int(cell.x - 1, cell.y);
            var right = new Vector2Int(cell.x + 1, cell.y);
            bool leftIsWall = grid.InBounds(left) && grid[left] == MapCellType.None;
            bool rightIsWall = grid.InBounds(right) && grid[right] == MapCellType.None;
            if (!leftIsWall && !rightIsWall) continue;

            var entry = m_wallpaperEntries[m_bakeRandom.NextInt(0, m_wallpaperEntries.Length)];
            if (entry.Prefab == null) continue;

            Vector3 world = CellToWorld(cell) + entry.Offset;
            Spawn(entry.Prefab, world, Quaternion.identity, m_wallpaperRoot);
        }
    }

    private void BakeProps(MapGrid grid)
    {
        if (m_propEntries == null || m_propEntries.Length == 0 || m_propRoot == null) return;

        MapGenerationResult mapResult = MapGenerationMasterRunner.LastResult;
        if (mapResult == null) return;

        var occupiedCells = new HashSet<Vector2Int>();
        if (m_objectiveStateControllerRef != null)
            foreach (var worldPos in m_objectiveStateControllerRef.OccupiedPositions)
                occupiedCells.Add(WorldToCell(worldPos));
        occupiedCells.UnionWith(mapResult.BlockedCells);

        foreach (var room in mapResult.Rooms)
        {
            var candidates = GetRoomInnerCells(room, occupiedCells);
            int count = Mathf.Min(m_maxPropsPerRoom, candidates.Count);

            for (int i = 0; i < count; i++)
            {
                if (candidates.Count == 0) break;

                int idx = m_bakeRandom.NextInt(0, candidates.Count);
                var chosen = candidates[idx];
                candidates.RemoveAt(idx);

                var entry = m_propEntries[m_bakeRandom.NextInt(0, m_propEntries.Length)];
                if (entry.Prefab == null) continue;

                Vector3 world = CellToWorld(chosen) + entry.Offset;
                Spawn(entry.Prefab, world, Quaternion.identity, m_propRoot);
                occupiedCells.Add(chosen);

                candidates.RemoveAll(c =>
                    Mathf.Abs(c.x - chosen.x) < m_minPropDistance &&
                    Mathf.Abs(c.y - chosen.y) < m_minPropDistance);
            }
        }
    }

    // ── 헬퍼 ───────────────────────────────────────────────

    // 같은 벽 라인 위에서 연속된 경계 셀을 구간으로 묶음. 고정축→가변축 순서로 스캔하므로 결과 순서가 결정적
    private static List<EdgeRun> CollectRuns(MapGrid grid, Vector2Int dir, System.Func<MapCellType, MapCellType, bool> isEdge)
    {
        List<EdgeRun> runs = new List<EdgeRun>();
        bool alongX = dir.y != 0;
        int fixedCount = alongX ? grid.Size.y : grid.Size.x;
        int variableCount = alongX ? grid.Size.x : grid.Size.y;

        for (int fixedCoord = 0; fixedCoord < fixedCount; fixedCoord++)
        {
            int runStart = -1;
            for (int variableCoord = 0; variableCoord <= variableCount; variableCoord++)
            {
                bool isEdgeCell = false;
                if (variableCoord < variableCount)
                {
                    Vector2Int cell = alongX ? new Vector2Int(variableCoord, fixedCoord) : new Vector2Int(fixedCoord, variableCoord);
                    isEdgeCell = isEdge(grid[cell], grid[cell + dir]);
                }

                if (isEdgeCell && runStart < 0)
                {
                    runStart = variableCoord;
                }
                else if (!isEdgeCell && runStart >= 0)
                {
                    runs.Add(new EdgeRun(fixedCoord, runStart, variableCoord - runStart));
                    runStart = -1;
                }
            }
        }

        return runs;
    }

    private static bool IsFloor(MapCellType type) => type == MapCellType.Room || type == MapCellType.Corridor;

    private static bool IsWallEdge(MapCellType self, MapCellType neighbor) => IsFloor(self) && neighbor == MapCellType.None;

    private static bool IsDoorEdge(MapCellType self, MapCellType neighbor) => self == MapCellType.Room && neighbor == MapCellType.Corridor;

    // requiredMask가 있으면 풋프린트 전체가 마스크 안에 들어가야 함
    private static bool FitsFloor(MapGrid grid, bool[,] filled, bool[,] requiredMask, int originX, int originY, int width, int depth)
    {
        for (int offsetX = 0; offsetX < width; offsetX++)
            for (int offsetY = 0; offsetY < depth; offsetY++)
            {
                Vector2Int cell = new Vector2Int(originX + offsetX, originY + offsetY);
                if (!grid.InBounds(cell) || !IsFloor(grid[cell]) || filled[cell.x, cell.y]) return false;
                if (requiredMask != null && !requiredMask[cell.x, cell.y]) return false;
            }
        return true;
    }

    // 셀 경계선 위 구간의 중심 좌표. variableCenter는 셀 단위(셀 n은 [n, n+1) 구간)
    private Vector3 EdgeSegmentToWorld(Vector2Int dir, int fixedCoord, float variableCenter)
    {
        bool alongX = dir.y != 0;
        float edgeLine = fixedCoord + 0.5f + (alongX ? dir.y : dir.x) * 0.5f;
        return alongX
            ? new Vector3(variableCenter * m_cellSize, m_groundY, edgeLine * m_cellSize)
            : new Vector3(edgeLine * m_cellSize, m_groundY, variableCenter * m_cellSize);
    }

    private bool CanBakeWalls()
        => m_bakeWalls && m_wallRoot != null && m_wallVariants != null && m_wallVariants.Length > 0;

    private LevelModule PickModule(WeightedPrefab[] variants, ModuleKind kind)
    {
        m_candidates.Clear();
        foreach (WeightedPrefab variant in variants)
            if (IsUsable(variant, kind))
                AddCandidate(variant.Prefab, false, variant.Weight);

        return TryPickCandidate(out ModuleCandidate picked) ? picked.Module : null;
    }

    private LevelModule PickWallModule(int maxWidth)
    {
        m_candidates.Clear();
        foreach (WeightedPrefab variant in m_wallVariants)
            if (IsUsable(variant, ModuleKind.Wall) && variant.Prefab.Size.x <= maxWidth)
                AddCandidate(variant.Prefab, false, variant.Weight);

        return TryPickCandidate(out ModuleCandidate picked) ? picked.Module : null;
    }

    private static bool IsUsable(WeightedPrefab variant, ModuleKind kind)
        => variant.Prefab != null && variant.Weight > 0f && variant.Prefab.Kind == kind;

    private void AddCandidate(LevelModule module, bool rotated, float weight)
        => m_candidates.Add(new ModuleCandidate { Module = module, Rotated = rotated, Weight = weight });

    private bool TryPickCandidate(out ModuleCandidate picked)
    {
        picked = default;
        float totalWeight = 0f;
        foreach (ModuleCandidate candidate in m_candidates)
            totalWeight += candidate.Weight;
        if (totalWeight <= 0f) return false;

        float roll = m_bakeRandom.NextFloat() * totalWeight;
        float cumulative = 0f;
        foreach (ModuleCandidate candidate in m_candidates)
        {
            cumulative += candidate.Weight;
            if (roll < cumulative)
            {
                picked = candidate;
                return true;
            }
        }

        picked = m_candidates[m_candidates.Count - 1];
        return true;
    }

    private void ValidateVariants()
    {
        ValidateVariantList(m_floorVariants, ModuleKind.Floor, "Floor");
        ValidateVariantList(m_ceilingVariants, ModuleKind.Floor, "Ceiling");
        ValidateVariantList(m_wallVariants, ModuleKind.Wall, "Wall");
        ValidateVariantList(m_doorFrameVariants, ModuleKind.DoorFrame, "DoorFrame");
        ValidateVariantList(m_endCapVariants, ModuleKind.EndCap, "EndCap");
    }

    private static void ValidateVariantList(WeightedPrefab[] variants, ModuleKind kind, string label)
    {
        if (variants == null || variants.Length == 0) return;

        bool hasUnitModule = false;
        foreach (WeightedPrefab variant in variants)
        {
            if (variant.Prefab == null) continue;
            if (variant.Prefab.Kind != kind)
            {
                Debug.LogWarning($"[PrefabBake] {label} 목록에 Kind={variant.Prefab.Kind} 모듈 '{variant.Prefab.name}' 포함 — 무시됨.");
                continue;
            }

            bool isUnit = variant.Prefab.Size.x == 1 && (kind != ModuleKind.Floor || variant.Prefab.Size.y == 1);
            if (variant.Weight > 0f && isUnit) hasUnitModule = true;
        }

        bool requiresUnitModule = kind == ModuleKind.Floor || kind == ModuleKind.Wall;
        if (requiresUnitModule && !hasUnitModule)
            Debug.LogWarning($"[PrefabBake] {label} 목록에 1칸 모듈이 없음 — 빈칸이 생길 수 있음.");
    }

    // 머신 간 배치 비교용 해시 누적 (호스트/클라이언트 로그 비교)
    private GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation, Transform parent)
    {
        GameObject instance = Instantiate(prefab, position, rotation, parent);

        unchecked
        {
            int hash = m_placementHash;
            hash = hash * 31 + StableHash(prefab.name);
            hash = hash * 31 + Mathf.RoundToInt(position.x * 100f);
            hash = hash * 31 + Mathf.RoundToInt(position.y * 100f);
            hash = hash * 31 + Mathf.RoundToInt(position.z * 100f);
            hash = hash * 31 + Mathf.RoundToInt(rotation.eulerAngles.y);
            m_placementHash = hash;
        }

        m_placementCount++;
        return instance;
    }

    // string.GetHashCode는 런타임마다 달라질 수 있어 FNV-1a 사용
    private static int StableHash(string text)
    {
        unchecked
        {
            int hash = FNV_OFFSET_BASIS;
            foreach (char character in text)
                hash = (hash ^ character) * FNV_PRIME;
            return hash;
        }
    }

    private Vector3 CellToWorld(Vector2Int cell, float groundY = m_groundY)
        => GridToWorld.CellCenterToWorld(cell, m_cellSize, groundY);

    private Vector2Int WorldToCell(Vector3 world)
        => GridToWorld.WorldToCell(world, m_cellSize);

    private List<Vector2Int> GetRoomAdjacentWalls(RoomData room, MapGrid grid)
    {
        var result = new List<Vector2Int>();
        var bounds = room.Bounds;
        for (int x = bounds.xMin - 1; x <= bounds.xMax; x++)
            for (int y = bounds.yMin - 1; y <= bounds.yMax; y++)
            {
                var pos = new Vector2Int(x, y);
                if (!grid.InBounds(pos)) continue;
                if (grid[pos] != MapCellType.None) continue;
                result.Add(pos);
            }
        return result;
    }

    private List<Vector2Int> GetCorridorAdjacentWalls(HashSet<Vector2Int> corridorCells, MapGrid grid)
    {
        var result = new HashSet<Vector2Int>();
        var dirs = new[] { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };
        foreach (var cell in corridorCells)
            foreach (var dir in dirs)
            {
                var neighbor = cell + dir;
                if (!grid.InBounds(neighbor)) continue;
                if (grid[neighbor] == MapCellType.None)
                    result.Add(neighbor);
            }
        return new List<Vector2Int>(result);
    }

    private List<Vector2Int> GetRoomInnerCells(RoomData room, HashSet<Vector2Int> occupied)
    {
        var result = new List<Vector2Int>();
        for (int x = room.Bounds.xMin + 1; x < room.Bounds.xMax - 1; x++)
            for (int y = room.Bounds.yMin + 1; y < room.Bounds.yMax - 1; y++)
            {
                var pos = new Vector2Int(x, y);
                if (!occupied.Contains(pos))
                    result.Add(pos);
            }
        return result;
    }

    private void ClearRoot(Transform root)
    {
        if (root == null) return;
        for (int i = root.childCount - 1; i >= 0; i--)
            DestroyImmediate(root.GetChild(i).gameObject);
    }

    [ContextMenu("Clear")]
    private void Clear()
    {
        ClearRoot(m_floorRoot);
        ClearRoot(m_wallRoot);
        ClearRoot(m_wallpaperRoot);
        ClearRoot(m_ceilingRoot);
        ClearRoot(m_propRoot);
        Debug.Log("[PrefabBake] Cleared.");
    }

    private void RemoveNavMesh()
    {
        NavMesh.RemoveAllNavMeshData();
        _navMeshDataInstance = default;
        _navMeshData = null;
    }

    private void OnDestroy()
    {
        RemoveNavMesh();
    }

}
