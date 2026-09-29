using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 유물의 겉모습 적용 — 고유 Q 애니 클립 교체와 오라 VFX 부착/해제.
/// PlayerController가 소유한다. 유물의 <b>로직</b>(패시브·스킬·피해 보정)은 IRelicBehavior가 맡고,
/// 이 클래스는 리소스를 불러와 붙이는 일만 한다.
/// </summary>
public sealed class RelicAppearance
{
    // ── Private ───────────────────────────────────────────────────
    private GameObject _auraInstance;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 유물 Q 애니 클립을 로드해 유물 전용 상태(RelicQ_*)에 물린다 — 무기 교체로 지워지지 않는다.
    ///
    /// 유물 클립 키는 무기 프리로드 경로(PreloadWeaponClipsAsync)에 포함되지 않아 캐시에 없다 —
    /// 그래서 여기서 직접 로드한 뒤 오버라이드한다. 로드는 비동기지만 Q 입력 전까지만 끝나면 되므로
    /// 오라 VFX와 같은 fire-and-forget으로 둔다.
    ///
    /// 유물은 <b>인자로 붙잡아 둔다</b> — 로드 대기 중에 호출자의 유물 필드가 바뀌어도
    /// 요청한 유물의 클립만 적용한다.
    /// </summary>
    public async UniTaskVoid ApplyQAnimationAsync(RelicClassSO relic, AnimatorOverrideService svc, UnityEngine.Object owner)
    {
        if (relic == null || svc == null) return;

        int steps = relic.QSkillStepCount;
        var keys = new List<string>(steps + 1);
        for (int i = 0; i < steps; i++)
        {
            var key = relic.QSkillClipKeyAt(i);
            if (!string.IsNullOrEmpty(key)) keys.Add(key);
        }
        // 단독 모션(캐스트·마무리) 클립 — 시퀀스와 별개 키.
        string mainKey = relic.QSkillMainClipKey;
        if (!string.IsNullOrEmpty(mainKey)) keys.Add(mainKey);
        if (keys.Count == 0) return;   // 클립 키 미설정 유물 — 컨트롤러 기본 클립 그대로(폴백)

        try
        {
            await Managers.AnimationResources.PreloadClipsAsync(keys);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerController] 유물 Q 클립 프리로드 실패: {e.Message}");
            return;
        }

        if (owner == null) return;   // 로드 중 파괴

        for (int i = 0; i < steps; i++)
        {
            var clipKey = relic.QSkillClipKeyAt(i);
            if (string.IsNullOrEmpty(clipKey)) continue;

            var clip = Managers.AnimationResources.GetClip(clipKey);
            if (clip == null)
            {
                // 조용히 넘기면 안 된다 — 기본 클립(NormalAttack_*)이 남아 Q가 평타 모션으로 나간다.
                Debug.LogError($"[PlayerController] 유물 Q 클립 '{clipKey}' 로드 실패 — Q가 평타 모션으로 재생된다");
                continue;
            }

            var stateName = relic.QSkillStateAt(i);
            if (!svc.OverrideRelic(stateName, clip))
                Debug.LogError($"[PlayerController] 유물 Q 오버라이드 실패 — 컨트롤러에 '{stateName}' 이름의 원본 클립이 없다.");
        }

        if (!string.IsNullOrEmpty(mainKey))
        {
            var mainClip = Managers.AnimationResources.GetClip(mainKey);
            if (mainClip == null)
                Debug.LogError($"[PlayerController] 유물 Q 단독 모션 클립 '{mainKey}' 로드 실패 — 기본 클립 유지");
            else if (!svc.OverrideRelic(relic.QSkillMainState, mainClip))
                Debug.LogError($"[PlayerController] 유물 Q 단독 모션 오버라이드 실패 — 컨트롤러에 '{relic.QSkillMainState}' 원본 클립이 없다.");
        }
    }

    /// <summary>유물 오라 VFX를 소켓(없으면 루트)에 부착. 재적용 시 기존 인스턴스를 먼저 정리(멱등).</summary>
    public async UniTaskVoid SpawnAuraAsync(string key, string socket, Transform root)
    {
        ReleaseAura();

        Transform parent = string.IsNullOrEmpty(socket)
            ? root
            : (Util.FindDeepChild(root, socket) ?? root);

        try
        {
            var instance = await Managers.AddressableManager.InstantiateAsync(key, parent);

            // 로드가 끝나기 전에 플레이어가 사라졌으면 붙일 곳이 없다 — 떠도는 인스턴스를 남기지 않고 반납한다.
            if (root == null)
            {
                if (instance != null) Managers.AddressableManager?.ReleaseInstance(instance);
                return;
            }
            _auraInstance = instance;
        }
        catch (OperationCanceledException) { }
        catch (Exception e)
        {
            Debug.LogWarning($"[PlayerController] 유물 오라 VFX 로드 실패: {key}\n{e.Message}");
        }
    }

    public void ReleaseAura()
    {
        if (_auraInstance != null)
        {
            Managers.AddressableManager?.ReleaseInstance(_auraInstance);
            _auraInstance = null;
        }
    }
}
