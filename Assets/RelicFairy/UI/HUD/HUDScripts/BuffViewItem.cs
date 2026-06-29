using UnityEngine;

/// <summary>
/// 지속 상태(버프창) 표시용 뷰모델 1건. 동작/밸런스 상태가 아니라 "한 줄로 어떻게 보일지"만 담는다.
///
/// 분산된 지속 효과 소스(방버프/룬 리소스/유물 메커닉 등)를 <see cref="BuffViewAggregator"/>가
/// 이 단일 모델로 수집하면, 표시 레이어(CombatPanelView)는 소스 종류를 몰라도 동일하게 그린다.
///
/// 어휘 통일: IconKey는 <see cref="EffectIconRegistry"/>/<see cref="EffectMetaRegistry"/>와 동일 키 문자열,
/// 라벨/수치는 <see cref="EffectDescriptionFormatter"/>를 재사용해 만든다.
///
/// P1 범위: 방버프만 채운다. Stacks/Remaining01은 P4(스택 배지·게이지) 전까지 표시에 쓰지 않는다.
/// </summary>
public readonly struct BuffViewItem
{
    /// <summary>아이콘 해석 키(EffectIconRegistry 어휘). 예: "atk", "speed".</summary>
    public readonly string IconKey;
    /// <summary>한글 라벨 + 수치까지 포함한 본문. 예: "공격력 +10%".</summary>
    public readonly string Label;
    /// <summary>중첩 수(없으면 1). P4 스택 배지용 — P1 표시 미사용.</summary>
    public readonly int Stacks;
    /// <summary>잔여 비율 0~1(게이지). 무한/해당없음이면 -1. P4 게이지용 — P1 표시 미사용.</summary>
    public readonly float Remaining01;
    /// <summary>잔여 표기. 예: "[3방]" / "" (없음).</summary>
    public readonly string RemainText;
    /// <summary>수집 출처(색/그룹 힌트).</summary>
    public readonly BuffSource Source;
    /// <summary>디버프 여부(배경 톤). 방버프 배경색 회귀 방지에 필요.</summary>
    public readonly bool IsDebuff;

    public BuffViewItem(string iconKey, string label, int stacks, float remaining01,
                        string remainText, BuffSource source, bool isDebuff)
    {
        IconKey = iconKey;
        Label = label;
        Stacks = stacks < 1 ? 1 : stacks;
        Remaining01 = remaining01;
        RemainText = remainText;
        Source = source;
        IsDebuff = isDebuff;
    }

    /// <summary>표시 본문(라벨 + 잔여). 기존 CombatPanelView 표기와 동일하게 합친다.</summary>
    public string DisplayText
        => string.IsNullOrEmpty(RemainText) ? Label : $"{Label} {RemainText}";

    /// <summary>
    /// 행 "구조"가 동일한지(라벨/아이콘/잔여텍스트/디버프/소스 + 스택배지·게이지 유무).
    /// true면 전량 재생성 없이 값만 in-place 갱신 가능. false면 행 재구성 필요.
    /// </summary>
    public bool SameStructure(in BuffViewItem o)
        => IsDebuff == o.IsDebuff
        && Source == o.Source
        && (Stacks > 1) == (o.Stacks > 1)              // 스택 배지 유무
        && (Remaining01 >= 0f) == (o.Remaining01 >= 0f) // 게이지 유무
        && string.Equals(IconKey, o.IconKey)
        && string.Equals(Label, o.Label)
        && string.Equals(RemainText, o.RemainText);

    /// <summary>구조 동일 시, 동적 값(스택 수/게이지)이 동일한지. 둘 다 같으면 갱신 스킵.</summary>
    public bool SameValues(in BuffViewItem o)
        => Stacks == o.Stacks && Mathf.Abs(Remaining01 - o.Remaining01) <= 0.005f;

    /// <summary>
    /// 방버프(ActiveRoomBuff) → 뷰모델. 기존 표기와 바이트 동일하도록
    /// 라벨/수치/잔여를 <see cref="EffectDescriptionFormatter"/> 프리미티브로 동일 생성한다.
    /// (기존: FormatStatBuff = "{StatLabel} {value} [N방]" → Label="{StatLabel} {value}", RemainText="[N방]")
    /// </summary>
    public static BuffViewItem FromRoomBuff(ActiveRoomBuff buff)
    {
        var type = buff.Modifier.Type;
        float value = buff.Modifier.Value;

        string label = EffectDescriptionFormatter.StatLabel(type);
        string valueStr = buff.IsPercent
            ? EffectDescriptionFormatter.FormatValue(EffectUnit.Ratio, value)
            : EffectDescriptionFormatter.FormatValue(EffectUnit.Flat, value);

        string remainText = buff.RoomsRemaining > 0 ? $"[{buff.RoomsRemaining}방]" : "";
        float remaining01 = buff.BaseDuration > 0
            ? (float)buff.RoomsRemaining / buff.BaseDuration
            : -1f;

        return new BuffViewItem(
            iconKey:     EffectDescriptionFormatter.IconKeyForStat(type),
            label:       $"{label} {valueStr}",
            stacks:      1,
            remaining01: remaining01,
            remainText:  remainText,
            source:      BuffSource.Room,
            isDebuff:    buff.IsDebuff);
    }
}

/// <summary>지속 버프의 수집 출처. P2에서 Rune/Relic을 실제로 채운다.</summary>
public enum BuffSource
{
    Room,
    Item,
    Rune,
    Relic,
    Status,
}
