using UnityEngine;

/// <summary>
/// 피격자(몬스터) 측 타격 연출 프리셋.
/// 블로그 ⑧ 피격자 반응(색/형태/질감) + ② 화면 번쩍임(Light) 파라미터를 한 SO에 통합.
///
/// ※ 형태(피격 애니메이션)와 넉백은 GetHitState가 기존 처리 → 이 SO 범위 외.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Combat/Monster Hit Profile", fileName = "MonsterHitProfile")]
public class MonsterHitProfileSO : ScriptableObject
{
    // ── Serialized ────────────────────────────────────────────────
    [Header("⑧ Flash (색 변화)")]
    [SerializeField] private Color _flashColor = new(1f, 0.12f, 0.1f, 1f); // 피격 빨강
    [SerializeField, Range(0f, 10f)] private float _emissionBoost = 2f;
    [SerializeField, Range(0f, 1f)]  private float _flashDuration = 0.18f;
    [Tooltip("플래시 지속시간 동안 반복할 깜빡임 횟수. 2면 더블 블링크(빨강→어둠→빨강→어둠).")]
    [SerializeField, Range(1, 4)] private int _flashPulses = 2;
    [SerializeField] private AnimationCurve _flashCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Tooltip("피격 시 외곽선(아웃라인)도 함께 빨갛게 깜빡이게 한다. MonsterOutline 셰이더의 _HitFlash를 구동.")]
    [SerializeField] private bool _useOutlineFlash = true;

    [Header("② Point Light (화면 번쩍임)")]
    [SerializeField] private bool  _usePointLight = true;
    [SerializeField] private Color _lightColor = Color.white;
    [SerializeField, Range(0f, 20f)] private float _lightIntensity = 5f;
    [SerializeField, Range(0.1f, 10f)] private float _lightRange = 3f;
    [SerializeField] private Vector3 _lightLocalOffset = new(0f, 1f, 0f);
    [SerializeField, Range(0f, 1f)]  private float _lightDuration = 0.10f;
    [SerializeField] private AnimationCurve _lightCurve = AnimationCurve.EaseInOut(0f, 1f, 1f, 0f);

    [Header("방향성 히트 리액션 (플린치/틸트)")]
    [SerializeField] private bool  _useHitReaction = true;
    [SerializeField, Range(0f, 45f)]  private float _flinchTiltAngle   = 14f;   // 맞은 방향으로 상단이 기우는 각도(deg)
    [SerializeField, Range(0f, 0.5f)] private float _flinchBackOffset  = 0.07f; // 맞은 방향으로 밀리는 비주얼 오프셋(m)
    [SerializeField, Range(0.02f, 0.5f)] private float _flinchDuration = 0.16f; // 틸트→복귀 전체 시간(s)
    [SerializeField, Range(1f, 3f)]   private float _critReactionMultiplier = 1.6f;
    // env: 0→즉시 1(스냅)→오버슈트(-)→0 정착. 스프링 감쇠 느낌. rest 기준 절대 세팅의 가중치로 사용.
    [SerializeField] private AnimationCurve _flinchCurve = new(
        new Keyframe(0f, 1f), new Keyframe(0.55f, -0.15f), new Keyframe(1f, 0f));

    // ── Properties ────────────────────────────────────────────────
    public Color          FlashColor        => _flashColor;
    public float          EmissionBoost     => _emissionBoost;
    public float          FlashDuration     => _flashDuration;
    public int            FlashPulses       => _flashPulses;
    public AnimationCurve FlashCurve        => _flashCurve;
    public bool           UseOutlineFlash   => _useOutlineFlash;

    public bool           UsePointLight     => _usePointLight;
    public Color          LightColor        => _lightColor;
    public float          LightIntensity    => _lightIntensity;
    public float          LightRange        => _lightRange;
    public Vector3        LightLocalOffset  => _lightLocalOffset;
    public float          LightDuration     => _lightDuration;
    public AnimationCurve LightCurve        => _lightCurve;

    public bool           UseHitReaction        => _useHitReaction;
    public float          FlinchTiltAngle       => _flinchTiltAngle;
    public float          FlinchBackOffset      => _flinchBackOffset;
    public float          FlinchDuration        => _flinchDuration;
    public float          CritReactionMultiplier => _critReactionMultiplier;
    public AnimationCurve FlinchCurve           => _flinchCurve;
}
