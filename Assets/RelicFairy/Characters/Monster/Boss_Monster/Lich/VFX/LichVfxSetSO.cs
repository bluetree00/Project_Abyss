using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 이펙트 칸. 패턴은 프리팹을 직접 들지 않고 칸 이름으로 부른다 — 아트를 바꿀 때 코드를 건드리지 않게.
/// 색 규약: 보라 = 마법·저주 · 빨강 = 즉시 회피 · 청록 = 끊을 수 있음 · 금 = 플레이어의 봉인 · 흰 = 안전.
/// 순서를 바꾸지 말 것(직렬화 값) — 새 칸은 끝에 붙인다.
/// </summary>
public enum LichVfxSlot
{
    None = 0,

    // ── 공용 ──
    CastFlare       = 1,   // 시전 순간 손·책의 섬광
    TeleportVanish  = 2,   // 사라짐(보라 연기·균열)
    TeleportAppear  = 3,   // 나타남
    SlamImpact      = 4,   // 낙하 강타 충격파
    PhaseBurst      = 5,   // 페이지 전환 방사 폭발
    BossAura        = 6,   // 몸에 두르는 어둠 오라(반복)

    // ── 1페이지 대마법 ──
    BoltProjectile  = 10,  // 성흔 탄막 투사체
    BoltMuzzle      = 11,
    BoltImpact      = 12,
    CircleFire      = 13,  // 원소 마법진 4종
    CircleIce       = 14,
    CircleLightning = 15,
    CircleDark      = 16,
    FireBeam        = 17,  // 관통 광선
    IceSpear        = 18,
    IceImpact       = 19,
    LightningStrike = 20,  // 낙뢰
    DarkOrb         = 21,  // 느린 추적 구체
    DarkOrbImpact   = 22,
    ArcaneOrb       = 23,  // 반경 3 m 구르는 구체
    ArcaneOrbBurst  = 24,
    TwinCircle      = 25,  // 쌍둥이 주문 원
    TwinBlast       = 26,
    SummonGround    = 27,  // 해골이 솟는 바닥
    WardDome        = 28,  // 결계 돔(반복)
    WardBreak       = 29,
    ZoneFire        = 30,  // 원소 재편 바닥(반복)
    ZoneIce         = 31,
    ZoneLightning   = 32,
    ZoneDark        = 33,
    LingerPool      = 34,  // 여운 장판(반복) — 0.4초마다 소량 피해, 회피 한 번으로 못 버틴다
    ArcaneBeam      = 35,  // 원소 격류 — 바닥을 쓸고 도는 비전 광선(두 끝을 매 프레임 따라간다)
    TileRestore     = 36,  // 부서진 바닥이 떠올라 복구되는 빛

    // ── 낫 · 사슬 ──
    ScytheSlash     = 40,
    ScytheSpin      = 41,
    BlinkAfterimage = 42,
    ChainShockwave  = 43,  // 사슬 끊기 원형 충격파
    DarkRainDrop    = 44,
    DarkRainImpact  = 45,
    DarkCloud       = 46,
    ParryGlint      = 47,  // 패링 창 — 낫의 금빛 섬광
    ParryClash      = 48,  // 패링 성공 — 튕겨내는 불꽃
    BindRing        = 49,  // 사슬 결박 — 묶인 플레이어 발밑 금빛 고리(반복)

    // ── 봉인 ──
    ChainGold       = 50,  // 금빛 사슬(반복)
    ChainReversed   = 51,  // 역류한 보라 사슬(반복)
    SealStoneIdle   = 52,  // 봉인석 대기(반복)
    SealStoneIgnite = 53,
    SealStoneBurst  = 54,
    SealBurst       = 55,  // 봉인 사슬이 감기는 순간의 금빛 폭발
    SealComplete    = 56,  // 봉인 완성 — 금빛 고리 + 빛기둥
    SealStoneModel  = 57,  // 봉인석 모델(메시) — LichVfx.InstantiateModel. 룬 발광은 _EmissionColor
    MonumentModel   = 58,  // 2페이즈 무대 변화 — 무너진 귀퉁이에서 솟는 거대 석상(메시)

