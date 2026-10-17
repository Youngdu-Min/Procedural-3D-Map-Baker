using System.Collections.Generic;
using UnityEngine;

// 조합 프리팹 루트에 부착. 피벗 = 풋프린트 중앙(Floor: 윗면 Y=0 / Wall·DoorFrame: 하단, 셀 경계선 위), 마감면 = 로컬 +Z
// EndCap: 피벗 = 벽 끝점(셀 경계선 위), 벽은 -X 쪽에 있고 캡은 +X로 돌출
public class LevelModule : MonoBehaviour
{
    public const int WALL_HEIGHT = 3;
    private const float SIZE_TOLERANCE = 0.05f;
    // 크랙 조각·파편·문틀 케이싱 등 의도된 돌출 허용치
    private const float OVERHANG_TOLERANCE = 0.2f;

    [Tooltip("모듈 종류")]
    [SerializeField] private ModuleKind m_kind = ModuleKind.Floor;

    [Tooltip("셀 단위 크기. Floor: (가로 X, 세로 Z) / Wall·DoorFrame: (폭, 높이)")]
    [SerializeField] private Vector2Int m_size = Vector2Int.one;

    [Tooltip("Floor 전용: 90도 회전 배치 허용 (가로/세로 스왑)")]
    [SerializeField] private bool m_allowRotate90;

    [Tooltip("Floor 전용: 통행 불가(구멍 등). 방 안쪽 안전 영역에만 배치되고 스폰 후보에서 제외됨")]
    [SerializeField] private bool m_blocksMovement;

    public ModuleKind Kind => m_kind;
    public Vector2Int Size => m_size;
    public bool AllowRotate90 => m_allowRotate90;
    public bool BlocksMovement => m_blocksMovement;

#if UNITY_EDITOR
    private void OnValidate()
    {
        // 씬에 배치된 인스턴스는 검사하지 않음 (Bake 시 대량 경고 방지) — 프리팹 에셋/프리팹 모드에서만 검사
        bool isPrefabAsset = UnityEditor.PrefabUtility.IsPartOfPrefabAsset(this);
        bool isInPrefabStage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(gameObject) != null;
        if (!isPrefabAsset && !isInPrefabStage) return;

        if (m_size.x < 1 || m_size.y < 1)
            Debug.LogWarning($"[LevelModule] {name}: Size는 1 이상이어야 합니다.", this);

        if (transform.localScale != Vector3.one)
            Debug.LogWarning($"[LevelModule] {name}: 루트 scale은 (1,1,1)이어야 합니다.", this);

        if (m_kind != ModuleKind.Floor && m_size.y != WALL_HEIGHT)
            Debug.LogWarning($"[LevelModule] {name}: {m_kind} 높이(Size.y)는 {WALL_HEIGHT}이어야 합니다.", this);

        foreach (string issue in GetBoundsIssues())
            Debug.LogWarning($"[LevelModule] {name}: {issue}", this);
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;

        if (m_kind == ModuleKind.Floor)
        {
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(m_size.x, 0.02f, m_size.y));
            return;
        }

        float halfHeight = m_size.y * 0.5f;
        Gizmos.DrawWireCube(new Vector3(0f, halfHeight, 0f), new Vector3(m_size.x, m_size.y, 0.02f));
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(new Vector3(0f, halfHeight, 0f), new Vector3(0f, halfHeight, 0.5f));
    }

    public bool TryGetLocalMeshBounds(out Bounds bounds)
    {
        bounds = default;
        bool hasBounds = false;
        Matrix4x4 rootInverse = transform.worldToLocalMatrix;

        foreach (MeshFilter meshFilter in GetComponentsInChildren<MeshFilter>(true))
        {
            Mesh mesh = meshFilter.sharedMesh;
            if (mesh == null) continue;

            Matrix4x4 toRoot = rootInverse * meshFilter.transform.localToWorldMatrix;
            Bounds meshBounds = mesh.bounds;
            Vector3 min = meshBounds.min;
            Vector3 max = meshBounds.max;

            for (int i = 0; i < 8; i++)
            {
                Vector3 corner = new Vector3(
                    (i & 1) == 0 ? min.x : max.x,
                    (i & 2) == 0 ? min.y : max.y,
                    (i & 4) == 0 ? min.z : max.z);
                Vector3 local = toRoot.MultiplyPoint3x4(corner);

                if (!hasBounds)
                {
                    bounds = new Bounds(local, Vector3.zero);
                    hasBounds = true;
                }
                else
                {
                    bounds.Encapsulate(local);
                }
            }
        }

        return hasBounds;
    }

    // 제작 규칙 위반 목록 (비어 있으면 정상). 실측값을 포함해 원인 파악이 쉽도록 함
    public List<string> GetBoundsIssues()
    {
        List<string> issues = new List<string>();
        if (!TryGetLocalMeshBounds(out Bounds bounds))
        {
            issues.Add("자식 메쉬 없음");
            return issues;
        }

        if (m_kind == ModuleKind.Floor)
        {
            CheckSpan(issues, "가로(X)", bounds.min.x, bounds.max.x, m_size.x);
            CheckSpan(issues, "세로(Z)", bounds.min.z, bounds.max.z, m_size.y);
            CheckEdge(issues, "윗면(Y=0)", bounds.max.y, 0f, true);
            return issues;
        }

        if (m_kind == ModuleKind.EndCap)
        {
            CheckEdge(issues, "벽 끝점(X=0, +X로 돌출)", bounds.min.x, 0f, false);
            CheckEdge(issues, "하단(Y=0)", bounds.min.y, 0f, false);
            CheckEdge(issues, "높이(Y)", bounds.max.y, m_size.y, true);
            CheckEdge(issues, "마감면(Z=0, 두께는 -Z 방향)", bounds.max.z, 0f, true);
            return issues;
        }

        CheckSpan(issues, "폭(X)", bounds.min.x, bounds.max.x, m_size.x);
        CheckEdge(issues, "하단(Y=0)", bounds.min.y, 0f, false);
        CheckEdge(issues, "높이(Y)", bounds.max.y, m_size.y, true);

        // 문틀은 양쪽으로 케이싱이 돌출되어 두께 방향 검사 제외
        if (m_kind == ModuleKind.Wall)
            CheckEdge(issues, "마감면(Z=0, 두께는 -Z 방향)", bounds.max.z, 0f, true);

        return issues;
    }

    // 중앙 피벗 기준으로 한쪽 끝이 맞아야 하며, 반대쪽 돌출은 OVERHANG_TOLERANCE까지 허용
    private static void CheckSpan(List<string> issues, string label, float min, float max, int expected)
    {
        float half = expected * 0.5f;
        bool edgeAligned = Mathf.Abs(min + half) <= SIZE_TOLERANCE || Mathf.Abs(max - half) <= SIZE_TOLERANCE;
        float size = max - min;
        bool sizeInRange = size >= expected - SIZE_TOLERANCE && size <= expected + OVERHANG_TOLERANCE;
        if (!edgeAligned || !sizeInRange)
            issues.Add($"{label} 기대 {-half:0.##}~{half:0.##}, 실측 {min:0.###}~{max:0.###}");
    }

    private static void CheckEdge(List<string> issues, string label, float actual, float expected, bool allowOverhang)
    {
        float upperLimit = expected + (allowOverhang ? OVERHANG_TOLERANCE : SIZE_TOLERANCE);
        if (actual < expected - SIZE_TOLERANCE || actual > upperLimit)
            issues.Add($"{label} 기대 {expected:0.##}, 실측 {actual:0.###}");
    }
#endif
}
