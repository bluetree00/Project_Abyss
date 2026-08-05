using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;

/// <summary>
/// 보스방 클리어 시 등장하는 챕터 전환 게이트.
/// 등장 연출(스케일인 + 글로우) 후, 플레이어가 통과하면 다음 챕터로 전환(AdvanceChapter).
/// 보스 보상과는 별개 — 보상은 ClearRewardTrigger가 담당하고, 이 게이트는 "전환"만 책임진다.
/// 이벤트 기반: GameRunBootstrapper가 GameRunSession.OnBossRoomCleared 구독 후 Spawn을 호출한다(보스 코드 무수정).
/// 비주얼은 URP-safe 런타임 포털 — 정식 VFX 프리팹으로 교체 가능(에셋 임포트/Addressable 등록 후).
/// </summary>
public sealed class ChapterGate : MonoBehaviour
{
    private const float GateWidth      = 3.5f;
    private const float GateHeight     = 4f;
    private const float GateDepth      = 1.5f;
    private const float AppearDuration = 0.6f;
    private const float GlowIntensity  = 2.2f;

    private static readonly Color GateColor = new Color(0.55f, 0.85f, 1f, 1f); // 다음 챕터 = 푸른빛

    private bool _triggered;

    public static void Spawn(Vector3 groundPos)
    {
        var go = new GameObject("@ChapterGate");
        go.transform.position = groundPos;
        go.AddComponent<ChapterGate>().Build();
    }

    private void Build()
    {
        var col = gameObject.AddComponent<BoxCollider>();
        col.isTrigger = true;
        col.size      = new Vector3(GateWidth, GateHeight, GateDepth);
        col.center    = new Vector3(0f, GateHeight * 0.5f, 0f);

        var vis = GameObject.CreatePrimitive(PrimitiveType.Quad);
        vis.name = "Visual";
        vis.transform.SetParent(transform, false);
        vis.transform.localPosition = new Vector3(0f, GateHeight * 0.5f, 0f);
        if (vis.TryGetComponent<Collider>(out var vc)) Destroy(vc);

        var rend = vis.GetComponent<Renderer>();
        RuntimePrimitiveMaterial.Apply(rend, GateColor);
        rend.material.EnableKeyword("_EMISSION"); // 포털 글로우 — AppearAsync가 펄스인

        CreateLabel(); // "다음 챕터" 월드 라벨

        AppearAsync(vis.transform, rend, this.GetCancellationTokenOnDestroy()).Forget();
    }

    /// <summary>게이트 상단에 "다음 챕터" 월드 스페이스 라벨을 띄운다(카메라 향해 빌보드, 1회 정렬).</summary>
    private void CreateLabel()
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

        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.text      = "다음 챕터";
        tmp.fontSize  = 48f;
        tmp.fontStyle = FontStyles.Bold;
        tmp.alignment = TextAlignmentOptions.Center;
        tmp.color     = GateColor;
    }

    private async UniTaskVoid AppearAsync(Transform visual, Renderer rend, CancellationToken ct)
    {
        var to = new Vector3(GateWidth, GateHeight, 1f);
        var mat = rend != null ? rend.material : null;
        float t = 0f;
        try
        {
            while (t < AppearDuration)
            {
                t += Time.unscaledDeltaTime;
                float k = Mathf.Clamp01(t / AppearDuration);
                if (visual != null) visual.localScale = Vector3.Lerp(Vector3.zero, to, k);
                if (mat != null) mat.SetColor("_EmissionColor", GateColor * (k * GlowIntensity));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (visual != null) visual.localScale = to;
            if (mat != null) mat.SetColor("_EmissionColor", GateColor * (GlowIntensity * 0.6f)); // 잔광
        }
        catch (OperationCanceledException) { }
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
