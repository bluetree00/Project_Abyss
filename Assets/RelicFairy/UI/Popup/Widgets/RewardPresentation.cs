using UnityEngine;

/// <summary>보상 연출 강도 3단. 원신·가챠 UX의 스킵 표준(구현설계_보상공개연출 §C-1 스킵 규칙).</summary>
public enum RewardPresentationMode
{
    /// <summary>전체 — 차징·스태거·굴림 리빌 전부.</summary>
    Full = 0,
    /// <summary>축약 — 차징 생략, 스태거 0.04s, 굴림 리빌·니어미스 생략.</summary>
    Brief = 1,
    /// <summary>끔 — 연출 없음(개편 전 동작과 동일).</summary>
    Off = 2,
}

/// <summary>
/// 보상 공개 연출의 <b>표시층 전용</b> 파라미터.
///
/// 여기 있는 값은 어느 것도 확률·결과·기대값에 영향을 주지 않는다. 결과는 <c>RoomClearGate</c>에서
/// 이미 확정돼 있고, 이 클래스는 "이미 정해진 것을 얼마나 크게 보여줄지"만 정한다
/// (재련소 PlayEnhanceSequence와 동일 원칙).
///
/// 정본: 구현설계/RelicFairy_구현설계_보상공개연출_희귀도차등.md §C-1 · §C-2
/// </summary>
public static class RewardPresentation
{
    private const string ModePrefsKey = "RelicFairy.RewardPresentationMode";

    // ── 연출 모드 ────────────────────────────────────────────────

    private static RewardPresentationMode? _cachedMode;

    /// <summary>현재 연출 강도. 설정 화면이 생기면 <see cref="Mode"/> setter만 물리면 된다.</summary>
    public static RewardPresentationMode Mode
    {
        get
        {
            _cachedMode ??= (RewardPresentationMode)Mathf.Clamp(
                PlayerPrefs.GetInt(ModePrefsKey, (int)RewardPresentationMode.Full), 0, 2);
            return _cachedMode.Value;
        }
        set
        {
            _cachedMode = value;
            PlayerPrefs.SetInt(ModePrefsKey, (int)value);
            PlayerPrefs.Save();
        }
    }

    /// <summary>연출을 아예 재생하지 않는가.</summary>
    public static bool IsOff => Mode == RewardPresentationMode.Off;

    // ── 티어 스펙 ────────────────────────────────────────────────

    /// <summary>등급 하나에 배정된 연출 파라미터 묶음(§C-2 스펙표 1행).</summary>
    public readonly struct TierSpec
    {
        /// <summary>월드 단계 차징 길이(초). 0이면 차징 생략.</summary>
        public readonly float ChargeDuration;
        /// <summary>보상 오브젝트 떨림 진폭(m).</summary>
        public readonly float ShakeAmplitude;
        /// <summary>월드 차징 중 걸 슬로우모 배율. 1이면 슬로우 없음.</summary>
        public readonly float SlowMotionScale;
        /// <summary>카드 팝인 길이(초).</summary>
        public readonly float CardPopDuration;
        /// <summary>카드 간 공개 간격(초).</summary>
        public readonly float CardStagger;
        /// <summary>카드 팝인 시작 회전각(도). 최상위만 회전이 붙는다.</summary>
        public readonly float CardPopRotation;
        /// <summary>프레임 플래시 반복 수. 0이면 플래시 없음.</summary>
        public readonly int   FlashPulses;
        /// <summary>VolumePulse 세기. 0이면 펄스 없음.</summary>
        public readonly float PulsePeak;
        /// <summary>VolumePulse 길이(초).</summary>
        public readonly float PulseDuration;
        /// <summary>등급 SFX 피치(단일 클립 4단 티어링).</summary>
        public readonly float SfxPitch;
        /// <summary>굴림 리빌 총 길이(초). 0이면 즉시 확정.</summary>
        public readonly float RevealDuration;
        /// <summary>전체화면 플래시 알파. 0이면 없음.</summary>
        public readonly float ScreenFlashAlpha;

        public TierSpec(float chargeDuration, float shakeAmplitude, float slowMotionScale,
                        float cardPopDuration, float cardStagger, float cardPopRotation,
                        int flashPulses, float pulsePeak, float pulseDuration,
                        float sfxPitch, float revealDuration, float screenFlashAlpha)
        {
            ChargeDuration = chargeDuration; ShakeAmplitude = shakeAmplitude; SlowMotionScale = slowMotionScale;
            CardPopDuration = cardPopDuration; CardStagger = cardStagger; CardPopRotation = cardPopRotation;
            FlashPulses = flashPulses; PulsePeak = pulsePeak; PulseDuration = pulseDuration;
            SfxPitch = sfxPitch; RevealDuration = revealDuration; ScreenFlashAlpha = screenFlashAlpha;
        }
    }

