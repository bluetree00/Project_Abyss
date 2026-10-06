using System.Collections.Generic;
using RelicFairy.Monster;
using UnityEngine;

/// <summary>
/// 클라렌트(카타나) 형 「칼바람」 — 연격. 기획 장비개선 §4-2.
/// - 쌓기: 평타가 맞은 휘두름마다 +1(최대 5).
/// - 잃기: 피격되면 0, 3초 못 맞히면 1초에 1씩 준다.
/// - 발동: 가득이면 콤보 마지막 타가 「풍절」 — 정면 8 m 관통 검기(공격력 150%).
/// 변주: Ⅱ 쌍풍 / 되감는 바람 · Ⅲ 무풍 / 폭풍의 눈(Task 4).
/// </summary>
public sealed class WindbladeForm : WeaponFormBase
{
    public const string TwinWind = "twin_wind", ReturnWind = "return_wind", StillWind = "still_wind", StormEye = "storm_eye";
    private const float HalfWidth = 0.9f, DecayPerSecond = 1f;

    private const float TwinSpreadDeg = 12f, ReturnDelay = 0.35f, StillWindRatio = 1f;
    private const int   StormEyeCap = 3;

    private readonly List<MonsterBase> _buf = new(16);
    private float   _lastHitAt = -999f, _decay;
    private float   _returnAt = -1f;
    private Vector3 _returnFrom, _returnDir, _dodgeFrom;
    private bool    _dodgeArmed;

    public WindbladeForm(PlayerController player, WeaponData weapon) : base(player, weapon) { }

    public override void OnMeleeHit(in HitInfo hit, bool newSwing, bool finisherStep)
    {
        _lastHitAt = Time.time;
        _decay = 0f;
        if (!newSwing) return;
        if (finisherStep && IsFull) { Release(); return; }
        SetStacks(Stacks + 1);
    }

    public override void OnDamaged() => SetStacks(0);

    public override void OnDodgeStart()
    {
        _dodgeArmed = Has(StillWind) && IsFull;
        _dodgeFrom  = Player.transform.position;
    }

    public override void OnDodgeEnd()
    {
        if (!_dodgeArmed) return;
        _dodgeArmed = false;
        Vector3 to = Player.transform.position;
        float len = Vector3.Distance(_dodgeFrom, to);
        int n = FormStrike.Line(Player, _dodgeFrom, to - _dodgeFrom, len, HalfWidth, Atk * StillWindRatio, _buf);
        if (len > 0.3f) FormStrike.PlayVfx(Def.TriggerVfxKey, _dodgeFrom + Vector3.up, to - _dodgeFrom, Def.TriggerVfxScale);   // 지나간 길을 벤다 — 보이게
        Debug.Log($"[WeaponForm] 무풍 — 맞힘 {n}");
        if (n > 0) RaiseTriggered("무풍");
    }

    public override void Tick(float dt)
    {
        if (_returnAt > 0f && Time.time >= _returnAt)
        {
            _returnAt = -1f;
            Vector3 end = _returnFrom + _returnDir * Def.TriggerRange;
            int n = FormStrike.Line(Player, end, -_returnDir, Def.TriggerRange, HalfWidth, Atk * Def.TriggerAtkRatio, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, end + Vector3.up, -_returnDir, Def.TriggerVfxScale);
            Debug.Log($"[WeaponForm] 되감는 바람 — 맞힘 {n}");
        }
        if (Stacks <= 0 || Time.time - _lastHitAt <= Def.IdleSeconds) return;
        _decay += dt * DecayPerSecond;
        while (_decay >= 1f && Stacks > 0) { _decay -= 1f; SetStacks(Stacks - 1); }
    }

    private void Release()
    {
        Vector3 from = Player.transform.position;
        Vector3 dir  = Player.AimForward;
        float   dmg  = Atk * Def.TriggerAtkRatio;
        int hits;
        if (Has(TwinWind))
        {
            Vector3 left  = Quaternion.AngleAxis(-TwinSpreadDeg, Vector3.up) * dir;
            Vector3 right = Quaternion.AngleAxis( TwinSpreadDeg, Vector3.up) * dir;
            hits = FormStrike.Lines(Player, from, left, right, Def.TriggerRange, HalfWidth, dmg, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, from + Vector3.up, left,  Def.TriggerVfxScale);
            FormStrike.PlayVfx(Def.TriggerVfxKey, from + Vector3.up, right, Def.TriggerVfxScale);
            Debug.Log($"[WeaponForm] 쌍풍 — 맞힘 {hits}");
        }
        else
        {
            hits = FormStrike.Line(Player, from, dir, Def.TriggerRange, HalfWidth, dmg, _buf);
            FormStrike.PlayVfx(Def.TriggerVfxKey, from + Vector3.up, dir, Def.TriggerVfxScale);
        }
        if (Has(ReturnWind)) { _returnAt = Time.time + ReturnDelay; _returnFrom = from; _returnDir = dir; }
        int back = Has(StormEye) ? Mathf.Min(StormEyeCap, hits) : 0;
        SetStacks(back);
        if (back > 0) Debug.Log($"[WeaponForm] 폭풍의 눈 — 칼바람 +{back}");
        Debug.Log($"[WeaponForm] 풍절 — 맞힘 {hits} · 피해 {dmg:0}");
        RaiseTriggered(Def.TriggerName);
    }
}
