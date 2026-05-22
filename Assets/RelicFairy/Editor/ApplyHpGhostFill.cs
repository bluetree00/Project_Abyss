using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

public static class ApplyHpGhostFill
{
    private const string PrefabPath = "Assets/Abyss/UI/RootUI/@UIRoot.prefab";
    private const string GhostName  = "GhostFill";

    [MenuItem("Tools/Abyss/Apply HP Ghost Fill")]
    public static void Execute()
    {
        var root = PrefabUtility.LoadPrefabContents(PrefabPath);
        if (root == null) { Debug.LogError("[ApplyHpGhostFill] 프리팹 로드 실패"); return; }

        try
        {
            // HUD_Hp 재귀 탐색
            var hudHp = FindDeep(root.transform, "HUD_Hp");
            if (hudHp == null) { Debug.LogError("[ApplyHpGhostFill] HUD_Hp 없음"); return; }

            var fillArea = hudHp.Find("Fill Area");
            if (fillArea == null) { Debug.LogError("[ApplyHpGhostFill] Fill Area 없음"); return; }

            // 기존 GhostFill 제거 (재실행 안전)
            var existing = fillArea.Find(GhostName);
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            // Fill Image 참조 (스프라이트 복사)
            var fillTf  = fillArea.Find("Fill");
            var fillImg = fillTf != null ? fillTf.GetComponent<Image>() : null;

            // GhostFill 생성
            var ghostGo = new GameObject(GhostName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            ghostGo.transform.SetParent(fillArea, false);
            ghostGo.transform.SetSiblingIndex(0); // Fill 뒤에 렌더

            var rt = ghostGo.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot     = new Vector2(0f, 0.5f);

            var img = ghostGo.GetComponent<Image>();
            img.type       = Image.Type.Filled;
            img.fillMethod = Image.FillMethod.Horizontal;
            img.fillOrigin = 0;
            img.fillAmount = 1f;
            img.color      = new Color(1f, 0.62f, 0.10f, 0.85f); // 주황
            if (fillImg != null && fillImg.sprite != null)
                img.sprite = fillImg.sprite;

            Debug.Log($"[ApplyHpGhostFill] GhostFill 생성 완료 (parent: {fillArea.name})");

            // CombatPanelView에 연결 (전체 계층에서 검색)
            var combatView = root.GetComponentInChildren<CombatPanelView>(true);
            if (combatView != null)
            {
                var so   = new SerializedObject(combatView);
                var prop = so.FindProperty("hpGhostFillImage");
                if (prop != null)
                {
                    prop.objectReferenceValue = img;
                    so.ApplyModifiedProperties();
                    Debug.Log("[ApplyHpGhostFill] CombatPanelView.hpGhostFillImage 연결 완료");
                }
                else
                {
                    Debug.LogWarning("[ApplyHpGhostFill] hpGhostFillImage 프로퍼티 없음 — Inspector 수동 연결 필요");
                }
            }
            else
            {
                Debug.LogWarning("[ApplyHpGhostFill] CombatPanelView 없음 — Inspector 수동 연결 필요");
            }

            PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Debug.Log("[ApplyHpGhostFill] 프리팹 저장 완료");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    static Transform FindDeep(Transform t, string name)
    {
        if (t.name == name) return t;
        foreach (Transform c in t)
        {
            var r = FindDeep(c, name);
            if (r != null) return r;
        }
        return null;
    }
}