    // §C-2 스펙표. 가로로 읽으면 "없음 → 색 → 색+시간 → 색+시간+화면+소리"로 채널 개수가 계단진다.
    // Common을 거의 무반응으로 두는 것이 설계의 방어선이다 — 한 런에 3지선다가 10~20회 발생한다.
    private static readonly TierSpec Common = new(
        chargeDuration: 0f,    shakeAmplitude: 0f,    slowMotionScale: 1f,
        cardPopDuration: 0.12f, cardStagger: 0.06f,   cardPopRotation: 0f,
        flashPulses: 0,        pulsePeak: 0f,         pulseDuration: 0f,
        sfxPitch: 0.85f,       revealDuration: 0f,    screenFlashAlpha: 0f);

    private static readonly TierSpec Rare = new(
        chargeDuration: 0.25f, shakeAmplitude: 0.02f, slowMotionScale: 1f,
        cardPopDuration: 0.16f, cardStagger: 0.08f,   cardPopRotation: 0f,
        flashPulses: 1,        pulsePeak: 0f,         pulseDuration: 0f,
        sfxPitch: 1.00f,       revealDuration: 0.10f, screenFlashAlpha: 0f);

    private static readonly TierSpec Epic = new(
        chargeDuration: 0.35f, shakeAmplitude: 0.05f, slowMotionScale: 0.45f,
        cardPopDuration: 0.20f, cardStagger: 0.10f,   cardPopRotation: 0f,
        flashPulses: 1,        pulsePeak: 0.30f,      pulseDuration: 0.18f,
        sfxPitch: 1.15f,       revealDuration: 0.18f, screenFlashAlpha: 0f);

    private static readonly TierSpec Legendary = new(
        chargeDuration: 0.55f, shakeAmplitude: 0.09f, slowMotionScale: 0.30f,
        cardPopDuration: 0.24f, cardStagger: 0.14f,   cardPopRotation: 4f,
        flashPulses: 2,        pulsePeak: 0.55f,      pulseDuration: 0.30f,
        sfxPitch: 1.30f,       revealDuration: 0.25f, screenFlashAlpha: 0.35f);

    /// <summary>등급 → 연출 스펙. 모드가 축약/끔이면 여기서 이미 깎아 돌려준다.</summary>
    public static TierSpec For(ItemRarity rarity)
    {
        var spec = rarity switch
        {
            ItemRarity.Legendary => Legendary,
            ItemRarity.Epic      => Epic,
            ItemRarity.Rare      => Rare,
            _                    => Common,
        };

        return Mode switch
        {
            RewardPresentationMode.Brief => Abbreviate(spec),
            RewardPresentationMode.Off   => Silence(spec),
            _                            => spec,
        };
    }

    /// <summary>축약 — 차징·굴림 리빌·전체화면 플래시를 걷고 스태거를 0.04s로 고정. 등급 색·소리는 남긴다.</summary>
    private static TierSpec Abbreviate(in TierSpec s) => new(
        chargeDuration: 0f, shakeAmplitude: 0f, slowMotionScale: 1f,
        cardPopDuration: Mathf.Min(s.CardPopDuration, 0.12f), cardStagger: 0.04f, cardPopRotation: 0f,
        flashPulses: Mathf.Min(s.FlashPulses, 1), pulsePeak: s.PulsePeak, pulseDuration: s.PulseDuration,
        sfxPitch: s.SfxPitch, revealDuration: 0f, screenFlashAlpha: 0f);

