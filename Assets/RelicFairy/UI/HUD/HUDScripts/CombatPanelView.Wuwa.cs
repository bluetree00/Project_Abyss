using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

// 명조형 HUD(10-08) — 사용자 「HUD도 명조 UX와 비슷하게 · UI가 변하는 느낌 · 중앙 핵심 게이지 변화 · 가시성」, 시안 확정(「진행해보자」).
// 시안 = 바탕화면 기획/HUD_명조형_20261008/. 톤 v2(ThinHud) 위에서 배치 · 모양 · 움직임만 바꾼다(WuwaHud = false면 톤 v2 배치 그대로).
//   아래 가운데 한 묶음: 유물 게이지(가운데에서 양옆으로 차오름 · 정오/절정에 굵어지고 빛) · 얇은 체력바(숫자 아래) · 공 · 방 오른쪽 · 부활 왼쪽 ·
//                        지금 든 무기(체력 왼쪽 큰 원) + 바꿀 무기(작은 원).
//   오른쪽 아래: 물약 C · E · R · Q(유물) 원형 칸 — 재사용 대기 = 남은 초(10초 미만 소수 한 자리), Q 바깥 고리 = 유물 게이지.
//   유물 컨셉 그림(가웨인 태양 · 랜슬롯 검 실루엣 · 유물 Q 아이콘)은 그대로 — 자리 · 크기만 옮긴다.
//   버프 · 서약(왼쪽 기둥)은 무기가 빠진 왼쪽 아래부터 쌓인다(HudView.LayoutLeftColumn 그대로).
public sealed partial class CombatPanelView
{
    // ── Constants ──
    /// <summary>명조형 배치 스위치(톤 v2 위에서만).</summary>
    internal static readonly bool WuwaHud = ThinHud;

    // 아래 가운데(화면 가운데 · 바닥에서, 1080 기준 px) — 시안 좌표
    // 10-11 위계 — 체력바 6 → 10(유물 게이지 6 · 정오 8보다 굵게, 체력이 1순위). 비교 게임 두께 10~29 · 명조 12 · 엘든 링 10.
    private const float WHpY = 50f, WHpW = 600f, WHpH = 10f;
    private const float WHpTextSize = 17f;   // 체력 숫자 14 → 17(몸통 10 → 약 12px, 명조 12.5~13)
    private const float WKeySize    = 15f;   // 키 · 강화 단계 · 물약 개수 14 → 15(글자 단계 15 · 17 · 20 · 24 · 48 안으로)
    private const float WGaugeY = 96f, WGaugeW = 440f, WGaugeH = 6f, WGaugeNoonH = 8f, WGaugeGap = 80f;
    private const float WEmblem = 56f, WEmblemNoon = 92f, WGlowSize = 170f;
    // 10-10 사용자 「장비 · 포션 · 스킬 칸이 좀 더 커져야」 — 약 1.3배. 위치는 바깥 여백(오른쪽 끝 · 바닥선)과 이웃 간격 14를 지켜 다시 셈.
    private static readonly Vector2 WWeaponBigPos = new(-398f, 78f), WWeaponSmallPos = new(-468f, 60f);
    private const float WWeaponBig = 78f, WWeaponSmall = 44f;
    // 오른쪽 아래(오른쪽 끝 · 바닥에서)
    private static readonly Vector2 WQPos = new(-105f, 103f), WRPos = new(-230f, 90f), WEPos = new(-332f, 90f), WCPos = new(-422f, 79f);
    private const float WQSize = 114f, WERSize = 88f, WCSize = 64f, WQRingGap = 7f, WQRingStroke = 3f;
    private const float WDockBottom = 40f;   // 왼쪽 기둥(버프 · 서약) 바닥 — 무기가 빠진 왼쪽 아래
    private const float WStreakSec = 0.3f;

    private static readonly Color WTrack = new(0.03f, 0.03f, 0.05f, 0.67f);
    private static readonly Color WNoon  = new(1f, 0.925f, 0.667f, 1f);    // 정오 · 절정 — 흰 금빛
    private static readonly Color WDisc  = new(0.063f, 0.059f, 0.086f, 0.65f);

