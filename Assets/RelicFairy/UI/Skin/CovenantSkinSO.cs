using UnityEngine;

/// <summary>
/// 서약 조립 팝업(<see cref="UI_CovenantAssemble"/>) 아트 슬롯.
/// 디자이너 개편본(바탕화면 "서약 UI 리소스", 메모: 양피지 + 등급 테두리 추가)과 1:1.
///
/// 미할당 슬롯은 무시되어 프리팹에 이미 꽂힌 구 아트가 그대로 남는다.
/// Addressable 키 "UI/CovenantSkin"으로 1회 로드해 캐싱한다(<see cref="UISkin"/>). <see cref="RefinerySkinSO"/> 관례.
///
/// ※ <b>등급 테두리는 조각 조립식이다</b> — 가로 장식바(위·아래)와 모서리 조각(네 귀퉁이)을
///   카드 크기에 맞춰 코드가 배치한다. 한 장짜리 액자가 아니라서 9-slice로는 안 된다.
/// ※ 납품본은 7등급(철·그린·블루·보라·루비·골드·빛)인데 <see cref="CovenantTier"/>는 3단계뿐이다.
///   현재는 철→Silver / 골드→Gold / 루비→Ruby만 배선하고 나머지 4종은 대기시킨다.
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/UI/Covenant Skin", fileName = "CovenantSkin")]
public sealed class CovenantSkinSO : ScriptableObject
{
    [Header("판")]
    [Tooltip("서약서 전체 배경(양피지 두루마리). 원본 비율 1292:919를 지켜야 말린 양끝이 안 찌그러진다.")]
    public Sprite panelBg;
    [Tooltip("중앙 결과 두루마리 — 세로로 펼친 양피지.")]
    public Sprite resultScroll;

    [Header("열 머리표 (글자가 아트에 구워져 있다)")]
    public Sprite headerCause;    // 원인
    public Sprite headerResult;   // 결과
    public Sprite headerEffect;   // 효과

    [Header("카드 바탕")]
    public Sprite cardIdle;       // 비선택 바탕(밝은 양피지)
    public Sprite cardSelected;   // 선택 바탕(어두운 판)

    [Header("등급 테두리 조각 — CovenantTier 순서(0=Silver 1=Gold 2=Ruby)")]
    [Tooltip("위·아래 가로 장식바.")]
    public Sprite[] gradeBar    = new Sprite[3];
    [Tooltip("네 귀퉁이 모서리 조각. 좌상단 모양 하나로 나머지 셋은 코드가 뒤집어 쓴다.")]
    public Sprite[] gradeCorner = new Sprite[3];

    public Sprite GradeBar(CovenantTier t)    => Pick(gradeBar, (int)t);
    public Sprite GradeCorner(CovenantTier t) => Pick(gradeCorner, (int)t);

    /// <summary>등급 테두리 조각이 갖춰졌나. 하나라도 없으면 카드는 기존 한 장짜리 액자를 쓴다.</summary>
    public bool HasGradeFrames => gradeBar != null && gradeCorner != null &&
                                  gradeBar.Length > 0 && gradeCorner.Length > 0 &&
                                  gradeBar[0] != null && gradeCorner[0] != null;

    private static Sprite Pick(Sprite[] arr, int i)
        => (arr == null || i < 0 || i >= arr.Length) ? null : arr[i];
}
