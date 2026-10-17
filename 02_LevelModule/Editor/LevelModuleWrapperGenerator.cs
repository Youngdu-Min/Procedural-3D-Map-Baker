using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

// BackroomsLikeAssetRe 프리팹 이름을 파싱해 LevelModule 래퍼 프리팹을 일괄 생성/갱신
// 이름 규칙(FBX 실측): 벽 = 높이x폭, 바닥 = ZxX. 원본 피벗은 이미 X·Z 중앙, 벽 하단/바닥 윗면이 Y=0
public static class LevelModuleWrapperGenerator
{
    private const string SOURCE_ROOT = "Assets/ThridParty/BackroomsLikeAssetRe/prefab";
    private const string OUTPUT_ROOT = "Assets/_Project/04_Prefabs/03_Map/LevelModules";
    private const string WRAPPER_SUFFIX = "_LM";
    private const string HOLE_TOKEN = "_Hole";
    private const string DOOR_TOKEN = "_Door";

    // 정수 크기만 매칭 — 접미사가 영문자로 시작해야 하므로 1x1_5(1.5), 3x05(0.5) 등은 제외됨
    private static readonly Regex FloorPattern = new Regex(@"^BR_Floor_(?<depth>\d)x(?<width>\d)(?:_[A-Za-z]\w*)?$");

    // 높이 3(룸 높이)인 A/B 벽만 매칭. _Door 접미사는 문 구멍이 뚫린 벽 → DoorFrame
    private static readonly Regex WallPattern = new Regex(@"^BR_Wall_(?<style>[AB])_3x(?<width>\d)(?<suffix>_[A-Za-z]\w*)?$");

    // 벽 메쉬에 끝단 면이 없어 노출된 끝을 덮는 캡 (높이 3인 기본형만, _Alt는 2.75라 제외)
    private static readonly Regex EndCapPattern = new Regex(@"^BR_Wall_(?<style>[AB])_Edge_3m$");

    [MenuItem("Tools/Level Generation/Generate BR Level Modules")]
    private static void Generate()
    {
        int savedCount = 0;
        int skippedCount = 0;
        Dictionary<string, float> halfThicknessCache = new Dictionary<string, float>();

        string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { SOURCE_ROOT + "/Floor", SOURCE_ROOT + "/Wall" });
        foreach (string guid in guids)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (source == null) continue;
            if (!TryParse(source.name, out ModuleKind kind, out Vector2Int size, out string style)) continue;

