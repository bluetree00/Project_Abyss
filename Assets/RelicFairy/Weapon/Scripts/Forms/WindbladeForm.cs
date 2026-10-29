using System;
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
    private const float StripBody = 0.4f, StripSeconds = 0.35f;   // 판정 띠 외곽선 — 반폭 + 몸 반경(FormStrike.Line과 같은 셈) · 10-13 이펙트 = 판정

    private const float TwinSpreadDeg = 12f, ReturnDelay = 0.35f, StillWindRatio = 1f;
    private const int   StormEyeCap = 3;

    private float   _lastHitAt = -999f, _decay;
    private float   _returnAt = -1f;
    private Vector3 _returnFrom, _returnDir, _dodgeFrom;
    private bool    _dodgeArmed;

    /// <summary>풍절이 나간 휘두름 번호 — 검풍(카타나 증강)이 같은 휘두름에 또 내지 않는다(10-24 ③ · 10-26).</summary>
    public int   ReleasedSerial { get; private set; } = -2;

    public WindbladeForm(PlayerController player, WeaponData weapon) : base(player, weapon) { }

    public override void OnMeleeHit(in HitInfo hit, bool newSwing, bool finisherStep)
    {
        _lastHitAt = Time.time;
        _decay = 0f;
        if (!newSwing) return;
        if (finisherStep && IsFull) { Release(); return; }
        SetStacks(Stacks + 1);
    }

    public override void OnDamaged() => SetStacks(0, StackReason.Damaged);

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
        // 지나간 길을 검기가 날아가며 벤다 · 판정 모양에(10-18 ⑯ · 10-23 W1)
        FormStrike.LineTravel(Player, _dodgeFrom, to - _dodgeFrom, len, HalfWidth, Atk * StillWindRatio, Def.TriggerVfxKey, 0f, null, n =>
        {
            Log($"무풍 — 맞힘 {n}");
            if (n > 0) RaiseTriggered("무풍");
        }, StripBody, StripSeconds);
    }

    public override void Tick(float dt)
    {
        if (_returnAt > 0f && Time.time >= _returnAt)
        {
            _returnAt = -1f;
            Vector3 end = _returnFrom + _returnDir * Def.TriggerRange;
            FormStrike.LineTravel(Player, end, -_returnDir, Def.TriggerRange, HalfWidth, Atk * Def.TriggerAtkRatio, Def.TriggerVfxKey, 0f, null,
                                  n => Log($"되감는 바람 — 맞힘 {n}"), StripBody, StripSeconds);
        }
        if (!Holding) { _lastHitAt += dt; return; }   // 원거리를 든 동안 쉬는 시간을 세지 않는다
        if (Stacks <= 0 || Time.time - _lastHitAt <= Def.IdleSeconds) return;
        _decay += dt * DecayPerSecond;
        while (_decay >= 1f && Stacks > 0) { _decay -= 1f; SetStacks(Stacks - 1, StackReason.Idle); }
    }

    private void Release()
    {
        Vector3 from = Player.transform.position;
        Vector3 dir  = Player.AimForward;
        float   dmg  = Atk * Def.TriggerAtkRatio;
        bool    storm = Has(StormEye), twin = Has(TwinWind);
        ReleasedSerial = WeaponFormRuntime.Of(Player)?.SwingSerial ?? -2;
        SetStacks(0, StackReason.Trigger);
        // 검기가 날아가며 벤다(10-23 W1) — 폭풍의 눈 되돌림은 다 지나간 뒤 맞힌 수로
        Action<int> landed = hits =>
        {
            Log($"{(twin ? "쌍풍" : "풍절")} — 맞힘 {hits} · 피해 {dmg:0}");
            int back = storm ? Mathf.Min(StormEyeCap, hits) : 0;
            if (back <= Stacks) return;
            SetStacks(back, StackReason.Refund);
            Log($"폭풍의 눈 — 칼바람 +{back}");
        };
        if (twin)
        {
            Vector3 left  = Quaternion.AngleAxis(-TwinSpreadDeg, Vector3.up) * dir;
            Vector3 right = Quaternion.AngleAxis( TwinSpreadDeg, Vector3.up) * dir;
            var once = new HashSet<MonsterBase>();   // 두 갈래 어느 쪽에 들었든 한 번만
            int doneN = 0, sum = 0;
            Action<int> both = n => { sum += n; if (++doneN == 2) landed(sum); };
            FormStrike.LineTravel(Player, from, left,  Def.TriggerRange, HalfWidth, dmg, Def.TriggerVfxKey, 0f, once, both, StripBody, StripSeconds);
            FormStrike.LineTravel(Player, from, right, Def.TriggerRange, HalfWidth, dmg, Def.TriggerVfxKey, 0f, once, both, StripBody, StripSeconds);
        }
        else
            FormStrike.LineTravel(Player, from, dir, Def.TriggerRange, HalfWidth, dmg, Def.TriggerVfxKey, 0f, null, landed, StripBody, StripSeconds);   // 앞 · 옆을 판정 모양에(10-18 ⑯)
        if (Has(ReturnWind)) { _returnAt = Time.time + ReturnDelay; _returnFrom = from; _returnDir = dir; }
        RaiseTriggered(Def.TriggerName);
    }
}