    // ── Private ──
    private RectTransform _wGaugeHost;                  // 게이지 자리(태양 = _sunRoot · 검 = _relicBar)
    private System.Threading.CancellationTokenSource[] _weaponSwapCts;   // 무기 원마다 도는 자리 바꿈(0.18초) — 새로 놓을 때 앞 것을 끊는다(10-14)
    private Image _wGaugeL, _wGaugeR, _wTipL, _wTipR, _wGlowSun, _wGlowBar;   // 빛은 게이지 모양마다(태양 · 검) 따로
    private Image _wQRing, _wQGlow, _wStreak;
    private Image _wHpTrack;
    private RectTransform _wQSlot;

    // ── Private Methods ──

    /// <summary>Awake 끝(톤 v2 스킨 뒤) 한 번 — 배치 · 원형 칸 · 게이지 모양.</summary>
    private void ApplyWuwaLayout()
    {
        LayoutWuwaHp();
        LayoutWuwaGauge();
        LayoutWuwaSkills();
        LayoutWuwaWeapons();
    }

    // ── 체력 · 공방 · 부활 ──

    private void LayoutWuwaHp()
    {
        if (_hpBar == null) return;
        _hpBar.pivot            = new Vector2(0.5f, 0.5f);
        _hpBar.anchoredPosition = new Vector2(0f, WHpY);
        _hpBar.sizeDelta        = new Vector2(WHpW, WHpH);
        if (_hpFrameImg != null) _hpFrameImg.enabled = false;   // 장식 테 없이 얇은 한 줄

        if (FindChildRecursive(_hpBar, "Background") is RectTransform bg)
        {
            InsetInside(bg, Vector4.zero);
            if (bg.TryGetComponent(out _wHpTrack)) { _wHpTrack.sprite = null; _wHpTrack.type = Image.Type.Simple; _wHpTrack.color = WTrack; }
        }
        var fillArea = hpSlider != null && hpSlider.fillRect != null ? hpSlider.fillRect.parent as RectTransform : null;
        if (fillArea != null) InsetInside(fillArea, Vector4.zero);
        if (hpGhostFillImage != null)
        {
            hpGhostFillImage.sprite = UIThinShapes.Solid;
            hpGhostFillImage.color  = UITheme.Thin.InkA(0.45f);
        }

        if (hpText != null)   // 숫자 = 바 아래 가운데(명조)
        {
            var rt = hpText.rectTransform;
            rt.SetParent(_hpBar, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot            = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -5f);
            rt.sizeDelta        = new Vector2(220f, 24f);
            UITheme.StyleThinText(hpText, UIWeight.Medium, WHpTextSize, UITheme.Thin.Ink, shadow: true);
            hpText.alignment = TextAlignmentOptions.Top;
        }

        if (_statRoot != null)   // 공 · 방 = 바 오른쪽 끝(명조 레벨 자리)
        {
            var rt = (RectTransform)_statRoot.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(1f, 0.5f);
            rt.pivot            = new Vector2(0f, 0.5f);
            rt.anchoredPosition = new Vector2(16f, 0f);
            rt.sizeDelta        = new Vector2(150f, 22f);
            PlaceStat(_atkText, 20f);
            PlaceStat(_defText, 86f);
        }

        if (_reviveBadge != null)   // 부활 = 바 왼쪽 끝 위 작은 알약
        {
            // 바 왼쪽(-38)은 든 무기 원(-392 ± 30)과 20px 겹쳤다(10-08 실측 HG) — 바 왼쪽 끝 위로 올린다
            _reviveBadge.pivot            = new Vector2(0f, 0f);
            _reviveBadge.anchoredPosition = new Vector2(0f, 8f);
            _reviveBadge.sizeDelta        = new Vector2(44f, 20f);
            if (_reviveLabel != null) UITheme.StyleThinText(_reviveLabel, UIWeight.Medium, UITheme.Thin.Caption, _reviveLabel.color);   // 12 → 13(글자 단계)
        }
    }

