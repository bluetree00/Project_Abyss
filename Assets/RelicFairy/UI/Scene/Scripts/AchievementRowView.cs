using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>업적 1행의 상태. 뷰는 이 값만 보고 그린다.</summary>
public struct AchievementRowState
{
    public Quest  Quest;
    public string DisplayName;
    public string ConditionText;   // 조건 + 진척 숫자
    public int    Reward;          // 정수 보상액
    public float  Progress01;      // 0~1
    public bool   Claimable;       // 달성했으나 미수령
    public bool   Claimed;         // 수령 완료
}

/// <summary>
/// 기억의 제단 「업적」 탭의 행 1개.
/// 레이아웃: <c>기호48 | 이름280 | 조건·진척380 | 보상120 | 액션160</c>, 행 높이 72.
///
/// <para><b>핵심은 「액션」 열이다.</b> 상태가 무엇이든 같은 x좌표에 놓이므로,
/// 목록이 길어져도 눈은 <b>우측 한 열만</b> 훑으면 된다 — 가로 스캔이 필요 없다.</para>
///
/// <para><b>진척은 숫자와 막대를 함께</b> 보여준다. 숫자만으론 "얼마나 가까운지"가 훑어서 안 읽힌다.
/// 목표에 가까울수록 동기가 커지므로(goal-gradient) 근접도가 한눈에 보여야 한다.</para>
/// </summary>
public class AchievementRowView : MonoBehaviour
{
    // ── 직렬화 필드 ──────────────────────────────────────────────────────
    [Header("배경 · 기호")]
    [SerializeField] private Image    background;
    [SerializeField] private TMP_Text symbolText;
    [Tooltip("좌측 표식 그림(✦/▶/✔). 비어 있으면 symbolText 글자를 그대로 쓴다.")]
    [SerializeField] private Image    markImage;

    [Header("본문")]
    [SerializeField] private TMP_Text nameText;
    [SerializeField] private TMP_Text conditionText;
    [SerializeField] private Image    progressFill;
    [SerializeField] private TMP_Text rewardText;

    [Header("액션 열")]
    [SerializeField] private Button   claimButton;
    [SerializeField] private Image    claimButtonImage;
    [SerializeField] private TMP_Text claimLabel;
    [SerializeField] private TMP_Text statusText;   // 진척 % / ✔ — 버튼과 같은 자리

    // ── 비공개 필드 ──────────────────────────────────────────────────────
    private Action _onClaim;

    // ── Public Methods ───────────────────────────────────────────────────

    public void Refresh(AchievementRowState s)
    {
        RefreshTone(s);
        RefreshBody(s);
        RefreshAction(s);
    }

    public void SetOnClaim(Action callback)
    {
        _onClaim = callback;
        if (claimButton == null) return;

        claimButton.onClick.RemoveAllListeners();
        claimButton.onClick.AddListener(() => _onClaim?.Invoke());
    }

    // ── Private Methods ──────────────────────────────────────────────────

    private void RefreshTone(AchievementRowState s)
    {
        var skin = UISkin.Achievement;

        if (background)
        {
            // 상태마다 <b>판이 다르다</b>. 색만 바꾸던 시절엔 "받을 수 있다"가 목록에서 안 튀어
            // 수령을 놓쳤다 — 판 자체가 다르면 훑기만 해도 걸린다.
            var art = skin?.Row(s.Claimable, s.Claimed);
            if (art != null)
            {
                background.sprite = art;
                background.type   = Image.Type.Sliced;
                background.color  = Color.white;
            }
            else
            {
                background.sprite = null;
                background.color  = s.Claimable ? AltarPalette.RowClaimable
                                  : s.Claimed   ? AltarPalette.RowClaimed
                                                : AltarPalette.RowIdle;
            }
        }

        // 좌측 표식 — 아트가 있으면 글자 대신 그림을 쓴다.
        var mark = skin?.Mark(s.Claimable, s.Claimed);
        if (markImage != null)
        {
            markImage.gameObject.SetActive(mark != null);
            if (mark != null)
            {
                markImage.sprite        = mark;
                markImage.color         = Color.white;
                markImage.preserveAspect = true;
            }
        }

        if (symbolText)
        {
            // 아트 표식이 자리를 대신하면 글자는 감춘다(둘이 겹치면 기호가 두 개로 보인다).
            symbolText.gameObject.SetActive(mark == null || markImage == null);
            symbolText.text  = s.Claimable ? "◆" : s.Claimed ? "■" : "▶";
            symbolText.color = s.Claimable ? AltarPalette.Gold
                             : s.Claimed   ? AltarPalette.TextFaint
                                           : AltarPalette.TextDim;
        }
    }

