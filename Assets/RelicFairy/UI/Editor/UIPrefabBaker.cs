using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>
/// 코드로 지어진 UI 화면을 <b>실제 프리팹으로 굽는다</b>. 프리팹화 이행의 유일한 입구다.
///
/// <para><b>왜 플레이 모드인가</b> — 화면을 짓는 <c>BuildLayout()</c>류는 스킨(Addressable)·Managers·
/// 런 상태를 전제한다. 에디터에서 반사로 억지로 호출하면 아트가 빠진 반쪽 계층이 구워진다.
/// 실제로 떠 있는 화면을 그대로 뜨면 <b>보이는 것이 곧 구워지는 것</b>이라 어긋날 여지가 없다.</para>
///
/// <para><b>씬을 건드리지 않는다</b> — 프로젝트 규칙(에디터 스크립트로 씬 수정 금지)을 지킨다.
/// 뜨는 대상은 이미 떠 있는 인스턴스이고, 복제본은 런타임 오브젝트로 만들어 저장 직후 파괴한다.
/// 플레이 모드의 런타임 오브젝트는 씬 파일에 남지 않는다.</para>
///
/// <para><b>덮어쓰기 전에 반드시 백업</b>한다 — <c>_PrefabBackup/</c>에 시각을 붙여 복사한다.
/// 굽기가 잘못돼도 그 파일을 되돌리면 원래 껍데기로 돌아간다.</para>
///
/// ⚠️ <b>Addressables Play Mode Script를 「Use Asset Database (fastest)」로 두고 구울 것.</b>
///   번들에서 로드된 스프라이트는 원본 에셋이 아니라 번들 사본이라, 그대로 구우면
///   프리팹의 스프라이트 참조가 빈칸으로 저장된다.
/// </summary>
public sealed class UIPrefabBaker : EditorWindow
{
    // ── Constants ────────────────────────────────────────────
    private const string BackupFolder = "Assets/RelicFairy/UI/_PrefabBackup";

    // ── Private ──────────────────────────────────────────────
    private Vector2 _scroll;
    private readonly List<Target> _targets = new();

    private struct Target
    {
        public GameObject Instance;
        public string     PrefabPath;
        public int        PrefabObjects;   // 프리팹이 현재 들고 있는 오브젝트 수
        public int        LiveObjects;     // 화면에 떠 있는 오브젝트 수
    }

    // ── Menu ─────────────────────────────────────────────────
    [MenuItem("RelicFairy/UI/Prefab Baker")]
    private static void Open() => GetWindow<UIPrefabBaker>("UI Prefab Baker");

    // ── Lifecycle ────────────────────────────────────────────
    private void OnEnable() => Rescan();

    private void OnGUI()
    {
        EditorGUILayout.HelpBox(
            "코드로 지어진 UI를 실제 프리팹으로 굽는다.\n\n" +
            "1. 플레이 모드로 들어가 대상 화면을 연다\n" +
            "2. [다시 훑기] → 목록에서 [굽기]\n" +
            "3. 플레이를 끝내고 프리팹을 열어 인스펙터로 조정한다\n\n" +
            "⚠ Addressables Play Mode Script = 「Use Asset Database (fastest)」 여야 한다.\n" +
            "   번들 모드로 구우면 스프라이트 참조가 빈칸으로 저장된다.",
            MessageType.Info);

        using (new EditorGUI.DisabledScope(!Application.isPlaying))
        {
            if (GUILayout.Button("다시 훑기", GUILayout.Height(26))) Rescan();
        }

        if (!Application.isPlaying)
        {
            EditorGUILayout.HelpBox("플레이 모드가 아니다 — 화면이 아직 지어지지 않았다.", MessageType.Warning);
            return;
        }

        if (_targets.Count == 0)
        {
            EditorGUILayout.HelpBox("열려 있는 UI 화면이 없다. 대상 화면을 연 뒤 [다시 훑기].", MessageType.None);
            return;
        }

        EditorGUILayout.Space(6);
        _scroll = EditorGUILayout.BeginScrollView(_scroll);

        for (int i = 0; i < _targets.Count; i++)
        {
            var t = _targets[i];
            if (t.Instance == null) continue;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.LabelField(t.Instance.name, EditorStyles.boldLabel);
                EditorGUILayout.LabelField($"프리팹 {t.PrefabObjects}개  →  화면 {t.LiveObjects}개",
                                           EditorStyles.miniLabel);

                if (string.IsNullOrEmpty(t.PrefabPath))
                {
                    EditorGUILayout.HelpBox("대응 프리팹을 못 찾았다 — 이름이 같은 프리팹이 있어야 한다.",
                                            MessageType.Warning);
                    continue;
                }

                EditorGUILayout.LabelField(t.PrefabPath, EditorStyles.miniLabel);

                if (t.PrefabObjects >= t.LiveObjects)
                    EditorGUILayout.HelpBox("이미 구워진 것으로 보인다(프리팹이 화면만큼 갖고 있다).",
                                            MessageType.None);

                if (GUILayout.Button("굽기 (백업 후 덮어쓰기)", GUILayout.Height(24)))
                    Bake(t);
            }
            EditorGUILayout.Space(4);
        }

