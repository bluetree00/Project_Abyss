using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 무기 픽업 상태.
/// Enter 전에 PlayerController.PendingPickupWeapon에 WeaponData를 세팅해야 합니다.
///
/// 흐름:
///   1. 픽업 모션 재생 + 이동/공격 차단
///   2. 해당 무기의 AnimationClip Addressables 로드 (await)
///   3. HandlePickupAsync — 빈 슬롯이면 자동 장착, 꽉 차면 ReplacePopup
///   4. ActState.None으로 복귀
/// </summary>
public class ActPickupState : ILayerState<ActState>
{
    private PlayerController _controller;
    private ILayerStateChanger<ActState> _stateChanger;
    private bool _completed;

    public void Init(PlayerController controller, ILayerStateChanger<ActState> stateChanger)
    {
        _controller   = controller;
        _stateChanger = stateChanger;
    }

    public void Enter()
    {
        _completed = false;

        var data   = _controller.PendingPickupWeapon;
        var source = _controller.PendingPickupSource;
        _controller.PendingPickupWeapon = null;
        _controller.PendingPickupSource = null;

        if (data == null)
        {
            _stateChanger.Change(ActState.None);
            return;
        }

        _controller.SetMoveScale(0f);
        _controller.Combo.SetAttacking(true);
        _controller.Anim.CrossFade("WeaponPickup", 0.1f);

        RunAsync(data, source).Forget();
    }

    public void Update()
    {
        if (_completed)
            _stateChanger.Change(ActState.None);
    }

    public void Exit()
    {
        _controller.SetMoveScale(1f);
        _controller.Combo.SetAttacking(false);
        _completed = false;
    }

    // ── 비동기 처리 ──────────────────────────────────────────
    private async UniTaskVoid RunAsync(WeaponData data, WorldWeaponDisplay source)
    {
        try
        {
            // 1. 해당 무기의 애니메이션 클립 먼저 로드
            var keys = CollectAddressableKeys(data);
            if (keys.Count > 0)
            {
                var anim = Managers.AnimationResources;
                if (anim != null)
                    await anim.PreloadClipsAsync(keys);
            }

            // 2. 무기 장착 (빈 슬롯 자동 / 꽉 찬 슬롯 팝업 — 팝업 결과 후 source 처리)
            if (_controller.WeaponManager != null)
                await _controller.WeaponManager.HandlePickupAsync(data, source);
        }
        catch (Exception e)
        {
            Debug.LogError($"[ActPickupState] 픽업 중 오류: {e}");
        }
        finally
        {
            // 성공/실패 모두 반드시 상태 복귀
            _completed = true;
        }
    }

    /// <summary>무기 animationSet에서 Addressables 키 수집</summary>
    private static List<string> CollectAddressableKeys(WeaponData data)
    {
        var keys    = new List<string>();
        var animSet = data?.animationSet as WeaponAnimationSetSO;
        if (animSet == null) return keys;

        foreach (var mapping in animSet.GetAllMappings())
        {
            if (!string.IsNullOrEmpty(mapping.addressableKey))
                keys.Add(mapping.addressableKey);
        }
        return keys;
    }
}