    /// <summary>
    /// 공 · 방 — 글자는 잉크 85%, 색(공 주황 · 방 파랑)은 16px 아이콘에만(10-11, 한 화면 강조색 6 → 4).
    /// 아이콘 그림은 흰 원본이라 글자에 있던 색을 그대로 옮겨 칠한다.
    /// </summary>
    private static void PlaceStat(TMP_Text label, float x)
    {
        if (label == null) return;
        var hue = label.color;
        var rt = label.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot            = new Vector2(0f, 0.5f);
        rt.anchoredPosition = new Vector2(x, 0f);
        rt.sizeDelta        = new Vector2(48f, 22f);
        UITheme.StyleThinText(label, UIWeight.Medium, 15f, UITheme.Thin.InkA(0.85f), shadow: true);
        if (rt.Find("StatIcon") is RectTransform icon)   // 아이콘도 글자 크기로(스킨은 34px)
        {
            icon.sizeDelta        = new Vector2(16f, 16f);
            icon.anchoredPosition = new Vector2(-3f, 0f);
            if (icon.TryGetComponent<Image>(out var iconImg)) iconImg.color = hue;
        }
    }

    // ── 유물 게이지(가운데) ──

    private void LayoutWuwaGauge()
    {
        if (_sunRoot != null)
        {
            // 태양형(가웨인) — 자식은 (0.5, 0) 기준 중심 y = SunCenterY로 놓여 있어, 높이 100이면 가운데다.
            _sunRoot.pivot            = new Vector2(0.5f, 0.5f);
            _sunRoot.anchoredPosition = new Vector2(0f, WGaugeY);
            _sunRoot.sizeDelta        = new Vector2(WGaugeW + 40f, SunCenterY * 2f);
            _sunRoot.SetAsLastSibling();   // 체력바 뒤로 숨기던 큰 태양이 아니다 — 위에 둔다

            if (_sunGaugeRoot != null)
            {
                _sunGaugeRoot.sizeDelta = new Vector2(WGaugeW, WGaugeH);
                if (_sunGaugeRoot.TryGetComponent<Image>(out var track)) track.color = WTrack;
                if (_sunGaugeFill != null) _sunGaugeFill.enabled = false;   // 한쪽 채움 → 가운데에서 양옆(아래 두 반쪽)
                _wGaugeL = GaugeHalf(_sunGaugeRoot, "FillL", left: true);
                _wGaugeR = GaugeHalf(_sunGaugeRoot, "FillR", left: false);
                _wTipL   = GaugeTip(_sunGaugeRoot, "TipL");
                _wTipR   = GaugeTip(_sunGaugeRoot, "TipR");
                EndTick(_sunGaugeRoot, 0f);
                EndTick(_sunGaugeRoot, 1f);
            }
            if (_sunOpenImg   != null) _sunOpenImg.rectTransform.sizeDelta   = Vector2.one * WEmblem;
            if (_sunClosedImg != null)
            {
                _sunClosedImg.rectTransform.sizeDelta = Vector2.one * WEmblemNoon;
                _sunClosedImg.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.35f);   // 정오 남은 시간이 비워진 자리
            }
            if (_sunClosedFill != null) _sunClosedFill.color = WNoon;
            if (_sunLabel != null)
            {
                var lrt = _sunLabel.rectTransform;
                lrt.pivot            = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(0f, SunCenterY + 40f);
                lrt.sizeDelta        = new Vector2(220f, 22f);
                UITheme.StyleThinText(_sunLabel, UIWeight.Medium, 15f, UITheme.Thin.Ink, shadow: true);
                _sunLabel.alignment = TextAlignmentOptions.Center;
            }
            _wGlowSun = MakeGlow(_sunRoot, new Vector2(0f, SunCenterY), Vector2.one * WGlowSize);
        }

