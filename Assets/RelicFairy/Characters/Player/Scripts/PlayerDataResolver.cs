using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 플레이어 캐릭터 데이터의 출처를 해석한다 — SO(범용 바디) · Addressables · 서버(CSV) 행 덮어쓰기.
/// PlayerController 초기화와 유물 적용이 호출한다. 상태를 갖지 않는다.
/// </summary>
public static class PlayerDataResolver
{
    /// <summary>
    /// 지정 char_id의 서버 PlayerStatEntry로 RuntimeStats + CharacterData(수치)를 적용한다.
    /// 무유물 기본은 "knight", 유물 획득 시 유물 char_id("gawain"/"galahad")로 호출 → 행 전체 교체.
    /// 서버 데이터/매칭 행이 없으면 false(호출자가 폴백 처리).
    /// </summary>
    /// <param name="characterData">성공 시 서버 값을 덮은 런타임 클론으로 교체된다.</param>
    public static bool TryApplyServerStats(string charId, PlayerRuntimeStats stats, ref CharacterData characterData)
    {
        if (string.IsNullOrEmpty(charId)) return false;

        var mgr = Managers.PlayerData;
        if (mgr == null || !mgr.IsInitialized)
            return false;

        var entry = mgr.GetPlayer(charId);
        if (entry == null)
            return false;

        var passives = mgr.GetPassives(entry.passive_id);

        var preloaded = Managers.CharacterData?.M_CharacterData;

        // SO 의 LayerMask/Sprite/Passive 참조는 유지하되 수치 컬럼은 CSV(서버) 로 덮어쓴다.
        // 원본 .asset 을 변경하지 않도록 Instantiate 로 런타임 클론을 만든 뒤 적용.
        var source = preloaded ?? characterData;

        // 포이즈/스태미너는 CSV에 컬럼이 없다 — SO 경로와 같은 값이 나오도록 같은 SO를 넘긴다.
        stats.InitializeFromServer(entry, passives, source);

        if (source != null)
        {
            var clone = ScriptableObject.Instantiate(source);
            clone.name = source.name + " (Runtime)";
            ApplyServerOverridesTo(clone, entry);
            characterData = clone;
            characterData.Initialize();
        }

        Debug.Log($"[PlayerController] 서버 데이터 사용: {entry.char_id} (HP:{entry.max_health}, Melee:{entry.base_melee_attack}, MoveSpd:{entry.base_move_speed})");
        return true;
    }

    /// <summary>CSV(PlayerStatEntry) 값을 CharacterData 클론에 덮어씀. LayerMask/Sprite/SO 참조는 건드리지 않는다.</summary>
    public static void ApplyServerOverridesTo(CharacterData data, PlayerStatEntry e)
    {
        data.maxHealth                  = e.max_health;
        data.baseMeleeAttack            = e.base_melee_attack;
        data.baseRangedAttack           = e.base_ranged_attack;
        data.baseDefense                = e.base_defense;
        data.baseLuck                   = e.base_luck;
        data.baseMoveSpeed              = e.base_move_speed;
        data.baseRunSpeed               = e.base_run_speed;
        if (e.base_run_ramp > 0.01f) data.runRampDuration = e.base_run_ramp; // CSV 컬럼 없으면 에셋값 유지
        if (e.move_accel > 0.01f)          data.moveAccel               = e.move_accel;
        if (e.move_decel > 0.01f)          data.moveDecel               = e.move_decel;
        if (e.reverse_accel_mult > 0.01f)  data.reverseAccelMultiplier  = e.reverse_accel_mult;
        if (e.initial_boost > 0.0001f)     data.initialBoost            = e.initial_boost;

        data.comboDuration              = e.combo_duration;
        data.heavyAttackChargeThreshold = e.heavy_charge_threshold;
        data.heavyAttackReleaseTime     = e.heavy_release_time;
        data.dashSpeed                  = e.dash_speed;
        data.dashDuration               = e.dash_duration;
        data.dodgeCooldown              = e.dodge_cooldown;
        data.jumpForce                  = e.jump_force;
        data.gravity                    = e.gravity;
        data.fallMultiplier             = e.fall_multiplier;
        // groundCheckDistance 는 차트에서 덮어쓰지 않는다.
        // 이 값은 밸런스 스탯이 아니라 캡슐 크기·스케일에 종속된 콜라이더 정합용 물리 상수다.
        // 서버 값(0.3)이 SO(0.15)를 덮어쓰면 접지 허용 밴드가 25cm 로 벌어져
        // 캐릭터가 지면 위에 뜬 채로 멈춘다(접지 판정 시 중력이 꺼지므로).
        // 포이즈·스태미너를 CSV 컬럼 없이 SO 값으로 두는 것과 같은 판단이다.
        //   data.groundCheckDistance = e.ground_check_distance;   ← 의도적으로 제거
        data.airControlMultiplier       = e.air_control_multiplier;
        data.groundDrag                 = e.ground_drag;
        data.airDrag                    = e.air_drag;
    }

    /// <summary>
    /// Addressables에서 CharacterData를 불러와 공용 매니저에 등록하고 RuntimeStats를 초기화한다.
    /// 로드 자체가 실패하면 null. 로드 뒤 등록·초기화에서 예외가 나도 불러온 데이터는 돌려준다
    /// (분리 전 코드가 데이터를 먼저 대입해 두던 동작 — 여기서 null을 주면 컨트롤러가 영구히 멈춘다).
    /// </summary>
    public static async UniTask<CharacterData> LoadAsync(string key, PlayerRuntimeStats stats)
    {
        if (string.IsNullOrEmpty(key))
        {
            Debug.LogError("캐릭터 이름이 비어있습니다.");
            return null;
        }

        CharacterData data = null;
        try
        {
            // UniTask 기반 Addressables 로드
            data = await Managers.AddressableManager.LoadAssetAsync<CharacterData>(key);

            if (data == null)
            {
                Debug.LogError($"캐릭터 데이터 '{key}' 로드 실패");
                return null;
            }

            Managers.CharacterData.SetCharacterData(data);

            // HUD/전투용 실시간 스탯 초기화 (SO는 템플릿)
            data.Initialize();
            stats.InitializeFrom(data);

            Debug.Log($"캐릭터 데이터 '{key}' 로드 완료");
            return data;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
        catch (Exception e)
        {
            Debug.LogError($"캐릭터 데이터 로드 중 예외 발생: {e.Message}");
            return data;
        }
    }
}
