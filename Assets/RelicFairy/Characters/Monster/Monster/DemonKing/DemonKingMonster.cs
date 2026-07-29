namespace RelicFairy.Monster
{
/// <summary>
/// 데몬킹 몬스터.
/// 특수 상태: HP 40% 이하 도달 시 1회 대폭발 — 주변 대미지 + 분노 추격 속도 부스트 (DemonKingExplosionState).
/// </summary>
public class DemonKingMonster : MonsterBase
{
    public const string PrefabAddress = "DemonKing/DemonKing";
    protected override string ConfigAddress   => "DemonKing/DemonKingConfig";
    protected override string DataAddress     => string.Empty;
    protected override string HeadBoneName    => null;
    protected override float  HPBarHeadOffset => 0.18f;

    private float _rageTimer;

    protected override void Update()
    {
        if (_rageTimer > 0f)
        {
            _rageTimer -= UnityEngine.Time.deltaTime;
            if (_rageTimer <= 0f)
            {
                _runtime.SpeedMultiplier = 1f;
                _agent.speed = _config.stat.moveSpeed;
            }
        }
        base.Update();
    }

    public void StartRageChase(float duration) => _rageTimer = duration;

    protected override void OnEnable()
    {
        _rageTimer = 0f;
        base.OnEnable();
    }
}
}