        if (_relicBar != null)
        {
            // 검형(랜슬롯) — 같은 자리. 검 실루엣 채움 아트는 그대로(유물 컨셉).
            _relicBar.pivot            = new Vector2(0.5f, 0.5f);
            _relicBar.anchoredPosition = new Vector2(0f, WGaugeY);
            _relicBar.sizeDelta        = HasRelicGaugeSkin ? new Vector2(WGaugeW, WGaugeW / 7.1f) : new Vector2(WGaugeW, WGaugeNoonH);
            if (_relicBarLabel != null) UITheme.StyleThinText(_relicBarLabel, UIWeight.Medium, 15f, UITheme.Thin.Ink, shadow: true);
            _wGlowBar = MakeGlow(_relicBar, Vector2.zero, new Vector2(WGaugeW * 0.7f, WGlowSize * 0.7f));
            _wGlowBar.rectTransform.anchorMin = _wGlowBar.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
        }
    }

    /// <summary>게이지 반쪽 — 가운데 틈(문장 자리) 바깥으로 차오른다(왼쪽은 오른쪽 끝에서, 오른쪽은 왼쪽 끝에서).</summary>
    private static Image GaugeHalf(RectTransform track, string name, bool left)
    {
        var img = ShopUIStyle.MakeImage(track, name, UITheme.Thin.Gold);
        img.sprite     = UIThinShapes.Solid;
        img.type       = Image.Type.Filled;
        img.fillMethod = Image.FillMethod.Horizontal;
        img.fillOrigin = left ? (int)Image.OriginHorizontal.Right : (int)Image.OriginHorizontal.Left;
        var rt = img.rectTransform;
        rt.anchorMin = left ? Vector2.zero : new Vector2(0.5f, 0f);
        rt.anchorMax = left ? new Vector2(0.5f, 1f) : Vector2.one;
        rt.offsetMin = new Vector2(left ? 0f : WGaugeGap * 0.5f, 0f);
        rt.offsetMax = new Vector2(left ? -WGaugeGap * 0.5f : 0f, 0f);
        return img;
    }

    /// <summary>차오르는 끝의 빛 한 점 — 채움을 따라 흐른다.</summary>
    private static Image GaugeTip(RectTransform track, string name)
    {
        var img = ShopUIStyle.MakeImage(track, name, Color.clear);
        img.sprite = UI_RuneSelectPopup.SoftDot;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(16f, 16f);
        return img;
    }

    private static void EndTick(RectTransform track, float x)
    {
        var rt = ShopUIStyle.MakeImage(track, "EndTick", UITheme.Thin.WithA(UITheme.Thin.Gold, 0.55f)).rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(x, 0.5f);
        rt.pivot     = new Vector2(0.5f, 0.5f);
        rt.sizeDelta = new Vector2(UITheme.Thin.Stroke, 14f);
    }

    private static Image MakeGlow(RectTransform host, Vector2 center, Vector2 size)
    {
        var img = ShopUIStyle.MakeImage(host, "GaugeGlow", Color.clear);
        img.sprite = UI_RuneSelectPopup.SoftDot;
        var rt = img.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = center;
        rt.sizeDelta        = size;
        rt.SetAsFirstSibling();   // 문장 · 게이지 뒤
        return img;
    }

    // ── 오른쪽 아래 원형 기술 칸 ──

    private void LayoutWuwaSkills()
    {
        var q = FindChildRecursive(transform, "HUD_QSkile") as RectTransform;
        var e = FindChildRecursive(transform, "HUD_ESkile") as RectTransform;
        var r = FindChildRecursive(transform, "HUD_RSkile") as RectTransform;
        var c = FindChildRecursive(transform, "HUD_Active_01") as RectTransform;
        PlaceRound(q, WQPos, WQSize, _qThin, qInner: true,  keyBelow: WQRingGap + WQRingStroke + 4f, skillQ?.keyLabel);
        PlaceRound(e, WEPos, WERSize, _eThin, qInner: false, keyBelow: 4f, skillE?.keyLabel);
        PlaceRound(r, WRPos, WERSize, _rThin, qInner: false, keyBelow: 4f, null);
        var potionSlot = activeSlots != null && activeSlots.Length > PotionSlotIndex ? activeSlots[PotionSlotIndex] : null;
        PlaceRound(c, WCPos, WCSize, _potionThin, qInner: false, keyBelow: 4f, potionSlot?.indexLabel);

        // 재사용 대기 부채꼴 = 원 · 숫자 크기
        RestyleWuwaCooldownTexts();
        if (_rCooldownFill != null)
        {
            _rCooldownFill.sprite        = UIThinShapes.Disc(WERSize);
            _rCooldownFill.type          = Image.Type.Filled;
            _rCooldownFill.fillMethod    = Image.FillMethod.Radial360;
            _rCooldownFill.fillOrigin    = (int)Image.Origin360.Top;
            _rCooldownFill.fillClockwise = true;
        }

        // Q 바깥 고리 = 유물 게이지(명조 공명해방 고리). 트랙(어둡게) 위에 금빛 채움, 가득 차면 흰 금빛 + 바깥 빛.
        if (q != null)
        {
            _wQSlot = q;
            float d = WQSize + WQRingGap * 2f;
            _wQGlow = ShopUIStyle.MakeImage(q, "QGlow", Color.clear);
            _wQGlow.sprite = UI_RuneSelectPopup.SoftDot;
            Center(_wQGlow.rectTransform, d + 40f);
            _wQGlow.transform.SetAsFirstSibling();
            var track = ShopUIStyle.MakeImage(q, "QGaugeTrack", WTrack);
            track.sprite = UIThinShapes.Ring(d, WQRingStroke);
            Center(track.rectTransform, d);
            _wQRing = ShopUIStyle.MakeImage(q, "QGaugeRing", UITheme.Thin.Gold);
            _wQRing.sprite        = UIThinShapes.Ring(d, WQRingStroke);
            _wQRing.type          = Image.Type.Filled;
            _wQRing.fillMethod    = Image.FillMethod.Radial360;
            _wQRing.fillOrigin    = (int)Image.Origin360.Top;
            _wQRing.fillClockwise = true;
            Center(_wQRing.rectTransform, d);
        }
    }

    /// <summary>재사용 대기 부채꼴 = 원 · 숫자 = Medium 24(Q도 24 — 10-11 글자 단계). 톤 v2 글자 패스(ApplyThinTextStyles)가 Light 28로 되돌린 뒤에도 다시 부른다.</summary>
    private void RestyleWuwaCooldownTexts()
    {
        skillQ?.UseRound(UIThinShapes.Disc(WQSize), 24f);
        skillE?.UseRound(UIThinShapes.Disc(WERSize), 24f);
        if (_rCooldownTxt != null) UITheme.StyleThinText(_rCooldownTxt, UIWeight.Medium, 24f, UITheme.Thin.Ink, shadow: true);
    }

    /// <summary>
    /// 칸을 오른쪽 아래 원으로 옮긴다 — 바탕 = 원판(글래스), 테 = 원 테. 테는 얇은 테(UIThinFrame)가 이미 넘겨받은 그 Image라
    /// 켜짐(테가 시계 방향으로 그려짐) · 눌림이 원 위에서 그대로 돈다. 키 글자는 원 아래 가운데.
    /// 선 굵기 세 가지(10-11): 평소 1 · 준비됨 2(E · R) · Q 게이지 고리 3. <paramref name="qInner"/>(Q 안쪽 테)는 준비돼도 1px 흰 선 그대로 —
    /// 금 테가 두 겹(안쪽 2 + 바깥 3)이던 것을 바깥 고리 하나로 모은다(명조 궁 고리). Q 준비됨은 바깥 고리 · 빛이 말하고, 안쪽은 그려지는 연출만.
    /// </summary>
    private void PlaceRound(RectTransform slot, Vector2 pos, float size, UIThinFrame frame, bool qInner, float keyBelow, TMP_Text keyLabel)
    {
        if (slot == null) return;
        slot.SetParent(transform, false);   // 포드에서 꺼낸다(포드는 빈 상자로 남는다)
        Anchor(slot, new Vector2(1f, 0f), pos, new Vector2(size, size), new Vector2(0.5f, 0.5f));

        var disc = UIThinShapes.Disc(size);
        if (slot.TryGetComponent<Image>(out var body)) { body.sprite = disc; body.type = Image.Type.Simple; body.color = WDisc; }
        if (slot.Find("SkinBg") is RectTransform bg && bg.TryGetComponent<Image>(out var bgImg))
        {
            StretchFill(bg);
            bgImg.sprite = disc; bgImg.type = Image.Type.Simple; bgImg.color = WDisc;
        }
        if (frame != null && frame.TryGetComponent<Image>(out var fImg))
        {
            var frt = fImg.rectTransform;
            frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
            frt.offsetMin = -Vector2.one; frt.offsetMax = Vector2.one;
            UIThinFrame.Adopt(fImg, UIThinShapes.Ring(size + 2f, 1f), UIThinShapes.Ring(size + 2f, qInner ? 1f : 2f), breathe: false, keepColor: qInner);
            frame.Reapply();
        }
        foreach (var t in slot.GetComponentsInChildren<TMP_Text>(true))
            if (t == keyLabel || t.name.StartsWith("Label")) StyleRoundKey(t, keyBelow);   // 프리팹이 배선한 키 글자는 이름이 제각각이다
    }

    /// <summary>원 아래 가운데 키 글자 — Medium 15 · 잉크(옛 금색 18 대신).</summary>
    private static void StyleRoundKey(TMP_Text t, float below)
    {
        var rt = t.rectTransform;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -below);
        rt.sizeDelta        = new Vector2(36f, 22f);
        UITheme.StyleThinText(t, UIWeight.Medium, WKeySize, UITheme.Thin.Ink, shadow: true);
        t.alignment = TextAlignmentOptions.Top;
    }

    private static void Center(RectTransform rt, float d)
    {
        rt.anchorMin = rt.anchorMax = rt.pivot = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta        = new Vector2(d, d);
    }

    // ── 무기(체력 왼쪽) ──

    private void LayoutWuwaWeapons()
    {
        for (int i = 0; i < _weaponSlotRoot.Length; i++)
        {
            var root = _weaponSlotRoot[i];
            if (root == null) continue;
            root.SetParent(transform, false);
            root.anchorMin = root.anchorMax = new Vector2(0.5f, 0f);
            root.pivot      = new Vector2(0.5f, 0.5f);
            root.localScale = Vector3.one;

            var slot = i == 0 ? slot0 : slot1;
            if (slot?.CellBg is Image bg)
            {
                StretchFill(bg.rectTransform);
                bg.sprite = UIThinShapes.Disc(WWeaponBig); bg.type = Image.Type.Simple;
            }
            if (slot?.Icon is Image icon)
            {
                var irt = icon.rectTransform;
                irt.anchorMin = new Vector2(0.2f, 0.2f); irt.anchorMax = new Vector2(0.8f, 0.8f);
                irt.offsetMin = irt.offsetMax = Vector2.zero;
                icon.preserveAspect = true;
            }
            if (_weaponThin[i] != null && _weaponThin[i].TryGetComponent<Image>(out var fImg))
            {
                var frt = fImg.rectTransform;
                frt.anchorMin = Vector2.zero; frt.anchorMax = Vector2.one;
                frt.offsetMin = -Vector2.one; frt.offsetMax = Vector2.one;
            }
            if (_weaponName[i] != null) _weaponName[i].text = string.Empty;   // 이름 없음(명조) — 끄면 하드 가드가 다시 켠다
            if (_weaponLevel[i] != null)
            {
                var lrt = _weaponLevel[i].rectTransform;
                lrt.anchorMin = lrt.anchorMax = new Vector2(1f, 1f);
                lrt.pivot            = new Vector2(0.5f, 0.5f);
                lrt.anchoredPosition = new Vector2(-2f, -2f);
                UITheme.StyleThinText(_weaponLevel[i], UIWeight.Medium, WKeySize, _weaponLevel[i].color, shadow: true);
            }
            foreach (var t in root.GetComponentsInChildren<TMP_Text>(true))
            {
                if (!t.name.StartsWith("Label")) continue;
                var rt = t.rectTransform;
                rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0f);
                rt.pivot            = new Vector2(0.5f, 1f);
                rt.anchoredPosition = new Vector2(0f, -4f);
                UITheme.StyleThinText(t, UIWeight.Medium, WKeySize, UITheme.Thin.Ink, shadow: true);
            }
        }
        PlaceWeapons(animate: false);
    }

    /// <summary>
    /// 무기 원 테를 지금 원 크기에 맞춘 1px 고리로 — 큰 원(78)용 고리 한 장을 작은 원(44)에 늘려 쓰면 선이 0.56px로 가늘어져
    /// 선 굵기가 네 번째로 갈렸다(10-11). 자리 바꿈이 끝난 크기로 다시 넘겨받는다(색 · 상태는 그대로).
    /// </summary>
    private void FitWeaponRing(int i, float size)
    {
        var frame = _weaponThin[i];
        if (frame == null || !frame.TryGetComponent<Image>(out var fImg)) return;
        var ring = UIThinShapes.Ring(size + 2f, 1f);
        if (fImg.sprite == ring) return;
        UIThinFrame.Adopt(fImg, ring, null, breathe: false);
        frame.Reapply();
    }

    /// <summary>든 무기 = 큰 원(체력 왼쪽) · 다른 무기 = 작은 원(그 왼쪽 아래). 바뀌면 0.18초 자리 · 크기를 맞바꾼다.</summary>
    private void PlaceWeapons(bool animate)
    {
        int active = _activeWeaponIndex < 0 ? 0 : _activeWeaponIndex;
        for (int i = 0; i < _weaponSlotRoot.Length; i++)
        {
            var root = _weaponSlotRoot[i];
            if (root == null) continue;
            // 앞 자리 바꿈을 먼저 끊는다(10-14 사용자 「근접을 들고 있는데 UI는 원거리」) — 빠르게 두 번 바꾸면(실측 로그: 1 → 2 → 1 … 연달아)
            // 둘째 호출 때 원이 아직 첫 프레임(움직임 0)이라 「이미 제자리」로 건너뛰고, 끊기지 않은 첫 바꿈이 끝까지 돌아 큰 원을 반대 무기에 남겼다.
            // 패널이 꺼진 채 바꿀 때(바로 놓기)도 앞 바꿈이 그 위를 덮었다.
            CancelWeaponSwap(i);
            bool big   = i == active;
            var  pos   = big ? WWeaponBigPos : WWeaponSmallPos;
            var  size  = Vector2.one * (big ? WWeaponBig : WWeaponSmall);
            if ((root.anchoredPosition - pos).sqrMagnitude < 0.01f && (root.sizeDelta - size).sqrMagnitude < 0.01f) { FitWeaponRing(i, size.x); continue; }
            if (!animate || !isActiveAndEnabled) { root.anchoredPosition = pos; root.sizeDelta = size; FitWeaponRing(i, size.x); continue; }
            _weaponSwapCts ??= new System.Threading.CancellationTokenSource[_weaponSlotRoot.Length];
            _weaponSwapCts[i] = System.Threading.CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            SwapWeaponAsync(i, root, pos, size, _weaponSwapCts[i].Token).Forget();
        }
        PlaceFormPill(animate);   // 형 게이지 — 원거리를 든 동안 근접 원을 따라 작아지고 옅어진다(10-08)
    }

    private void CancelWeaponSwap(int i)
    {
        if (_weaponSwapCts == null || i >= _weaponSwapCts.Length || _weaponSwapCts[i] == null) return;
        _weaponSwapCts[i].Cancel();
        _weaponSwapCts[i].Dispose();
        _weaponSwapCts[i] = null;
    }

    private async UniTaskVoid SwapWeaponAsync(int index, RectTransform rt, Vector2 pos, Vector2 size, System.Threading.CancellationToken ct)
    {
        var p0 = rt.anchoredPosition;
        var s0 = rt.sizeDelta;
        try
        {
            await UIMotion.Tween(UIMotion.Swap, k =>
            {
                rt.anchoredPosition = Vector2.LerpUnclamped(p0, pos, k);
                rt.sizeDelta        = Vector2.LerpUnclamped(s0, size, k);
            }, ct);
            FitWeaponRing(index, size.x);   // 다 바뀐 크기에 맞춘 1px 고리
        }
        catch (System.OperationCanceledException) { }
    }

    /// <summary>왼쪽 기둥(버프 · 서약) 바닥 — 무기가 아래 가운데로 옮겨 가 왼쪽 아래가 비었다.</summary>
    private float WuwaDockBottomWorld()
    {
        var rt = (RectTransform)transform;
        rt.GetWorldCorners(_columnCorners);
        return _columnCorners[0].y + WDockBottom * rt.lossyScale.y;
    }

    // ── 매 프레임(Update 끝) ──

    /// <summary>게이지 채움 · 끝 빛 · 정오/절정 변신 · Q 고리 · 빛. 힙 할당 없음.</summary>
    private void TickWuwa()
    {
        if (_relicResource == null) return;
        float fill  = Mathf.Clamp01(_relicResource.Fill);
        bool  ready = _relicResource.IsSkillReady;
        float k     = _relicFlash;   // 정오 · 절정 진입 1 → 0(UpdateSunGauge · 검 바가 줄인다)
        float breathe = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 3f);

        if (_useSunGauge && _wGaugeL != null)
        {
            var col = ready ? WNoon : UITheme.Thin.Gold;
            _wGaugeL.fillAmount = fill; _wGaugeL.color = col;
            _wGaugeR.fillAmount = fill; _wGaugeR.color = col;
            float h = ready ? WGaugeNoonH : WGaugeH;
            if (_sunGaugeRoot != null && Mathf.Abs(_sunGaugeRoot.sizeDelta.y - h) > 0.01f) _sunGaugeRoot.sizeDelta = new Vector2(WGaugeW, h);

            // 차오르는 끝 빛 — 충전 중에만
            float halfW = WGaugeW * 0.5f - WGaugeGap * 0.5f;
            float x     = WGaugeGap * 0.5f + halfW * fill;
            var tip = !ready && fill > 0.01f && fill < 0.995f ? UITheme.Thin.WithA(UITheme.Thin.Gold, 0.9f) : Color.clear;
            if (_wTipL != null) { _wTipL.rectTransform.anchoredPosition = new Vector2(-x, 0f); _wTipL.color = tip; }
            if (_wTipR != null) { _wTipR.rectTransform.anchoredPosition = new Vector2( x, 0f); _wTipR.color = tip; }

            // 정오 진입 — 문장이 0.8배에서 자리 잡으며 커진다(튕김 없이)
            if (_sunClosedImg != null) _sunClosedImg.rectTransform.localScale = Vector3.one * Mathf.Lerp(1f, 0.8f, k * k);
            if (_sunLabel != null) _sunLabel.color = ready ? WNoon : UITheme.Thin.Ink;
        }

        var glow = _useSunGauge ? _wGlowSun : _wGlowBar;
        if (glow != null)
            glow.color = UITheme.Thin.WithA(_useSunGauge ? UITheme.Thin.Gold : _relicResource.BarColor, ready ? 0.32f + 0.14f * breathe + 0.4f * k : 0f);

        if (_wQRing != null)
        {
            _wQRing.fillAmount = ready ? 1f : fill;
            _wQRing.color      = ready ? WNoon : UITheme.Thin.Gold;
            bool usable = _qGateOpen && _qCooldownReady && _qHasSkill;   // 빛 = 지금 누를 수 있음(실제 Q 문 · 10-12 A4) — 고리 채움 · 색은 게이지 그대로
            if (_wQGlow != null) _wQGlow.color = UITheme.Thin.WithA(UITheme.Thin.Gold, usable ? 0.3f + 0.15f * breathe : 0f);
        }
    }

    /// <summary>Q를 쓴 순간 — 게이지에서 Q 칸으로 빛 한 점이 날아간다(0.3초).</summary>
    private void PlayQStreak()
    {
        if (_wGaugeHost == null) _wGaugeHost = _useSunGauge ? _sunRoot : _relicBar;
        if (_wGaugeHost == null || _wQSlot == null || !isActiveAndEnabled || !_wGaugeHost.gameObject.activeInHierarchy) return;
        if (_wStreak == null)
        {
            _wStreak = ShopUIStyle.MakeImage(transform, "QStreak", Color.clear);
            _wStreak.sprite = UI_RuneSelectPopup.SoftDot;
            _wStreak.rectTransform.sizeDelta = new Vector2(30f, 30f);
        }
        _wStreak.transform.SetAsLastSibling();
        StreakAsync(_wGaugeHost.position, _wQSlot.position).Forget();
    }

    private async UniTaskVoid StreakAsync(Vector3 from, Vector3 to)
    {
        var rt = _wStreak.rectTransform;
        try
        {
            await UIMotion.Tween(WStreakSec, k =>
            {
                rt.position   = Vector3.LerpUnclamped(from, to, k);
                _wStreak.color = UITheme.Thin.WithA(UITheme.Thin.Gold, 0.9f * Mathf.Sin(k * Mathf.PI));
            }, destroyCancellationToken);
        }
        catch (System.OperationCanceledException) { }
        finally { if (_wStreak != null) _wStreak.color = Color.clear; }
    }

    /// <summary>재사용 대기 숫자 — 10초 미만은 소수 한 자리(명조 「8.2」), 그 위는 올림 정수.</summary>
    private static void SetCooldownLabel(TMP_Text t, float remaining)
    {
        if (t == null) return;
        if (WuwaHud && remaining < 10f) t.SetText("{0:1}", remaining);
        else t.text = Mathf.CeilToInt(remaining).ToString();
    }
}
