using System;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 리치 소리 칸. 패턴은 클립을 직접 들지 않고 칸 이름으로 부른다 — 이펙트 칸(<see cref="LichVfxSlot"/>)과 같은 방식.
/// 봉인판·해방판 에셋 쌍이 같은 소리를 쓰고, 소리 교체는 매핑 파일 한 곳에서 한다.
/// 순서를 바꾸지 말 것(직렬화 값) — 새 칸은 끝에 붙인다.
/// </summary>
public enum LichSfxSlot
{
    None = 0,

    // ── 마법 ──
    CastCharge     = 1,   // 큰 마법 충전(스웰)
    CastShort      = 2,   // 짧은 시전(마력탄 충전)
    BoltFire       = 3,
    BoltImpact     = 4,
    Vanish         = 5,
    Appear         = 6,
    SlamImpact     = 7,   // 낙하 강타 · 대형 폭발
    FireBeam       = 8,
    IceCast        = 9,
    LightningCast  = 10,
    DarkOrb        = 11,  // 어둠 구체 · 소환
    ZoneHum        = 12,  // 결계 · 원형 영역
    CircleSpawn    = 13,  // 바닥 원 생성
    WardCrack      = 14,  // 결계에 금 감
    WardBreak      = 15,

    // ── 낫 · 사슬 ──
    ScytheSwing    = 20,
    ScytheSlash    = 21,  // 짧은 베기
    ScytheThrow    = 22,
    ScytheCatch    = 23,
    ChainPulse     = 24,  // 사슬 충격파
    RainStart      = 25,
    RainLoop       = 26,

    // ── 봉인 · 전환 ──
    SealStoneHit   = 30,
    SealStoneHum   = 31,  // 점화 루프
    SealComplete   = 32,
    Collapse       = 33,  // 제단 붕괴 · 돌
    PageTransition = 34,
    Blocked        = 35,  // 무적에 막힘

    // ── 패링 · 결박 · 바닥 ──
    ParryCue       = 40,  // 패링 창 — 낫이 빛나는 「쨍」
    ParryClash     = 41,  // 튕겨냄
    BindSnap       = 42,  // 사슬이 감겨 묶임
    TileRestore    = 43,  // 부서진 바닥 복구
}

[Serializable]
public struct LichSfxEntry
{
    public LichSfxSlot slot;
    public AudioClip   clip;
    [Tooltip("볼륨 (0이면 1)")]
    public float       volume;
    [Tooltip("재생 시작 지점(초) — 클립 앞 무음·예비음을 건너뛴다")]
    public float       startTime;
    [Tooltip("피치 (0이면 1)")]
    public float       pitch;
    [Tooltip("이 거리 안은 최대 음량(m). 0이면 기본값 — 60 m 제단이라 넉넉히 잡는다")]
    public float       minDistance;
    [Tooltip("출처 메모")]
    public string      note;
}

/// <summary>
/// 리치 소리 목록 — Addressables 주소 <see cref="LichSfx.SetAddress"/>로 불러온다. 클립은 이 에셋이 직접 참조한다.
/// 등록: 메뉴 RelicFairy/Boss/Lich/Apply VFX Mapping + Register Addressables (매핑 = VFX/Editor/LichSfxMapping.json).
/// </summary>
[CreateAssetMenu(menuName = "RelicFairy/Boss/Lich/Lich SFX Set", fileName = "LichSfxSet")]
public sealed class LichSfxSetSO : ScriptableObject
{
    [SerializeField] private List<LichSfxEntry> entries = new();

    private Dictionary<LichSfxSlot, LichSfxEntry> _map;

    public IReadOnlyList<LichSfxEntry> Entries => entries;

    public bool TryGet(LichSfxSlot slot, out LichSfxEntry entry)
    {
        if (_map == null)
        {
            _map = new Dictionary<LichSfxSlot, LichSfxEntry>(entries.Count);
            foreach (var e in entries)
                if (e.slot != LichSfxSlot.None && e.clip != null)
                    _map[e.slot] = e;
        }
        return _map.TryGetValue(slot, out entry) && entry.clip != null;
    }

    private void OnDisable() => _map = null;
}
}
