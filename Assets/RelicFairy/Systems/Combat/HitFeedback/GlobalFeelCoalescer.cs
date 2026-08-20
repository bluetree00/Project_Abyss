using System;
using UnityEngine;

/// <summary>
/// 글로벌 화면 연출의 <b>프레임 합산기</b>.
///
/// 타격이 들어올 때마다 셰이크·플래시·줌·펄스를 즉시 거는 대신 이 프레임의 버퍼에 적립하고,
/// 프레임 끝(LateUpdate)에 <see cref="OnBurst"/>를 <b>딱 한 번</b> 발행한다.
///
/// 이 계층이 없으면 분열·관통·폭발이 붙은 원거리 한 발이 수십 건의 타격을 만들어
/// 셰이크 트라우마가 선형 누적되고 풀스크린 플래시가 수십 겹으로 쌓인다.
/// 근접은 한 스윙에 1~3타뿐이라 드러나지 않던 결함이고, 파츠가 붙은 원거리가 이를 드러냈다.
///
/// <b>합산 규칙</b>
///  · 세기는 <b>합계가 아니라 최대값</b>이 정한다 — 20발이 크리 한 방보다 세게 흔들리면 안 된다.
///  · "많이 맞췄다"는 체감은 셰이크에만 로그 가산으로 얹는다(선형 누적 폐기).
///  · 플래시·줌·펄스는 세기를 올리지 않고 <b>횟수만 1회로</b> 줄인다 — 눈이 아픈 채널이라 가산이 독이다.
///
/// 국소 연출(피격자 플래시·히트 VFX·데미지 숫자)은 여기를 타지 않는다. 착탄 지점마다 나야 옳다.
/// </summary>
public static class GlobalFeelCoalescer
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>다중 타격 셰이크 가산 계수. 세기 = base × (1 + k·log2(N)).</summary>
    private const float MultiHitGain    = 0.18f;
    /// <summary>가산 상한. 아무리 많이 맞춰도 단타의 이 배를 넘지 않는다.</summary>
    private const float MultiHitGainMax = 1.8f;

    // ── Static ────────────────────────────────────────────────────
    /// <summary>프레임 합산 결과 발행. 구독자는 OnEnable에서 += / OnDisable에서 -= 필수.</summary>
    public static event Action<HitBurst> OnBurst;

    private class Host : MonoBehaviour
    {
        private void LateUpdate() => Flush();
    }

    private static Host _host;

    // 프레임 누적 상태
    private static int        _count;
    private static float      _maxDamage;
    private static float      _totalDamage;
    private static bool       _anyCrit;
    private static Vector3    _dirSum;
    private static Vector3    _hitPoint;
    private static WeaponType _weaponType;
    private static GameObject _attacker;

    // 도메인 리로드 OFF: 2회차에 누적치·호스트 참조가 잔류하면 첫 프레임에 유령 버스트가 터진다.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        _host = null;
        ClearAccumulator();
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>타격 1건을 이번 프레임 버퍼에 적립한다. 실제 연출은 프레임 끝에 1회 발행된다.</summary>
    public static void Accumulate(in HitInfo info)
    {
        EnsureHost();

        _count++;
        _totalDamage += info.Damage;
        _anyCrit     |= info.IsCritical;
        _dirSum      += info.AttackDirection;

        // 대표값(무기·공격자·착탄점)은 가장 센 타격에서 가져온다 — 손맛 프로필이 잔타에 끌려가지 않게.
        if (info.Damage >= _maxDamage)
        {
            _maxDamage  = info.Damage;
            _hitPoint   = info.HitPoint;
            _weaponType = info.WeaponType;
            _attacker   = info.Attacker;
        }
    }

    /// <summary>N타 동시 적중에 대한 세기 배수. N=1이면 정확히 1(단타 체감 불변).</summary>
    public static float MultiHitScale(int count)
        => count <= 1 ? 1f : Mathf.Min(1f + MultiHitGain * Mathf.Log(count, 2f), MultiHitGainMax);

    /// <summary>버퍼를 비우고 합산 결과를 발행한다. 프레임당 1회, 적립분이 없으면 무동작.</summary>
    public static void Flush()
    {
        if (_count == 0) return;

        var burst = new HitBurst(
            count:       _count,
            maxDamage:   _maxDamage,
            totalDamage: _totalDamage,
            anyCritical: _anyCrit,
            // 사방에서 들어온 타격은 방향이 상쇄된다 — 그 경우 0을 넘겨 무방향 셰이크가 되게 둔다.
            direction:   _dirSum.sqrMagnitude > 0.0001f ? _dirSum.normalized : Vector3.zero,
            hitPoint:    _hitPoint,
            weaponType:  _weaponType,
            attacker:    _attacker);

        ClearAccumulator();

        var handlers = OnBurst;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action<HitBurst>)handler).Invoke(burst); }
            catch (Exception e) { Debug.LogException(e); }
        }
    }

    // ── Private Methods ───────────────────────────────────────────
    private static void ClearAccumulator()
    {
        _count       = 0;
        _maxDamage   = 0f;
        _totalDamage = 0f;
        _anyCrit     = false;
        _dirSum      = Vector3.zero;
        _hitPoint    = Vector3.zero;
        _weaponType  = WeaponType.None;
        _attacker    = null;
    }

    private static void EnsureHost()
    {
        if (_host != null) return;
        var go = new GameObject("[GlobalFeelCoalescer]");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _host = go.AddComponent<Host>();
    }
}
