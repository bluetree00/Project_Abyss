using System;
using UnityEngine;

/// <summary>
/// 유물 전용 이상 상태(유물 성장 v2, 10-02) — 적 하나에 붙는 그릇.
/// <list type="bullet">
/// <item><b>태양흔</b>(가웨인) — 여명에 쌓이고 정오에 개화(문턱형, 명조 「불꽃 효과」 결). 기본 상한 3.</item>
/// <item><b>흑점</b>(가웨인 일식) — 잠식 50+ 동안 태양흔 대신 쌓인다. 상한 없음.</item>
/// <item><b>배신의 낙인</b>(랜슬롯) — 피해 없는 표식, 심판이 찢는다(명조 「암흑 효과」 결). 기본 상한 3, 넘치면 원한으로(부르는 쪽이 받는다).</item>
/// </list>
/// 그릇은 수만 센다 — 언제 쌓고 무엇이 터지는지는 유물 조각 효과가 정한다. 부여 간격(대상당 0.5초)은 그릇이 지킨다.
/// 적이 꺼지면(사망 · 풀 반환) 비운다. 표시는 <see cref="RelicFairy.Monster.MonsterBase.CollectStatuses"/>가 스택 수와 함께 체력 바 상태 줄에 올린다.
/// </summary>
[DisallowMultipleComponent]
public sealed class RelicMarkStatus : MonoBehaviour
{
    public const string SunmarkId   = "relic_sunmark";
    public const string BlackspotId = "relic_blackspot";
    public const string BrandId     = "relic_brand";

    public const int   DefaultSunmarkCap = 3;
    public const int   DefaultBrandCap   = 3;
    public const float DefaultApplyGap   = 0.5f;   // 같은 종류를 같은 대상에 다시 쌓을 수 있는 최소 간격(초)

    /// <summary>어느 적이든 수가 바뀌었다(연출 · HUD · 실측 도구).</summary>
    public static event Action<RelicMarkStatus> Changed;

    private int   _sunmark, _blackspot, _brand;
    private int   _sunmarkCap = DefaultSunmarkCap;
    private int   _brandCap   = DefaultBrandCap;
    private int   _brandCapBonus;
    private float _brandCapBonusUntil;
    private float _nextSunmarkAt, _nextBrandAt;

    public int  Sunmark    => _sunmark;
    public int  SunmarkCap => _sunmarkCap;
    public int  Blackspot  => _blackspot;
    public int  Brand      => _brand;
    public int  BrandCap   => _brandCap + (Time.time < _brandCapBonusUntil ? _brandCapBonus : 0);
    public bool IsEmpty    => _sunmark == 0 && _blackspot == 0 && _brand == 0;
    /// <summary>폭로(빛 반응) 중인가 — 낙인 상한 보너스가 살아 있다.</summary>
    public bool IsExposed  => _brandCapBonus > 0 && Time.time < _brandCapBonusUntil;

    /// <summary>대상의 그릇. <paramref name="create"/>면 없을 때 붙인다(몬스터 · 허수아비 모두).</summary>
    public static RelicMarkStatus Of(GameObject target, bool create)
    {
        if (target == null) return null;
        if (target.TryGetComponent<RelicMarkStatus>(out var s)) return s;
        return create ? target.AddComponent<RelicMarkStatus>() : null;
    }

    // ── 태양흔 · 흑점 ─────────────────────────────────────

    /// <summary>
    /// 태양흔 <paramref name="n"/>을 쌓는다(상한 <paramref name="cap"/> — 가웨인 공명이 정한다). 실제로 쌓인 수를 돌려준다.
    /// <paramref name="ignoreGap"/>이 아니면 대상당 <see cref="DefaultApplyGap"/>초에 한 번만.
    /// </summary>
    public int AddSunmark(int n, int cap, bool ignoreGap = false)
    {
        if (n <= 0) return 0;
        if (!ignoreGap)
        {
            if (Time.time < _nextSunmarkAt) return 0;
            _nextSunmarkAt = Time.time + DefaultApplyGap;
        }
        _sunmarkCap = Mathf.Max(1, cap);
        int before = _sunmark;
        _sunmark = Mathf.Min(_sunmarkCap, _sunmark + n);
        if (_sunmark != before) Changed?.Invoke(this);
        return _sunmark - before;
    }

    /// <summary>태양흔을 거둔다(개화). <paramref name="keep"/>만큼은 남긴다(새벽에서 한낮으로). 거둔 수를 돌려준다.</summary>
    public int ConsumeSunmark(int keep = 0)
    {
        int take = Mathf.Max(0, _sunmark - Mathf.Max(0, keep));
        if (take <= 0) return 0;
        _sunmark -= take;
        Changed?.Invoke(this);
        return take;
    }