            float faceOffsetZ = kind == ModuleKind.Floor ? 0f : -GetHalfThickness(style, source, halfThicknessCache);
            if (CreateOrUpdateWrapper(source, kind, size, faceOffsetZ)) savedCount++;
            else skippedCount++;
        }

        AssetDatabase.SaveAssets();
        Debug.Log($"[LevelModuleGenerator] 생성/갱신 {savedCount}개 / 규칙 불일치로 건너뜀 {skippedCount}개 — 출력: {OUTPUT_ROOT}");
    }

    private static bool TryParse(string prefabName, out ModuleKind kind, out Vector2Int size, out string style)
    {
        style = null;

        Match floorMatch = FloorPattern.Match(prefabName);
        if (floorMatch.Success)
        {
            kind = ModuleKind.Floor;
            size = new Vector2Int(int.Parse(floorMatch.Groups["width"].Value), int.Parse(floorMatch.Groups["depth"].Value));
            return size.x > 0 && size.y > 0;
        }

        Match wallMatch = WallPattern.Match(prefabName);
        if (wallMatch.Success)
        {
            kind = wallMatch.Groups["suffix"].Value.StartsWith(DOOR_TOKEN) ? ModuleKind.DoorFrame : ModuleKind.Wall;
            size = new Vector2Int(int.Parse(wallMatch.Groups["width"].Value), LevelModule.WALL_HEIGHT);
            style = wallMatch.Groups["style"].Value;
            return size.x > 0;
        }

        Match endCapMatch = EndCapPattern.Match(prefabName);
        if (endCapMatch.Success)
        {
            kind = ModuleKind.EndCap;
            size = new Vector2Int(1, LevelModule.WALL_HEIGHT);
            style = endCapMatch.Groups["style"].Value;
            return true;
        }

        kind = default;
        size = default;
        return false;
    }

    // 같은 스타일 기본 벽(3x1)의 두께 절반 — 문·창 케이싱이 아닌 벽체 기준으로 마감면을 Z=0에 맞추기 위함
    private static float GetHalfThickness(string style, GameObject fallbackSource, Dictionary<string, float> cache)
    {
        if (cache.TryGetValue(style, out float cached)) return cached;

        GameObject reference = AssetDatabase.LoadAssetAtPath<GameObject>($"{SOURCE_ROOT}/Wall/BR_Wall_{style}_3x1.prefab");
        if (reference == null) return MeasureMaxZ(fallbackSource);

        float halfThickness = MeasureMaxZ(reference);
        cache[style] = halfThickness;
        return halfThickness;
    }

    private static float MeasureMaxZ(GameObject prefab)
    {
        MeshFilter meshFilter = prefab.GetComponentInChildren<MeshFilter>();
        return meshFilter != null && meshFilter.sharedMesh != null ? meshFilter.sharedMesh.bounds.max.z : 0f;
    }

    // 기존 래퍼는 제자리 갱신 — 씬 배열의 참조(fileID)와 사용자가 켠 AllowRotate90/BlocksMovement 유지
    private static bool CreateOrUpdateWrapper(GameObject source, ModuleKind kind, Vector2Int size, float faceOffsetZ)
    {
        string path = $"{EnsureFolder(kind)}/{source.name}{WRAPPER_SUFFIX}.prefab";
        bool exists = AssetDatabase.LoadAssetAtPath<GameObject>(path) != null;
        GameObject root = exists ? PrefabUtility.LoadPrefabContents(path) : new GameObject(source.name + WRAPPER_SUFFIX);

        try
        {
            LevelModule module = root.GetComponent<LevelModule>();
            if (module == null) module = root.AddComponent<LevelModule>();

            SerializedObject serializedModule = new SerializedObject(module);
            serializedModule.FindProperty("m_kind").enumValueIndex = (int)kind;
            serializedModule.FindProperty("m_size").vector2IntValue = size;
            if (!exists)
                serializedModule.FindProperty("m_blocksMovement").boolValue = kind == ModuleKind.Floor && source.name.Contains(HOLE_TOKEN);
            serializedModule.ApplyModifiedPropertiesWithoutUndo();

            Transform child = FindSourceInstance(root.transform, source);
            if (child == null)
            {
                GameObject instance = PrefabUtility.InstantiatePrefab(source, root.transform) as GameObject;
                if (instance == null) return false;
                child = instance.transform;
            }

            child.localPosition = new Vector3(0f, 0f, faceOffsetZ);
            child.localRotation = Quaternion.identity;
            child.localScale = Vector3.one;

            // 캡은 +X로 돌출해야 함 — 원본이 -X로 뻗으면 Y축 180° 회전 (두께가 Z 대칭이라 마감면 위치는 유지됨)
            if (kind == ModuleKind.EndCap && module.TryGetLocalMeshBounds(out Bounds capBounds) && capBounds.center.x < 0f)
                child.localRotation = Quaternion.Euler(0f, 180f, 0f);

            List<string> issues = module.GetBoundsIssues();
            if (issues.Count > 0)
            {
                Debug.LogWarning($"[LevelModuleGenerator] {source.name}: 제작 규칙 불일치로 건너뜀 — {string.Join(" / ", issues)}", source);
                return false;
            }

            PrefabUtility.SaveAsPrefabAsset(root, path);
            return true;
        }
        finally
        {
            if (exists) PrefabUtility.UnloadPrefabContents(root);
            else Object.DestroyImmediate(root);
        }
    }

    private static Transform FindSourceInstance(Transform root, GameObject source)
    {
        foreach (Transform child in root)
            if (PrefabUtility.GetCorrespondingObjectFromSource(child.gameObject) == source)
                return child;
        return null;
    }

    private static string EnsureFolder(ModuleKind kind)
    {
        string folder = $"{OUTPUT_ROOT}/{kind}";
        if (!AssetDatabase.IsValidFolder(folder)) CreateFolderRecursive(folder);
        return folder;
    }

    private static void CreateFolderRecursive(string folder)
    {
        string parent = System.IO.Path.GetDirectoryName(folder).Replace('\\', '/');
        if (!AssetDatabase.IsValidFolder(parent)) CreateFolderRecursive(parent);
        AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(folder));
    }
}