        EditorGUILayout.EndScrollView();
    }

    // ── Private Methods ──────────────────────────────────────

    /// <summary>떠 있는 UI 루트를 모은다. 이름이 같은 프리팹을 짝지어 굽기 대상으로 삼는다.</summary>
    private void Rescan()
    {
        _targets.Clear();
        if (!Application.isPlaying) return;

        foreach (var popup in FindObjectsByType<UI_Base>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            var go = popup.gameObject;

            // 자식 UI_Base(카드·행 등)는 화면이 아니다 — 루트만 굽는다.
            if (go.GetComponentInParent<UI_Base>() != popup) continue;

            _targets.Add(new Target
            {
                Instance      = go,
                PrefabPath    = FindPrefabPath(go.name),
                PrefabObjects = CountPrefabObjects(FindPrefabPath(go.name)),
                LiveObjects   = go.GetComponentsInChildren<Transform>(true).Length,
            });
        }

        _targets.Sort((a, b) => string.CompareOrdinal(a.Instance.name, b.Instance.name));
    }

    /// <summary>런타임 이름에 붙는 "(Clone)"을 떼고 같은 이름의 프리팹을 찾는다.</summary>
    private static string FindPrefabPath(string instanceName)
    {
        string name = instanceName.Replace("(Clone)", "").Trim();
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:Prefab"))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (System.IO.Path.GetFileNameWithoutExtension(path) == name) return path;
        }
        return null;
    }

    private static int CountPrefabObjects(string path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        var asset = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        return asset != null ? asset.GetComponentsInChildren<Transform>(true).Length : 0;
    }

    private static void Bake(Target t)
    {
        if (t.Instance == null || string.IsNullOrEmpty(t.PrefabPath)) return;

        if (!Backup(t.PrefabPath))
        {
            Debug.LogError($"[UIPrefabBaker] 백업 실패 — 굽지 않는다: {t.PrefabPath}");
            return;
        }

        // 프리팹 인스턴스를 그대로 저장하면 "연결된 인스턴스"라 거부되거나 원본을 물고 늘어진다.
        // 순수 복제본을 떠서 저장하고 즉시 버린다(런타임 오브젝트라 씬에 남지 않는다).
        var clone = Instantiate(t.Instance);
        clone.name = t.Instance.name.Replace("(Clone)", "").Trim();
        clone.SetActive(true);

        try
        {
            PrefabUtility.SaveAsPrefabAsset(clone, t.PrefabPath, out bool ok);
            if (ok) Debug.Log($"[UIPrefabBaker] 구움: {t.PrefabPath}  ({t.LiveObjects}개 오브젝트)");
            else    Debug.LogError($"[UIPrefabBaker] 저장 실패: {t.PrefabPath}");
        }
        finally
        {
            DestroyImmediate(clone);
        }

        AssetDatabase.Refresh();
    }

    /// <summary>덮어쓰기 전 원본 복사. 되돌릴 길이 없으면 굽지 않는다.</summary>
    private static bool Backup(string path)
    {
        if (!AssetDatabase.IsValidFolder(BackupFolder))
        {
            string parent = System.IO.Path.GetDirectoryName(BackupFolder).Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, System.IO.Path.GetFileName(BackupFolder));
        }

        string name  = System.IO.Path.GetFileNameWithoutExtension(path);
        string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        return AssetDatabase.CopyAsset(path, $"{BackupFolder}/{name}_{stamp}.prefab");
    }
}
