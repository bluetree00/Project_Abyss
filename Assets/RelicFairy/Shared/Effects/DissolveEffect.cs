using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 디졸브 셰이더 기반 등장/퇴장 연출. Addressables 기반 머티리얼 로드, UniTask 구동.
/// </summary>
public static class DissolveEffect
{
    private const string MaterialKey = "DissolveMaterial";

    private static readonly int DissolveID  = Shader.PropertyToID("_Dissolve");
    private static readonly int EdgeColorID = Shader.PropertyToID("_EdgeColor");
    private static readonly int EdgeWidthID = Shader.PropertyToID("_EdgeWidth");
    private static readonly int BaseColorID = Shader.PropertyToID("_BaseColor");
    private static readonly int BaseMapID   = Shader.PropertyToID("_BaseMap");

    private static readonly string[] FallbackColorProps =
        { "_Color01", "_Color", "_MainColor", "_TintColor", "_AlbedoColor" };

    private const float EdgeFadePortion = 0.25f;
    private const float MaxEdgeWidth    = 0.12f;

    // ─────────────────── 공개 API ───────────────────

    /// <summary>디졸브로 등장 (소멸 → 완전 등장 후 원본 복원).
    /// activationToken: 풀 반환 시 취소되는 토큰 (MonsterBase.ActivationToken). 전달 시 풀 반환 후
    /// 남은 복원 태스크가 레이스를 방지한다. edgeColor 미지정 시 기본 고정색 사용. 완료 시 onComplete 호출.</summary>
    public static void PlayAppear(
        GameObject target,
        float duration = 0.5f,
        Action onComplete = null,
        CancellationToken activationToken = default,
        Color? edgeColor = null)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        DissolveInAsync(target, duration, target.GetCancellationTokenOnDestroy(), activationToken, onComplete, edgeColor).Forget();
    }

    /// <summary>디졸브로 등장. await 가능.</summary>
    public static async UniTask PlayAppearAsync(
        GameObject target, float duration = 0.5f, CancellationToken ct = default,
        Color? edgeColor = null)
    {
        if (target == null) return;
        await DissolveInAsync(target, duration, target.GetCancellationTokenOnDestroy(), ct, null, edgeColor);
    }

    /// <summary>디졸브로 퇴장 (완전 등장 → 소멸). 완료 시 onComplete 호출.</summary>
    public static void PlayDisappear(GameObject target, float duration = 0.8f, Action onComplete = null)
    {
        if (target == null) { onComplete?.Invoke(); return; }
        DissolveOutAsync(target, duration, target.GetCancellationTokenOnDestroy(), onComplete).Forget();
    }

    /// <summary>디졸브로 퇴장. await 가능.</summary>
    public static async UniTask PlayDisappearAsync(
        GameObject target, float duration = 0.8f, CancellationToken ct = default)
    {
        if (target == null) return;
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(
            target.GetCancellationTokenOnDestroy(), ct);
        await DissolveOutAsync(target, duration, cts.Token, null);
    }

    /// <summary>DissolveMaterial을 Addressables에서 미리 로드해 캐시를 워밍업. DissolveEntrance 선로드용.</summary>
    public static async UniTask WarmupAsync(CancellationToken ct = default)
    {
        await Managers.AddressableManager.TryLoadAssetAsync<Material>(MaterialKey);
    }

    /// <summary>타일 목록을 staggerInterval 간격으로 순차 디졸브 등장. 전체 완료까지 await.</summary>
    public static async UniTask PlaySequentialAsync(
        IList<GameObject> tiles,
        float staggerInterval = 0.05f,
        float duration        = 0.5f,
        CancellationToken ct  = default)
    {
        if (tiles == null || tiles.Count == 0) return;

        var pending = new List<UniTask>(tiles.Count);
        for (int i = 0; i < tiles.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (tiles[i] != null)
                pending.Add(PlayAppearAsync(tiles[i], duration, ct));

            if (i < tiles.Count - 1)
                await UniTask.Delay(TimeSpan.FromSeconds(staggerInterval), cancellationToken: ct);
        }
        await UniTask.WhenAll(pending);
    }

    // ─────────────────── 내부 구현 ───────────────────

    private static readonly Color DefaultEdgeColor = new Color(0f, 2.4f, 3f, 1f);

    private static async UniTask DissolveInAsync(
        GameObject target, float duration,
        CancellationToken destroyCt, CancellationToken activationToken,
        Action onComplete, Color? edgeColor = null)
    {
        CancellationTokenSource linkedCts = activationToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(destroyCt, activationToken)
            : null;
        CancellationToken ct = linkedCts?.Token ?? destroyCt;

        // try 밖에 선언 — catch 블록에서 취소 시 원본 복원에 접근 가능하도록
        Renderer[]      renderers = null;
        Material[][]    origMats  = null;
        List<Material>  instances = null;
        try
        {
            var mat = await Managers.AddressableManager.TryLoadAssetAsync<Material>(MaterialKey);
            if (mat == null)
            {
                Debug.LogWarning(
                    $"[DissolveEffect] '{MaterialKey}' 로드 실패 — '{target?.name}' 등장 디졸브 스킵");
                if (target != null && !target.activeSelf) target.SetActive(true);
                onComplete?.Invoke();
                return;
            }

            if (target == null) return;
            // includeInactive: true — 비활성 오브젝트의 렌더러도 사전에 준비
            renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                Debug.LogWarning($"[DissolveEffect] '{target.name}' Renderer 없음 — 등장 디졸브 스킵");
                if (!target.activeSelf) target.SetActive(true);
                onComplete?.Invoke();
                return;
            }

            origMats = new Material[renderers.Length][];
            for (int i = 0; i < renderers.Length; i++)
                origMats[i] = renderers[i].sharedMaterials;

            instances = ReplaceMaterials(renderers, mat, edgeColor ?? DefaultEdgeColor);
            SetDissolveValue(instances, 1f);
            // 비활성 오브젝트는 dissolve=1(완전 투명) 설정 후 활성화 — 플래시 없이 등장
            if (!target.activeSelf) target.SetActive(true);

            float mainDur = Mathf.Max(0.01f, duration * (1f - EdgeFadePortion));
            float edgeDur = Mathf.Max(0.01f, duration - mainDur);

            // Phase 1: dissolve 1 → 0 (몸체 드러남)
            float t = 0f;
            while (t < mainDur)
            {
                t += Time.deltaTime;
                SetDissolveValue(instances, 1f - Mathf.Clamp01(t / mainDur));
                await UniTask.Yield(ct);
            }
            SetDissolveValue(instances, 0f);

            // Phase 2: edge 두께만 페이드 아웃
            t = 0f;
            while (t < edgeDur)
            {
                t += Time.deltaTime;
                SetEdgeWidth(instances, MaxEdgeWidth * (1f - Mathf.Clamp01(t / edgeDur)));
                await UniTask.Yield(ct);
            }
            SetEdgeWidth(instances, 0f);

            await UniTask.Yield(ct); // 1프레임 보호

            // 디졸브 완료 즉시 해당 오브젝트의 모든 렌더러를 원본으로 복구
            if (target != null)
            {
                for (int i = 0; i < renderers.Length && i < origMats.Length; i++)
                    if (renderers[i] != null)
                        renderers[i].sharedMaterials = origMats[i];
            }

            onComplete?.Invoke();
        }
        catch (OperationCanceledException)
        {
            // 취소(풀 반환·파괴) 시 원본 복원 — instances는 finally에서 Destroy되므로 먼저 복원
            if (target != null && renderers != null && origMats != null)
                for (int i = 0; i < renderers.Length && i < origMats.Length; i++)
                    if (renderers[i] != null) renderers[i].sharedMaterials = origMats[i];
        }
        finally
        {
            linkedCts?.Dispose();
            if (instances != null)
                foreach (var m in instances)
                    if (m != null) UnityEngine.Object.Destroy(m);
        }
    }

    private static async UniTask DissolveOutAsync(
        GameObject target, float duration, CancellationToken ct, Action onComplete)
    {
        List<Material> instances = null;
        try
        {
            var mat = await Managers.AddressableManager.TryLoadAssetAsync<Material>(MaterialKey);
            if (mat == null)
            {
                Debug.LogWarning(
                    $"[DissolveEffect] '{MaterialKey}' 로드 실패 — '{target?.name}' 퇴장 디졸브 스킵");
                onComplete?.Invoke();
                return;
            }

            if (target == null) return;
            var renderers = target.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0) { onComplete?.Invoke(); return; }

            instances = ReplaceMaterials(renderers, mat, new Color(0f, 2.4f, 3f, 1f));
            SetDissolveValue(instances, 0f);
            SetEdgeWidth(instances, MaxEdgeWidth);

            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                SetDissolveValue(instances, Mathf.Clamp01(t / duration));
                await UniTask.Yield(ct);
            }
            SetDissolveValue(instances, 1f);

            onComplete?.Invoke();
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (instances != null)
                foreach (var m in instances)
                    if (m != null) UnityEngine.Object.Destroy(m);
        }
    }

    private static List<Material> ReplaceMaterials(
        Renderer[] renderers, Material dissolveMat, Color edgeColor)
    {
        var instances = new List<Material>();
        foreach (var r in renderers)
        {
            var newMats = new Material[r.sharedMaterials.Length];
            for (int j = 0; j < newMats.Length; j++)
            {
                var inst = new Material(dissolveMat);
                var orig = r.sharedMaterials[j];

                if (orig != null)
                {
                    if (orig.HasProperty(BaseMapID) && inst.HasProperty(BaseMapID))
                        inst.SetTexture(BaseMapID, orig.GetTexture(BaseMapID));
                    if (orig.HasProperty(BaseColorID) && inst.HasProperty(BaseColorID))
                        inst.SetColor(BaseColorID, orig.GetColor(BaseColorID));

                    // Polyart/Tint 계열 — _BaseColor 대체 후보
                    if (inst.HasProperty(BaseColorID))
                    {
                        foreach (var propName in FallbackColorProps)
                        {
                            if (!orig.HasProperty(propName)) continue;
                            var c = orig.GetColor(propName);
                            if (c.r + c.g + c.b < 0.01f) continue;
                            c.a = 1f;
                            inst.SetColor(BaseColorID, c);
                            break;
                        }
                    }
                }

                inst.SetColor(EdgeColorID, edgeColor);
                inst.SetFloat(EdgeWidthID, MaxEdgeWidth);
                inst.SetFloat(DissolveID, 0f);

                newMats[j] = inst;
                instances.Add(inst);
            }
            r.materials = newMats;
        }
        return instances;
    }

    private static void SetDissolveValue(List<Material> mats, float value)
    {
        foreach (var m in mats)
            if (m != null) m.SetFloat(DissolveID, value);
    }

    private static void SetEdgeWidth(List<Material> mats, float value)
    {
        foreach (var m in mats)
            if (m != null) m.SetFloat(EdgeWidthID, value);
    }
}
