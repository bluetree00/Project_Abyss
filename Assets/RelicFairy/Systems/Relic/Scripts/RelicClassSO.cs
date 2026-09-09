using UnityEngine;

/// <summary>능력 종류 — 카드의 강조색과 기본 배지를 결정한다.</summary>
public enum RelicAbilityKind
{
    Passive,   // 상시
    State,     // 조건부 강화 상태(광란·정오 등)
    Skill,     // 고유 스킬(Q)
}

/// <summary>
/// 유물 정보 팝업의 '능력 카드' 한 장.
///
/// 긴 산문 대신 <b>스캔 가능한 조각</b>으로 쪼갠다 — 제단 앞에서 몇 초 안에 비교해야 하는 UI라,
/// 문단을 읽히면 아무도 안 읽는다.
///
/// <b>작성 규칙 — 관용적인 RPG 툴팁 표기로 쓴다.</b>
/// 라벨은 플레이어가 아는 <b>스탯/효과 이름</b>, 값은 <b>정확한 수치</b>. 서술형 수식어를 붙이지 않는다.
/// "쌓을수록 올라", "절반만큼" 같은 말은 읽는 데 시간만 더 들고 정확하지도 않다.
///   ✗ "공격력 증가 | 쌓을수록 올라 최대 +40%"   ○ "공격력 | 스택당 +1% (최대 +40%)"
///   ✗ "체력 회복 | 준 피해의 절반만큼"          ○ "체력 회복 | 입힌 피해의 50%"
///   ✗ "거대 피해 | 한 번에 크게"                ○ "피해 | 공격력 × 3.5"
/// 계산식을 그대로 노출해도 좋다 — 빌드를 짜는 플레이어에게는 그게 가장 정확한 정보다.
/// </summary>
[System.Serializable]
public class RelicAbilityInfo
{
    [Tooltip("능력 이름 (예: 광기)")]
    public string name;

    [Tooltip("종류 — 카드 강조색과 기본 배지를 정한다.")]
    public RelicAbilityKind kind;

    [Tooltip("우측 배지. 비우면 종류에 따라 자동(패시브 / 상태 / Q).")]
    public string badge;

    [Tooltip("한두 줄 요약 — 발동 조건과 무슨 일이 일어나는지. 수치는 아래 stats에 맡긴다.")]
    [TextArea(1, 3)] public string summary;

    [Tooltip("수치 표. 형식 = \"라벨|값\".\n" +
             "라벨 = 스탯/효과 이름 (공격력 / 받는 피해 / 체력 회복 / 이동 속도 …)\n" +
             "값   = 정확한 수치 표기. 서술형 수식어 금지.\n" +
             "  ○ \"공격력|스택당 +1% (최대 +40%)\"\n" +
             "  ○ \"체력 회복|입힌 피해의 50%\"\n" +
             "  ○ \"피해|공격력 × (2.0 + 스택 × 0.08)\"\n" +
             "  ✗ \"공격력 증가|쌓을수록 올라 최대 +40%\"\n" +
             "세로줄이 없으면 한 줄 그대로 나간다. 숫자는 UI가 금색으로 강조한다. 2~4개 권장.")]
    public string[] stats;
}

/// <summary>
/// 유물 Q 애니메이션 한 단계. 여러 단계를 <b>순서대로 이어 재생</b>해 다단 모션을 만든다.
///
/// <b>stateName은 오버라이드 키를 겸한다.</b> AnimatorOverrideController의 키는 상태 이름이 아니라
/// 그 상태가 물고 있는 <b>원본 클립의 이름</b>이므로, 둘이 같도록 컨트롤러를 구성해야 한다
/// (QSkill_01 상태 = QSkill_01 클립, RelicQ_Slash2 상태 = RelicQ_Slash2 클립 …).
/// </summary>
[System.Serializable]
public class RelicQAnimStep
{
    [Tooltip("애니메이터 상태 이름. 컨트롤러 원본 클립 이름과 같아야 한다(오버라이드 키 겸용).")]
    public string stateName;

    [Tooltip("이 상태에 물릴 클립의 Addressables 키. 비우면 컨트롤러 기본 클립을 그대로 쓴다.")]
    public string clipKey;
}

/// <summary>
/// 유물 클래스 정의(정적 데이터). 몸은 CombatGirl 고정, 유물이 패시브 + 고유스킬 + 외형(오라)만 부여.
/// 스탯은 부여하지 않는다(베이스 스탯은 CombatGirl 공통).
/// 코드가 필요한 패시브/스킬 로직은 <see cref="RelicId"/> 기반 팩토리(RelicRegistry)에서 처리한다.
/// </summary>
[CreateAssetMenu(fileName = "RelicClass", menuName = "RelicFairy/Relic Class")]
public class RelicClassSO : ScriptableObject
{
    [Header("식별")]
    [SerializeField] private RelicId id;
    [SerializeField] private string displayName;
    [SerializeField, TextArea] private string loreDesc;

    [Header("정보 팝업 (선택 화면)")]
    // 텍스트의 정본을 여기(SO)로 둔다 — 여태 각 behavior의 private const에 하드코딩돼 있어 UI가 못 읽었다.
    // 산문이 아니라 '스캔 가능한 조각'으로 쪼갠다: 한 줄 컨셉 + 태그 + 능력 카드.
    [SerializeField, Tooltip("한 줄 컨셉 (예: 찢긴 서약의 검)")]
    private string tagline;

    [SerializeField, Tooltip("성격 태그 (예: 스택형 / 고위험 고보상). 3개 정도가 적당하다.")]
    private string[] tags;

