using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 보스방 클리어 시 등장하는 챕터 전환 게이트.
/// 등장 연출(소용돌이가 번지며 열리고 글자가 떠오름) 후, 플레이어가 통과하면 다음 챕터로 전환(AdvanceChapter).
/// 보스 보상과는 별개 — 보상은 ClearRewardTrigger가 담당하고, 이 게이트는 "전환"만 책임진다.
/// 이벤트 기반: GameRunBootstrapper가 GameRunSession.OnBossRoomCleared 구독 후 Spawn을 호출한다(보스 코드 무수정).
/// 비주얼은 런 공용 이펙트 목록의 포탈 칸(<see cref="RunFxSlot.Portal"/>). 목록이 없으면 옛 발광 판으로 대신한다 —
/// 하얀 네모 판으로 보였다(10-01 f5 전주기 시뮬)지만, 길이 안 보이는 보스방은 런이 막힌 것과 같다.
/// </summary>
public sealed class ChapterGate : MonoBehaviour
{
    private const float GateWidth      = 3.5f;
    private const float GateHeight     = 4f;
    private const float GateDepth      = 1.5f;
    private const float AppearDuration = 0.9f;   // 소용돌이가 번지는 시간 — 순간 등장 금지
    private const float GlowIntensity  = 2.2f;   // 발광 판(대체)만
    private const float PortalScale    = 1f;     // 목록 칸 배율에 곱한다
    private const float PortalYaw      = 0f;     // 이펙트 정면 축 보정(후보 비교에서 정한다)

    private static readonly Color GateColor = new Color(0.55f, 0.85f, 1f, 1f); // 다음 챕터 = 푸른빛

    private bool       _triggered;
    private GameObject _portal;

    public static void Spawn(Vector3 groundPos)
    {
        var go = new GameObject("@ChapterGate");
        go.transform.position = groundPos;
        go.AddComponent<ChapterGate>().Build();
    }

    private void OnDestroy() => RunFx.Stop(ref _portal);

    private void Build()
    {
        FaceCamera();

        var col = gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size      = new Vector3(GateWidth, GateHeight, GateDepth);
        col.center    = new Vector3(0f, GateHeight * 0.5f, 0f);

        var label = CreateLabel(); // "다음 챕터" 월드 라벨

        AppearAsync(label, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>게이트 면이 카메라를 보게 수평으로 돈다 — 소용돌이가 옆으로 서서 선 하나로 보이지 않게.</summary>
    private void FaceCamera()
    {
        var cam = Camera.main;
        if (cam == null) return;
        Vector3 toCam = -cam.transform.forward;
        toCam.y = 0f;
        if (toCam.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(toCam.normalized, Vector3.up);
    }

    /// <summary>게이트 상단에 "다음 챕터" 월드 스페이스 라벨을 띄운다(카메라 향해 빌보드, 1회 정렬). 처음엔 투명 — 소용돌이와 함께 떠오른다.</summary>
    private CanvasGroup CreateLabel()
    {
        var go = new GameObject("Label");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = new Vector3(0f, GateHeight + 0.6f, 0f);

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        if (Camera.main != null) go.transform.rotation = Camera.main.transform.rotation;

        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta  = new Vector2(320f, 90f);
        rt.localScale = Vector3.one * 0.02f;

        var group = go.AddComponent<CanvasGroup>();
        group.alpha = 0f;

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = "다음 챕터";
        tmp.fontSize  = 48f;
        tmp.fontStyle = FontStyles.Normal;   // 09-27: 가짜 굵게 해제(기본 폰트가 이미 굵다)
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = GateColor;
        TMPOutlineHelper.ApplySoftShadow(tmp);
        return group;
    }

    private async UniTaskVoid AppearAsync(CanvasGroup label, CancellationToken ct)
    {
        try
        {
            try { await RunFx.LoadAsync(); }
            catch (Exception e) { Debug.LogWarning($"[ChapterGate] 이펙트 목록 로드 예외 — 발광 판으로 대신한다: {e.Message}"); }
            if (ct.IsCancellationRequested) return;

            Material glow = null;
            Vector3 center = transform.position + Vector3.up * (GateHeight * 0.5f);
            _portal = RunFx.PlayLoop(RunFxSlot.Portal, center, transform.rotation * Quaternion.Euler(0f, PortalYaw, 0f), PortalScale, Color.clear);
            Transform visual = _portal != null ? _portal.transform : CreateFallbackQuad(out glow);
            Vector3 to = _portal != null ? visual.localScale : new Vector3(GateWidth, GateHeight, 1f);

            for (float t = 0f; t < AppearDuration; t += Time.unscaledDeltaTime)
            {
                float u = 1f - t / AppearDuration;
                float k = 1f - u * u;   // 빨리 번지고 천천히 자리 잡는다
                if (visual != null) visual.localScale = to * k;
                if (glow != null) glow.SetColor("_EmissionColor", GateColor * (k * GlowIntensity));
                if (label != null) label.alpha = k;
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (visual != null) visual.localScale = to;
            if (glow != null) glow.SetColor("_EmissionColor", GateColor * (GlowIntensity * 0.6f)); // 잔광
            if (label != null) label.alpha = 1f;
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>이펙트 목록이 없을 때의 대체 — URP-safe 런타임 발광 판.</summary>
    private Transform CreateFallbackQuad(out Material glow)
    {
        Debug.LogWarning("[ChapterGate] 포탈 이펙트 칸이 없다 — 발광 판으로 대신한다");
        var vis = GameObject.CreatePrimitive(PrimitiveType.Quad);
        vis.name = "Visual";
        vis.transform.SetParent(transform, false);
        vis.transform.localPosition = new Vector3(0f, GateHeight * 0.5f, 0f);
        vis.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);   // 판 앞면(-Z)이 카메라 쪽(게이트 정면)을 보게
        vis.transform.localScale    = Vector3.zero;
        if (vis.TryGetComponent<Collider>(out var vc)) Destroy(vc);

        var rend = vis.GetComponent<Renderer>();
        RuntimePrimitiveMaterial.Apply(rend, GateColor);
        glow = rend.material;
        glow.EnableKeyword("_EMISSION");
        return vis.transform;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _triggered = true;

        // 퀘스트: 챕터 게이트 입장 보고 (target='*')
        QuestEvents.Report("Gate", gameObject.name);

        GameRunBootstrapper.Instance?.AdvanceChapter();
    }
}