    private void RefreshBody(AchievementRowState s)
    {
        if (nameText)
        {
            nameText.text  = s.DisplayName;
            // 달성한 것은 밝게, 미달성은 흐리게 — 훑어서 구분되는 최소 신호.
            nameText.color = s.Claimed ? AltarPalette.TextFaint
                           : s.Claimable ? AltarPalette.TextPrimary
                                         : AltarPalette.TextDim;
        }

        if (conditionText)
        {
            conditionText.text  = s.ConditionText;
            conditionText.color = s.Claimed ? AltarPalette.TextFaint : AltarPalette.TextDim;
        }

        // 게이지 아트 — 채움/바탕/테두리 세 겹. 채움은 fillAmount로 늘어나므로 가로 9-slice가 맞다.
        ApplyGaugeArt();

        if (progressFill)
        {
            // 달성/수령 행에서는 막대가 정보가 아니라 잡음이 된다.
            progressFill.transform.parent.gameObject.SetActive(!s.Claimable && !s.Claimed);
            progressFill.fillAmount = Mathf.Clamp01(s.Progress01);
        }

        if (rewardText)
        {
            rewardText.text  = $"+{s.Reward:N0}";
            rewardText.color = s.Claimed ? AltarPalette.TextFaint : AltarPalette.Essence;
        }
    }

    /// <summary>버튼과 진척 표시가 <b>같은 자리</b>를 나눠 쓴다 — 상태에 따라 내용만 바뀐다.</summary>
    /// <summary>
    /// 진척 게이지에 아트를 입힌다. 한 번만 하면 되지만 <see cref="Refresh"/>가 여러 번 불려도
    /// 같은 스프라이트를 다시 넣을 뿐이라 안전하다(스킨이 늦게 로드될 수도 있어 매번 확인한다).
    /// </summary>
    private void ApplyGaugeArt()
    {
        var skin = UISkin.Achievement;
        if (skin == null || progressFill == null) return;

        if (skin.gaugeFill != null)
        {
            progressFill.sprite = skin.gaugeFill;
            progressFill.type   = Image.Type.Filled;
            progressFill.color  = Color.white;
        }

        // 바탕은 채움의 부모(Bar_BG)다.
        if (skin.gaugeTrack != null &&
            progressFill.transform.parent != null &&
            progressFill.transform.parent.TryGetComponent<Image>(out var track))
        {
            track.sprite = skin.gaugeTrack;
            track.type   = Image.Type.Sliced;
            track.color  = Color.white;
        }
    }

    private void RefreshAction(AchievementRowState s)
    {
        // [받기] 버튼 아트 — 없으면 기존 색 버튼 그대로.
        var btnArt = UISkin.Achievement?.claimButton;
        if (btnArt != null && claimButtonImage != null)
        {
            claimButtonImage.sprite = btnArt;
            claimButtonImage.type   = Image.Type.Sliced;
            claimButtonImage.color  = Color.white;
        }

        if (claimButton) claimButton.gameObject.SetActive(s.Claimable);
        if (statusText)  statusText.gameObject.SetActive(!s.Claimable);

        if (s.Claimable)
        {
            if (claimButtonImage) claimButtonImage.color = AltarPalette.Gold;
            if (claimLabel)
            {
                claimLabel.text  = "받기";
                claimLabel.color = AltarPalette.OnGold;
            }
            return;
        }

        if (statusText == null) return;

        if (s.Claimed)
        {
            statusText.text  = "■";
            statusText.color = AltarPalette.TextFaint;
        }
        else
        {
            statusText.text  = s.Progress01 > 0f ? $"{Mathf.FloorToInt(s.Progress01 * 100f)}%" : "—";
            statusText.color = AltarPalette.TextDim;
        }
    }
}