    /// <summary>태양흔을 정해진 수로 맞춘다(정점 「가장 높은 적에 맞춘다」 · 보스 상한 채움).</summary>
    public void SetSunmark(int value, int cap)
    {
        _sunmarkCap = Mathf.Max(1, cap);
        int v = Mathf.Clamp(value, 0, _sunmarkCap);
        if (v == _sunmark) return;
        _sunmark = v;
        Changed?.Invoke(this);
    }

    /// <summary>흑점(일식) — 상한 없음.</summary>
    public int AddBlackspot(int n, bool ignoreGap = false)
    {
        if (n <= 0) return 0;
        if (!ignoreGap)
        {
            if (Time.time < _nextSunmarkAt) return 0;
            _nextSunmarkAt = Time.time + DefaultApplyGap;
        }
        _blackspot += n;
        Changed?.Invoke(this);
        return n;
    }

    public int ConsumeBlackspot()
    {
        int take = _blackspot;
        if (take <= 0) return 0;
        _blackspot = 0;
        Changed?.Invoke(this);
        return take;
    }

    // ── 배신의 낙인 ──────────────────────────────────────

    /// <summary>
    /// 낙인 <paramref name="n"/>을 새긴다(기본 상한 <paramref name="cap"/> + 폭로 보너스). 실제로 새긴 수를 돌려주고,
    /// 상한을 넘친 수는 <paramref name="overflow"/>로(원한이 된다 — 부르는 쪽이 받는다).
    /// </summary>
    public int AddBrand(int n, int cap, out int overflow, bool ignoreGap = false)
    {
        overflow = 0;
        if (n <= 0) return 0;
        if (!ignoreGap)
        {
            if (Time.time < _nextBrandAt) return 0;
            _nextBrandAt = Time.time + DefaultApplyGap;
        }
        _brandCap = Mathf.Max(1, cap);
        int room  = Mathf.Max(0, BrandCap - _brand);
        int added = Mathf.Min(room, n);
        overflow  = n - added;
        if (added > 0) { _brand += added; Changed?.Invoke(this); }
        return added;
    }

    /// <summary>낙인을 모두 찢는다. 찢은 수를 돌려준다.</summary>
    public int ConsumeBrand()
    {
        int take = _brand;
        if (take <= 0) return 0;
        _brand = 0;
        Changed?.Invoke(this);
        return take;
    }

    /// <summary>낙인을 하나만 찢는다(찢긴 맹세의 검 · 작은 심판).</summary>
    public bool ConsumeOneBrand()
    {
        if (_brand <= 0) return false;
        _brand--;
        Changed?.Invoke(this);
        return true;
    }

    /// <summary>낙인을 상한까지 채운다(배신자의 걸음 · 끝의 문턱 ③).</summary>
    public int FillBrand(int cap)
    {
        _brandCap = Mathf.Max(1, cap);
        int before = _brand;
        _brand = BrandCap;
        if (_brand != before) Changed?.Invoke(this);
        return _brand - before;
    }

    /// <summary>폭로(빛 반응) — 낙인 상한을 잠시 늘린다.</summary>
    public void SetBrandCapBonus(int bonus, float duration)
    {
        _brandCapBonus = Mathf.Max(0, bonus);
        _brandCapBonusUntil = Time.time + Mathf.Max(0f, duration);
        Changed?.Invoke(this);
    }

    // ── 표시 ─────────────────────────────────────────────

    /// <summary>체력 바 상태 줄 항목(스택 수 · 상한 대비 채움).</summary>
    public void Collect(System.Collections.Generic.List<BuffViewItem> into)
    {
        if (into == null || IsEmpty) return;
        if (_sunmark > 0)
            into.Add(RelicFairy.Monster.MonsterStatusReceiver.MakeItem(SunmarkId, _sunmark, (float)_sunmark / Mathf.Max(1, _sunmarkCap), -1f));
        if (_blackspot > 0)
            into.Add(RelicFairy.Monster.MonsterStatusReceiver.MakeItem(BlackspotId, _blackspot, 1f, -1f));
        if (_brand > 0)
            into.Add(RelicFairy.Monster.MonsterStatusReceiver.MakeItem(BrandId, _brand, (float)_brand / Mathf.Max(1, BrandCap), -1f));
    }

    // ── 수명 ─────────────────────────────────────────────

    private void OnDisable()
    {
        // 사망 · 풀 반환 — 다음 생애로 넘기지 않는다
        _sunmark = _blackspot = _brand = 0;
        _brandCapBonus = 0;
        _brandCapBonusUntil = 0f;
        _nextSunmarkAt = _nextBrandAt = 0f;
    }
}