    /// <summary>끔 — 시간을 쓰는 연출을 전부 0으로. 프레임 색·라벨 같은 정지 표기만 남는다.</summary>
    private static TierSpec Silence(in TierSpec s) => new(
        chargeDuration: 0f, shakeAmplitude: 0f, slowMotionScale: 1f,
        cardPopDuration: 0f, cardStagger: 0f, cardPopRotation: 0f,
        flashPulses: 0, pulsePeak: 0f, pulseDuration: 0f,
        sfxPitch: s.SfxPitch, revealDuration: 0f, screenFlashAlpha: 0f);

    // ── 월드 단계(방에 선 보상 오브젝트) ─────────────────────────

    /// <summary>
    /// 방에 선 보상 오브젝트의 등급 연출 — 예고(빛이 모임) · 등장 · 놓여 있는 모습(10-01 이벤트방 설계 §11).
    /// 후보는 방을 클리어하는 순간 정해지므로, 최고 등급을 오브젝트가 서기 전부터 알릴 수 있다.
    /// </summary>
    public readonly struct WorldSpec
    {
        /// <summary>예고 길이(초) — 클리어 뒤 이만큼 있다가 오브젝트가 선다. 등급이 높을수록 길다.</summary>
        public readonly float Foretell;
        /// <summary>예고 · 놓여 있는 소용돌이의 크기 배율. 0이면 빛 없음(끔).</summary>
        public readonly float SwirlScale;
        /// <summary>등장 빛기둥 크기 배율. 0이면 빛기둥 없음.</summary>
        public readonly float PillarScale;
        /// <summary>등장 뒤 이어지는 빛 박자(빛줄기 + 바닥 불꽃) 수.</summary>
        public readonly int   Rings;
        /// <summary>등장 슬로모 배율. 1이면 없음.</summary>
        public readonly float SlowScale;
        /// <summary>등장 슬로모 길이(초, 실시간).</summary>
        public readonly float SlowSeconds;
        /// <summary>등장 색수차 세기. 0이면 없음.</summary>
        public readonly float PulsePeak;
        /// <summary>화면 가장자리 금빛을 띄우는가(전설).</summary>
        public readonly bool  EdgeGlow;
        /// <summary>놓여 있는 빛의 맥동 빠르기(Hz). 0이면 고정.</summary>
        public readonly float IdlePulseHz;
        /// <summary>예고 빛이 등장 뒤에도 보상 곁에 남는가(레어 이상 — 일반은 매 방 보는 등급이라 흩어진다).</summary>
        public readonly bool  IdleAura;
        /// <summary>등급 기둥이 예고부터 서는가(레어 이상 — 서기 전부터 등급이 보인다). 등장 뒤 잠시 제 높이로 섰다가 낮아진다(반대편 시야를 가리지 않게).</summary>
        public readonly bool  IdlePillar;
        /// <summary>등장음 수 — 0 = 없음, 1 = 한 음, 3 = 상승 3음.</summary>
        public readonly int   Notes;

        public WorldSpec(float foretell, float swirlScale, float pillarScale, int rings,
                         float slowScale, float slowSeconds, float pulsePeak, bool edgeGlow,
                         float idlePulseHz, bool idleAura, bool idlePillar, int notes)
        {
            Foretell = foretell; SwirlScale = swirlScale; PillarScale = pillarScale; Rings = rings;
            SlowScale = slowScale; SlowSeconds = slowSeconds; PulsePeak = pulsePeak; EdgeGlow = edgeGlow;
            IdlePulseHz = idlePulseHz; IdleAura = idleAura; IdlePillar = idlePillar; Notes = notes;
        }
    }

    /// <summary>연출이 꺼져 있을 때 오브젝트가 서기까지의 대기(초) — 빛 없이 조용히 선다.</summary>
    public const float WorldOffDelay = 0.4f;

    // 가로로 읽으면 "빛 몇 가닥 → 빛기둥 → 문양+기둥+빛 박자 → 표지 기둥(예고부터)+빛 박자 2+슬로모+금빛 가장자리"로 채널이 계단진다(카드 표와 같은 원칙).
    // 낮은 등급은 빨리 서고(런 템포), 높은 등급은 기다림 자체가 예고다.
    private static readonly WorldSpec WorldCommon = new(
        foretell: 0.6f,  swirlScale: 0.5f,  pillarScale: 0f,    rings: 0,
        slowScale: 1f,   slowSeconds: 0f,   pulsePeak: 0f,    edgeGlow: false,
        idlePulseHz: 0f,   idleAura: false, idlePillar: false, notes: 1);

