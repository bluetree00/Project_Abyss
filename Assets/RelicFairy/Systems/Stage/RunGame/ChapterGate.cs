using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 보스방 클리어 시 등장하는 챕터 전환 게이트.
/// 등장 연출(스케일인) 후, 플레이어가 통과하면 다음 챕터로 전환(AdvanceChapter).
/// 보스 보상과는 별개 — 보상은 ClearRewardTrigger가 담당하고, 이 게이트는 "전환"만 책임진다.
/// 이벤트 기반: GameRunBootstrapper가 GameRunSession.OnBossRoomCleared 구독 후 Spawn을 호출한다(보스 코드 무수정).
/// 비주얼은 URP-safe 임시 패널 — 추후 프리팹/연출로 교체 가능.
/// </summary>
public sealed class ChapterGate : MonoBehaviour
{
    private const float GateWidth      = 3.5f;
    private const float GateHeight     = 4f;
    private const float GateDepth      = 1.5f;
    private const float AppearDuration = 0.6f;

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
        RuntimePrimitiveMaterial.Apply(vis.GetComponent<Renderer>(), GateColor);

        AppearAsync(vis.transform, this.GetCancellationTokenOnDestroy()).Forget();
    }

    private async UniTaskVoid AppearAsync(Transform visual, CancellationToken ct)
    {
        var to = new Vector3(GateWidth, GateHeight, 1f);
        float t = 0f;
        try
        {
            while (t < AppearDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / AppearDuration);
                if (visual != null) visual.localScale = Vector3.Lerp(Vector3.zero, to, k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (visual != null) visual.localScale = to;
        }
        catch (OperationCanceledException) { }
    }

    private void OnTriggerEnter(Collider other)
    {
        if (_triggered) return;
        if (other.GetComponentInParent<PlayerController>() == null) return;

        _triggered = true;
        GameRunBootstrapper.Instance?.AdvanceChapter();
    }
}
