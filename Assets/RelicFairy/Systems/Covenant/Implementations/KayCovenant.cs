using UnityEngine;

/// <summary>
/// 케이 경의 서약 — 일정한 리듬의 강타 (전투 리듬형: 주기 발동)
///
/// [선택]   N초마다 다음 공격 1회가 특수 형태(관통)로 전환.
/// [강화]   주기 단축 + 부채꼴 형태(범위↑).
/// [각성]   주기 발동 공격 적중 수만큼 다음 주기 단축.
///
/// 데이터 인덱스: [0]주기초 [1]형태(0관통/1부채꼴) [2]적중당단축초 [3]피해배율
/// ⚠️ P1 근사: 관통/부채꼴을 전방 광역 버스트(DealAoe)로 근사. 실제 형태 변환은 P2 seam.
/// </summary>
public sealed class KayCovenant : CovenantBase
{
    private const int V_INTERVAL = 0;
    private const int V_SHAPE    = 1;
    private const int V_PERHIT   = 2;
    private const int V_MULT     = 3;

    public override string CovenantId => CovenantFactory.Kay;
    public override CovenantCategory Category => CovenantCategory.CombatRhythm;

    public override string DisplayName         => "케이 경의 서약";
    public override string LoreText            => "케이 경 — 일정한 리듬으로 강타를 내지르는 서약을 전달했다";
    public override string BasicDescription    => "8초마다 다음 공격 1회가 관통 형태로 전환.";
    public override string EnhancedDescription => "주기 6초로 단축. 관통 대신 전방 부채꼴 형태로 변환.";
    public override string EvolvedDescription  => "주기 발동 공격이 적중한 수만큼 다음 주기가 0.5초씩 단축.";

    private float _timer;
    private bool  _armed;
    private float _nextInterval;

    private float Interval => Mathf.Max(1f, V(V_INTERVAL, 8f));
    private float Mult     => V(V_MULT, 1f);
    private float PerHit   => V(V_PERHIT, 0f);
    private float Radius   => V(V_SHAPE, 0f) >= 1f ? 3f : 2f; // 부채꼴=더 넓게 근사

    public override void OnRoomEnter() { _timer = 0f; _armed = false; _nextInterval = Interval; }

    public override void Tick(float deltaTime)
    {
        if (_armed) return;
        _timer += deltaTime;
        float target = _nextInterval > 0f ? _nextInterval : Interval;
        if (_timer >= target) { _armed = true; _timer = 0f; }
    }

    public override void OnAttackHit(GameObject target, float dmg)
    {
        if (!_armed || target == null) return;
        _armed = false;

        Vector3 at = target.transform.position;
        int hits = DealAoe(at, Radius, Mult); // 관통/부채꼴 근사
        Vfx("VFX_FireExplosion", at);

        // 각성: 적중 수만큼 다음 주기 단축
        _nextInterval = Mathf.Max(1f, Interval - PerHit * hits);
    }
}
