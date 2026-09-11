using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 점 앵커로 지어진 UI를 <b>비율(스트레치) 앵커</b>로 다시 쓴다. <b>아무것도 움직이지 않는다</b> —
/// 같은 자리를 다른 방식으로 표현할 뿐이다.
///
/// <para><b>왜 필요한가</b> — 화면들은 목업 px를 그대로 박은 점 앵커로 지어졌다. 점 앵커는 부모의
/// 한 점에 자식을 매다는 것이라, 창을 키워도 자식은 원래 크기로 좌상단에 남는다.
/// 그래서 배경만 넓어지고 내용은 안 따라와 자리가 통째로 비었다.</para>
///
/// <para><b>왜 빌더가 아니라 여기서 하는가</b> — 화면마다 배치 헬퍼가 제각각이고(<c>Place</c>·
/// <c>PlaceTL</c>·인라인 대입), 호출부가 부모 크기를 모르는 곳도 많다. 다 지어진 <b>결과 계층</b>을
/// 한 번 훑는 편이 훨씬 적은 손으로 빠짐없이 덮는다. 앞으로 새로 굽는 화면도 자동으로 포함된다.</para>
///
/// <para><b>건드리지 않는 것</b> — 레이아웃 그룹의 자식(그룹이 배치한다) · 내용맞춤(ContentSizeFitter) ·
/// 스크롤 부품 · 최상위 껍데기(암막·창 자신) · 이미 비율인 것.</para>
/// </summary>
public static class UIFractionalAnchorEditor
{
    /// <summary>목업 기준 캔버스. 루트 크기를 이걸로 잡고 아래로 풀어 내려간다.</summary>
    private const float RefW = 1920f, RefH = 1080f;

    [MenuItem("RelicFairy/UI/비율 앵커로 변환 — 구워진 팝업 전체")]
    private static void ConvertAll()
    {
        int total = 0;
        foreach (var path in AssetDatabase.FindAssets("t:Prefab", new[] { "Assets/RelicFairy/UI" }))
        {
            string p = AssetDatabase.GUIDToAssetPath(path);
            if (p.Contains("_PrefabBackup")) continue;

            var root = PrefabUtility.LoadPrefabContents(p);
            try
            {
                int n = Convert(root);
                if (n <= 0) continue;
                PrefabUtility.SaveAsPrefabAsset(root, p, out bool ok);
                if (ok) { total += n; Debug.Log($"[비율앵커] {System.IO.Path.GetFileNameWithoutExtension(p)} — {n}개 변환"); }
            }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        AssetDatabase.Refresh();
        Debug.Log($"[비율앵커] 모두 {total}개 변환");
    }

    /// <summary>계층을 훑어 점 앵커를 비율 앵커로 바꾼다. 바꾼 개수를 돌려준다.</summary>
    public static int Convert(GameObject root)
    {
        if (root == null || root.transform is not RectTransform rootRt) return 0;

        var skip = CollectScrollParts(root);
        var size = new Dictionary<RectTransform, Vector2> { [rootRt] = new Vector2(RefW, RefH) };
        int changed = 0;

        // 너비 우선 — 부모 크기가 먼저 확정돼야 자식을 풀 수 있다.
        var queue = new Queue<RectTransform>();
        queue.Enqueue(rootRt);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            var pSize  = size[parent];

            for (int i = 0; i < parent.childCount; i++)
            {
                if (parent.GetChild(i) is not RectTransform rt) continue;

                // 부모 좌표계에서의 사각형을 먼저 구한다 — 바꾸든 말든 자식이 이 값을 쓴다.
                float w = (rt.anchorMax.x - rt.anchorMin.x) * pSize.x + rt.sizeDelta.x;
                float h = (rt.anchorMax.y - rt.anchorMin.y) * pSize.y + rt.sizeDelta.y;
                // 피벗은 sizeDelta에만 곱한다(Unity: offsetMin = anchoredPosition − sizeDelta×pivot).
                float left   = rt.anchorMin.x * pSize.x + rt.anchoredPosition.x - rt.pivot.x * rt.sizeDelta.x;
                float bottom = rt.anchorMin.y * pSize.y + rt.anchoredPosition.y - rt.pivot.y * rt.sizeDelta.y;

                size[rt] = new Vector2(w, h);
                queue.Enqueue(rt);

                if (ShouldSkip(rt, parent, rootRt, pSize, skip)) continue;

                rt.anchorMin = new Vector2(left / pSize.x, bottom / pSize.y);
                rt.anchorMax = new Vector2((left + w) / pSize.x, (bottom + h) / pSize.y);
                // 피벗은 그대로 둔다 — sizeDelta·anchoredPosition이 0이면 피벗과 무관하게
                // rect가 앵커 사각형과 정확히 같아져, 기존 확대/회전 연출의 기준점이 보존된다.
                rt.sizeDelta        = Vector2.zero;
                rt.anchoredPosition = Vector2.zero;
                changed++;
            }
        }
        return changed;
    }

    // ── Private Methods ──────────────────────────────────────

    private static bool ShouldSkip(RectTransform rt, RectTransform parent, RectTransform root,
                                   Vector2 pSize, HashSet<RectTransform> scrollParts)
    {
        if (parent == root) return true;                       // 암막·창 자신 — 창은 크기를 직접 갖는다
        if (rt.anchorMin != rt.anchorMax) return true;          // 이미 비율
        if (pSize.x <= 0.001f || pSize.y <= 0.001f) return true;
        if (scrollParts.Contains(rt)) return true;              // 스크롤이 직접 움직인다
        if (rt.GetComponent<ContentSizeFitter>() != null) return true;

        // 배율 레이어는 건드리지 않는다 — <b>고정 크기가 그 좌표계의 기준</b>이라
        // 비율 앵커로 바꿔 sizeDelta를 0으로 만들면 기준이 사라지고 안의 좌표가 전부 무의미해진다.
        // (실제로 원거리 탭 레이어가 1257×599 → 0×0이 되어 내용이 통째로 어긋났다.)
        if (rt.localScale != Vector3.one) return true;
        // 레이아웃 그룹이 잡는 칸은 <b>그 아래 서브트리 전체</b>를 건너뛴다.
        // 그룹이 배치하는 자식의 rect는 베이크 시점에 아직 미확정(새 RectTransform 기본 100×100)이다.
        // 그 자식의 자손을 그 100 기준으로 비율화하면, 런타임에 부모가 실제 크기를 얻는 순간
        // 자식이 (실제폭 / 100)배로 부풀어 오른다. 한 단계만 보던 탓에 실제로
        // Txt_Condition이 행 폭의 3.00배, Txt_Question 2.80배, Holder 1.98배,
        // Divider/Art 2.80배로 굳어 화면 밖까지 뻗었다.
        for (var a = parent; a != null && a != root; a = a.parent as RectTransform)
            if (a.GetComponent<LayoutGroup>() != null) return true;

        return false;
    }

    /// <summary>스크롤의 내용·뷰포트는 스크롤이 직접 옮기므로 앵커를 굳히면 안 된다.</summary>
    private static HashSet<RectTransform> CollectScrollParts(GameObject root)
    {
        var set = new HashSet<RectTransform>();
        foreach (var sr in root.GetComponentsInChildren<ScrollRect>(true))
        {
            if (sr.content  != null) set.Add(sr.content);
            if (sr.viewport != null) set.Add(sr.viewport);
        }
        return set;
    }
}
