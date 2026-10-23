using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 추가 피해(시너지 경로 — 룬 · 아이템 · 증강 · 형 · 조각의 2차 피해)가 맞은 대상마다 작은 타격 표시.
/// 10-19 사용자 「추가 이펙트도 맞았을 때 타격 이펙트가 따로 필요 — 맞았는지 구분이 안 된다」: 주 피해 경로(CombatDamage)엔
/// 피 · 깜빡임 · 소리가 있지만 시너지 경로는 숫자만 떴고, 그 숫자도 같은 순간의 주 피해 숫자에 합쳐졌다.
/// 표시 = 검 섬광(SwordStrikeFlash = Travis Hit_02)을 반경 약 0.4 m로 · 속성 피해면 그 속성 색, 아니면 옅은 금빛을 곱한다 — 주 피해의 흰 섬광과 색으로 갈린다.
/// 속성 점 타격 프리팹은 빛 · 어둠 · 독이 구름처럼 크고, Knife hit은 칼 모양이 박혀(실측 73 · 75) 하나로 맞췄다. 같은 대상은 0.12초에 한 번.
/// </summary>
public static class ExtraHitCue
{
    private const string Key          = "SwordStrikeFlash";
    private const float  Scale        = 0.5f;    // 원래 반경 0.81 m → 0.4 m(주 피해 섬광 0.45 m와 비슷하게 · 0.3 m는 3 m 밖에서 안 보였다 · 실측 75)
    private const float  Life         = 0.6f;
    private const float  PerTargetGap = 0.12f;   // 동시 다발 추가타가 한 점에 겹치지 않게
    private const int    PruneAt      = 128;
    private static readonly Color Neutral = new(1f, 0.85f, 0.45f);   // 속성 없음 = 금빛(주 피해 흰 섬광과 구분)

    private static GameObject s_prefab;
    private static bool       s_loading;
    private static readonly Dictionary<int, float> s_last = new();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        s_prefab  = null;
        s_loading = false;
        s_last.Clear();
    }

    /// <summary>프리팹을 미리 읽는다 — 첫 추가타 표시가 버려지지 않게.</summary>
    public static void Warmup()
    {
        if (s_prefab == null) LoadAsync().Forget();
    }

    public static void Play(int targetId, Vector3 at, RuneElement? element)
    {
        if (s_prefab == null) { Warmup(); return; }
        float now = Time.time;
        if (s_last.TryGetValue(targetId, out float last) && now - last < PerTargetGap) return;
        if (s_last.Count >= PruneAt) s_last.Clear();
        s_last[targetId] = now;
        ElementVfxPlayer.PlayPrefab(s_prefab, at, Scale, Life, element.HasValue ? ElementPalette.Bright(element.Value) : Neutral, null);
    }

    private static async UniTaskVoid LoadAsync()
    {
        if (s_loading) return;
        s_loading = true;
        try
        {
            s_prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(Key);
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Debug.LogWarning($"[ExtraHitCue] {Key} 읽기 실패: {e.Message}"); }
        finally { s_loading = false; }
    }
}