    private static readonly WorldSpec WorldRare = new(
        foretell: 1.0f,  swirlScale: 0.8f,  pillarScale: 0.6f,  rings: 0,
        slowScale: 1f,   slowSeconds: 0f,   pulsePeak: 0f,    edgeGlow: false,
        idlePulseHz: 0f,   idleAura: true,  idlePillar: true,  notes: 1);

    private static readonly WorldSpec WorldEpic = new(
        foretell: 1.5f,  swirlScale: 1.1f,  pillarScale: 0.85f, rings: 1,
        slowScale: 1f,   slowSeconds: 0f,   pulsePeak: 0f,    edgeGlow: false,
        idlePulseHz: 0.8f, idleAura: true,  idlePillar: true,  notes: 1);

    private static readonly WorldSpec WorldLegendary = new(
        foretell: 2.0f,  swirlScale: 1.6f,  pillarScale: 1.15f, rings: 2,
        slowScale: 0.5f, slowSeconds: 0.3f, pulsePeak: 0.15f, edgeGlow: true,
        idlePulseHz: 0.6f, idleAura: true,  idlePillar: true,  notes: 3);

    /// <summary>등급 → 월드 연출 수치. 모드가 축약/끔이면 여기서 깎아 돌려준다.</summary>
    public static WorldSpec World(ItemRarity rarity)
    {
        var spec = rarity switch
        {
            ItemRarity.Legendary => WorldLegendary,
            ItemRarity.Epic      => WorldEpic,
            ItemRarity.Rare      => WorldRare,
            _                    => WorldCommon,
        };

        return Mode switch
        {
            // 축약 — 예고 절반 · 슬로모 · 화면 효과 없음 · 고리 1겹 · 한 음. 빛깔과 놓여 있는 모습은 남긴다.
            RewardPresentationMode.Brief => new WorldSpec(
                spec.Foretell * 0.5f, spec.SwirlScale, spec.PillarScale, Mathf.Min(spec.Rings, 1),
                1f, 0f, 0f, false, spec.IdlePulseHz, spec.IdleAura, spec.IdlePillar, 1),
            // 끔 — 빛도 소리도 없이 짧게 기다렸다 선다.
            RewardPresentationMode.Off => new WorldSpec(
                WorldOffDelay, 0f, 0f, 0, 1f, 0f, 0f, false, 0f, false, false, 0),
            _ => spec,
        };
    }

    // ── 등급 표기 ────────────────────────────────────────────────

    /// <summary>
    /// 카드 등급 라벨. Epic·Legendary가 같은 <c>◆</c>를 쓰던 것을 고쳐 최상위를 <c>★</c>로 분리한다
    /// — 최상위 등급이 형태로도 구별되지 않으면 색만으로는 안 읽힌다(§C-2 · Hades 반례).
    /// </summary>
    public static string RarityLabel(ItemRarity rarity) => rarity switch
    {
        ItemRarity.Rare      => "◇ Rare",
        ItemRarity.Epic      => "◆ Epic",
        ItemRarity.Legendary => "★ Legendary",
        _                    => "□ Common",   // 가운뎃점은 구분자와 겹쳐 "· Common · 2칸"으로 읽혔다 — 빈 네모(글리프 화이트리스트)
    };

    /// <summary>등급 프레임 색(불투명). 카드 테두리·월드 인디케이터 공용 — "프레임=희귀도" 규칙(P0-8).</summary>
    public static Color FrameColor(ItemRarity rarity)
    {
        var c = RarityColorTable.Get(rarity);
        // Common은 눈에 띄면 안 된다 — 회색으로 눌러 하위를 미니멀하게 유지한다.
        if (rarity == ItemRarity.Common) return new Color(0.34f, 0.33f, 0.38f, 1f);
        c.a = 1f;
        return c;
    }

    /// <summary>후보 중 최고 등급 — 시퀀스 강도를 정하는 값(§C-1 티어 결정값).</summary>
    public static ItemRarity MaxRarity(System.Collections.Generic.List<(RuntimeItemData data, ItemSO so)> items)
    {
        var max = ItemRarity.Common;
        if (items == null) return max;
        for (int i = 0; i < items.Count; i++)
            if (items[i].data != null && items[i].data.rarity > max) max = items[i].data.rarity;
        return max;
    }
}