    // ── 3페이지 · 마감 ──
    SealArray       = 60,  // 반경 10 m 회전 봉인진(반복)
    DebrisFall      = 61,
    DebrisImpact    = 62,
    DeathBurst      = 63,
    GroundCrack     = 64,
    SkeletonDeath   = 65,  // 낫 해골이 부서지는 뼛가루
    EntranceRise    = 66,  // 등장 — 제단 바닥에서 솟는 어둠 분출(페이지 전환과 다른 자원 — 09-20)
    PageBreak       = 67,  // 페이지 전환 — 보라 섬광 폭발(휘청 · 포효)

    // ── 화면 전체(카메라에 붙는 오버레이 — LichVfx.PlayScreen) ──
    ScreenDebuff    = 70,  // 보라 가장자리 — 페이지 전환
    ScreenMagicFlow = 71,  // 봉인 완성
    ScreenSpace     = 72,  // 최후의 대마법
    ScreenWind      = 73,  // 최후의 원 — 코어로 끄는 바람
}

[Serializable]
public struct LichVfxEntry
{
    public LichVfxSlot slot;
    public GameObject  prefab;
    [Tooltip("프리팹 크기에 곱할 배율 (0이면 1)")]
    public float       scale;
    [Tooltip("재생 위치 보정(재생 회전 기준)")]
    public Vector3     offset;
    [Tooltip("한 번 재생 수명(초). 0이면 파티클 길이로 계산")]
    public float       lifetime;
    [Tooltip("출처 메모(팩·원본 이름)")]
    public string      note;
    [Tooltip("광선 칸: 배율 1일 때 프리팹의 +Z 길이(m). 0이면 늘이지 않는다(선 렌더러 광선은 끝점을 옮긴다)")]
    public float       beamLength;
    [Tooltip("입자 시작 색에 곱할 색 — 팩 색을 규약 색(낫 = 빨강 등)으로 바꿀 때. 알파 0이면 원래 색")]
    public Color       tint;
}

/// <summary>
/// 리치 이펙트 목록 — Addressables 주소 <see cref="LichVfx.SetAddress"/>로 불러온다.
/// 프리팹은 이 에셋이 직접 참조하므로 번들에 함께 들어간다(기존 보스들의 패턴 SO 참조와 같은 방식).
/// 등록: 메뉴 RelicFairy/Boss/Lich/Apply VFX Mapping + Register Addressables (매핑 = VFX/Editor/LichVfxMapping.json).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich VFX Set", fileName = "LichVfxSet")]
public sealed class LichVfxSetSO : ScriptableObject
{
    [SerializeField] private List<LichVfxEntry> entries = new();

    [Header("재질 — 코드가 그리는 사슬·균열")]
    [Tooltip("사슬 줄(LineRenderer) — 사슬 고리가 반복되는 알파 텍스처 재질. 색은 선 정점 색으로 입힌다")]
    [SerializeField] private Material chainMaterial;
    [Tooltip("바닥 균열(평면 쿼드) — 균열 텍스처 재질. 색·페이드는 _Color로 입힌다")]
    [SerializeField] private Material crackMaterial;

    private Dictionary<LichVfxSlot, LichVfxEntry> _map;

    public IReadOnlyList<LichVfxEntry> Entries => entries;
    public Material ChainMaterial => chainMaterial;
    public Material CrackMaterial => crackMaterial;

    public bool TryGet(LichVfxSlot slot, out LichVfxEntry entry)
    {
        if (_map == null) BuildMap();
        return _map.TryGetValue(slot, out entry) && entry.prefab != null;
    }

    private void BuildMap()
    {
        _map = new Dictionary<LichVfxSlot, LichVfxEntry>(entries.Count);
        foreach (var e in entries)
            if (e.slot != LichVfxSlot.None && e.prefab != null)
                _map[e.slot] = e;
    }

    private void OnDisable() => _map = null;   // 인스펙터 편집 후 다시 만든다
}
}