    [SerializeField, Tooltip("능력 카드 — 패시브·상태·Q스킬 순서로 넣는 것을 권장.")]
    private RelicAbilityInfo[] abilities;

    [Header("선택 UI")]
    // 유물 톤 색 — 팝업의 제목·강조·테두리가 이 색을 따른다. 유물마다 정체성이 다르게 보이게 한다.
    // (a<=0이면 팝업 기본 금색 사용 → 미설정 유물도 정상 동작.)
    [SerializeField, Tooltip("팝업 테마색. 유물 정체성 색(가웨인=태양금, 랜슬롯=핏빛 등). 비우면 기본 금색.")]
    private Color themeColor = new Color(0f, 0f, 0f, 0f);
    [SerializeField] private Sprite portrait;
    [SerializeField, Tooltip("유물 선택 팝업 전신 일러스트")] private Sprite rosterIllust;

    [Header("패시브 (순수 데이터). 코드형 패시브는 RelicId 팩토리에서 등록")]
    [SerializeField] private PassiveSO[] passives;

    [Header("고유 스킬 (Q)")]
    [SerializeField, Tooltip("Q스킬 'QSkill_01' state 클립으로 쓸 Addressables 키. 아래 시퀀스가 있으면 무시된다.")]
    private string qSkillClipKey;

    [SerializeField, Tooltip("Q 애니 시퀀스 — 순서대로 이어 재생해 다단 모션을 만든다. 비우면 위 단일 클립 키(QSkill_01)를 쓴다.")]
    private RelicQAnimStep[] qSkillClipSequence;
    [SerializeField, Tooltip("Q 단독 모션(가웨인 캐스트·랜슬롯 마무리) 상태. 비우면 QSkill_01 — 무기 R 스킬이 덮어쓰는 공용 상태라 든 무기에 따라 모션이 바뀐다. 전용 상태 RelicQ_Main 권장")]
    private string qSkillMainState = DefaultQSkillState;
    [SerializeField, Tooltip("위 상태에 물릴 클립 Addressables 키. 비우면 컨트롤러 기본 클립")]
    private string qSkillMainClipKey;
    [SerializeField, Tooltip("Q 입력 시 카메라 연출. 비우면 연출 생략")]
    private UltimateCinematicConfig qSkillCinematic;

    [Header("스탯 (공통 CombatGirl 베이스 위에 가산)")]
    [SerializeField] private StatModifier[] stats;

    [Header("외형 (초기엔 오라만)")]
    [SerializeField, Tooltip("상시 오라 VFX Addressables 키. 비우면 없음")]
    private string auraVfxKey;
    [SerializeField, Tooltip("오라를 붙일 본/소켓 이름 (비우면 루트)")]
    private string auraSocket;

    public RelicId Id => id;
    public string DisplayName => displayName;
    public string LoreDesc => loreDesc;
    public string Tagline => tagline;
    public string[] Tags => tags;
    public RelicAbilityInfo[] Abilities => abilities;
    /// <summary>팝업 테마색. 미설정(a≤0)이면 기본값(fallback)을 그대로 돌려준다.</summary>
    public Color ThemeColor(Color fallback) => themeColor.a > 0.01f ? themeColor : fallback;
    public Sprite Portrait => portrait;
    public Sprite RosterIllust => rosterIllust;
    public PassiveSO[] Passives => passives;
    public StatModifier[] Stats => stats;
    public string QSkillClipKey => qSkillClipKey;
    public UltimateCinematicConfig QSkillCinematic => qSkillCinematic;
    /// <summary>Q 단독 모션 상태(비우면 QSkill_01 폴백). 시퀀스(연타)와 별개 — 캐스트·마무리처럼 한 번 재생하는 모션.</summary>
    public string QSkillMainState => string.IsNullOrEmpty(qSkillMainState) ? DefaultQSkillState : qSkillMainState;
    public string QSkillMainClipKey => qSkillMainClipKey;

    /// <summary>시퀀스 미설정 유물이 쓰는 기본 Q 상태 — 기존 하드코딩과 같은 값(폴백).</summary>
    public const string DefaultQSkillState = "QSkill_01";

    private bool HasQSequence => qSkillClipSequence != null && qSkillClipSequence.Length > 0;

    /// <summary>Q 애니가 이어 재생할 단계 수. 시퀀스가 없으면 1(기본 상태 하나).</summary>
    public int QSkillStepCount => HasQSequence ? qSkillClipSequence.Length : 1;

    /// <summary>i번째 단계에서 재생할 애니메이터 상태 이름. 범위를 벗어나면 양끝으로 물린다.</summary>
    public string QSkillStateAt(int i)
    {
        if (!HasQSequence) return DefaultQSkillState;
        var step = qSkillClipSequence[Mathf.Clamp(i, 0, qSkillClipSequence.Length - 1)];
        return string.IsNullOrEmpty(step?.stateName) ? DefaultQSkillState : step.stateName;
    }

    /// <summary>i번째 단계 상태에 물릴 클립의 Addressables 키. 비어 있으면 컨트롤러 기본 클립 유지.</summary>
    public string QSkillClipKeyAt(int i)
    {
        if (!HasQSequence) return i == 0 ? qSkillClipKey : null;
        return qSkillClipSequence[Mathf.Clamp(i, 0, qSkillClipSequence.Length - 1)]?.clipKey;
    }
    public string AuraVfxKey => auraVfxKey;
    public string AuraSocket => auraSocket;
}
