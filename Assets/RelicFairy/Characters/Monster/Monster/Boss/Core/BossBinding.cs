using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace RelicFairy.Monster
{
/// <summary>
/// 보스 「묶임」 — 옛 봉인 사슬 넷 + 발밑 봉인진(10-03 설계 「봉인해방시나리오·던전공간」 S2).
///   · 봉인기: 전투 내내 감겨 있다. 큰 기술을 쓰려 하면 사슬이 당겨 끊는다(<see cref="Yank"/>) — 「반쪽 힘으로 묶여 있다」를 보여 준다.
///   · 해방기: 등장 연출 끝에 보스가 사슬을 하나씩 당겨 끊고(<see cref="Strain"/> · <see cref="Snap"/>) 떨쳐낸다(<see cref="BreakFree"/>) — <see cref="BossStoryScenes.UnbindAsync"/>가 순서를 정한다.
/// 보스 오브젝트에 붙는다. 바닥 고리는 보스 발밑을 따라가고(공중이면 그 아래 바닥), 몸 쪽 끝은 보스 표의 사슬 높이.
/// 보스가 꺼지거나 파괴되면 사슬 · 봉인진을 거둔다.
/// </summary>
public sealed class BossBinding : MonoBehaviour
{
    // ── Constants ──────────────────────────────────────────────
    private const int   ChainCount        = 4;
    private const float ChainWidth        = 0.9f;    // 봉인 장면 사슬과 같은 굵기
    private const float AnchorRadiusScale = 0.7f;    // 봉인 장면(사방에서 솟는 사슬)보다 몸 가까이 — 감겨 있는 모습
    private const float SigilScaleMul     = 0.6f;
    private const float FloorProbeUp      = 2f;
    private const float FloorProbeDown    = 80f;     // 공중 화룡도 바닥까지
    private const int   GroundMask        = 1 << 3;  // Ground 레이어

    // ── Private fields ─────────────────────────────────────────
    private readonly LichChainLine[] _chains = new LichChainLine[ChainCount];
    private GameObject               _sigil;
    private BossStoryScenes.BindingLook _look;
    private bool                     _built;
    private bool                     _released;
    private bool                     _yankSaid;
    private UniTask                  _buildTask;

    // ── Properties ─────────────────────────────────────────────
    /// <summary>사슬이 감겨 있는가(만들어졌고 아직 안 끊어짐).</summary>
    public bool IsBound => _built && !_released;

    /// <summary>사슬 수(끊어진 것 포함한 자리 수).</summary>
    public int Count => ChainCount;

    // ── Lifecycle ──────────────────────────────────────────────
    private void LateUpdate()
    {
        if (!_built || _released) return;
        Vector3 floor = FloorUnder(transform.position);
        Vector3 body  = transform.position + Vector3.up * _look.BodyHeight;
        for (int i = 0; i < ChainCount; i++)
        {
            if (_chains[i] == null) continue;
            _chains[i].SetEnds(AnchorOf(floor, i), body);
        }
        if (_sigil != null) _sigil.transform.position = floor + Vector3.up * 0.05f;
    }

    private void OnDisable()
    {
        // 풀로 돌아가는 보스 — 다음 등장 때 새로 감는다
        Cleanup(false);
        if (this != null) Destroy(this);
    }

    private void OnDestroy() => Cleanup(false);

    // ── Public Methods ─────────────────────────────────────────
    /// <summary>이 보스의 묶임(없으면 null).</summary>
    public static BossBinding Of(MonsterBase boss)
        => boss != null && boss.TryGetComponent(out BossBinding b) && !b._released ? b : null;

    /// <summary>이야기 전투의 보스에 사슬을 감는다(이미 있으면 그대로). 이야기 전투가 아니면 null.</summary>
    public static BossBinding Attach(MonsterBase boss, string bossId)
    {
        if (boss == null || !BossStoryScenes.IsStoryBoss(bossId)) return null;
        if (!BossStoryScenes.TryGetLook(bossId, out var look)) return null;
        var existing = Of(boss);
        if (existing != null) return existing;
        var b = boss.gameObject.AddComponent<BossBinding>();
        b._look      = look;
        b._buildTask = b.BuildAsync(boss.destroyCancellationToken).Preserve();   // 여러 곳이 기다린다
        return b;
    }

    /// <summary>사슬이 다 만들어질 때까지(이펙트 목록 읽기 포함).</summary>
    public UniTask ReadyAsync() => _buildTask;

    /// <summary>
    /// 봉인기 — 사슬이 당겨 큰 기술을 끊는다: 팽팽 · 번쩍 · 봉인진 섬광, 첫 끊김에 멀린 한 줄(전투당 1회).
    /// 반환 = 휘청 시간(부른 패턴이 그만큼 쉰다).
    /// </summary>
    public float Yank(float stagger)
    {
        if (!IsBound) return 0f;
        foreach (var c in _chains)
        {
            if (c == null) continue;
            c.Tension(1f);
            c.Flash(0.25f);
        }
        LichVfx.PlayTinted(LichVfxSlot.SealBurst, transform.position + Vector3.up * _look.BodyHeight,
                           Quaternion.identity, _look.WrapScale * 0.3f, _look.SealColor);
        if (!_yankSaid)
        {
            _yankSaid = true;
            BossStoryScenes.SayYank(_look);
        }
        Debug.Log($"[BossBinding] 사슬이 기술을 끊음 — {name} · 휘청 {stagger:0.0}초");
        return stagger;
    }

    /// <summary>해방기 — 보스가 사슬 하나를 당긴다: 팽팽 · 번쩍(<paramref name="seconds"/> 동안).</summary>
    public void Strain(int i, float seconds)
    {
        if (!IsBound || i < 0 || i >= ChainCount || _chains[i] == null) return;
        _chains[i].Tension(1f);
        _chains[i].Flash(seconds);
    }

    /// <summary>해방기 — 그 사슬이 끊어진다: 사슬 가운데서 금빛 파편.</summary>
    public void Snap(int i)
    {
        if (!IsBound || i < 0 || i >= ChainCount || _chains[i] == null) return;
        Vector3 floor = FloorUnder(transform.position);
        Vector3 mid   = Vector3.Lerp(AnchorOf(floor, i), transform.position + Vector3.up * _look.BodyHeight, 0.5f);
        LichVfx.PlayTinted(LichVfxSlot.SealBurst, mid, Quaternion.identity, _look.WrapScale * 0.2f, _look.SealColor);
        _chains[i].Dispose();
        _chains[i] = null;
    }

    /// <summary>해방기 — 떨쳐낸다: 발밑 충격파 · 몸 섬광 · 남은 사슬과 봉인진을 거둔다.</summary>
    public void BreakFree()
    {
        if (!IsBound) return;
        Vector3 body = transform.position + Vector3.up * _look.BodyHeight;
        LichVfx.PlayTinted(LichVfxSlot.ChainShockwave, FloorUnder(transform.position) + Vector3.up * 0.2f,
                           Quaternion.identity, _look.WrapScale * 0.6f, _look.SealColor);
        LichVfx.PlayTinted(LichVfxSlot.SealBurst, body, Quaternion.identity, _look.WrapScale * 0.5f, _look.SealColor);
        Release(true);
    }

    /// <summary>사슬 · 봉인진을 거둔다(봉인 장면 시작 · 끊어짐 · 보스 꺼짐).</summary>
    public void Release(bool fade)
    {
        if (_released) return;
        Cleanup(fade);
        if (this != null) Destroy(this);
    }

    // ── Private Methods ────────────────────────────────────────
    private async UniTask BuildAsync(CancellationToken ct)
    {
        try
        {
            await LichVfx.LoadAsync();
            if (_released || this == null) return;
            Vector3 floor = FloorUnder(transform.position);
            Vector3 body  = transform.position + Vector3.up * _look.BodyHeight;
            for (int i = 0; i < ChainCount; i++)
                _chains[i] = LichChainLine.Create(floor, body, ChainWidth, _look.SealColor);
            _sigil = LichVfx.PlayLoopTinted(LichVfxSlot.SealArray, floor + Vector3.up * 0.05f, Quaternion.identity,
                                            _look.SigilScale * SigilScaleMul, _look.SealColor);
            _built = true;
            ct.ThrowIfCancellationRequested();
        }
        catch (OperationCanceledException) { Cleanup(false); }
    }

    private void Cleanup(bool fade)
    {
        if (_released) return;
        _released = true;
        for (int i = 0; i < ChainCount; i++)
        {
            if (_chains[i] != null) _chains[i].Dispose();
            _chains[i] = null;
        }
        LichVfx.Stop(ref _sigil, fade ? 0.5f : 0f);
    }

    /// <summary>i번 사슬의 바닥 고리 — 보스 발밑 둘레 네 방향.</summary>
    private Vector3 AnchorOf(Vector3 floor, int i)
        => floor + Quaternion.Euler(0f, 45f + i * 90f, 0f) * Vector3.forward * (_look.ChainRadius * AnchorRadiusScale);

    private static Vector3 FloorUnder(Vector3 p)
    {
        if (Physics.Raycast(p + Vector3.up * FloorProbeUp, Vector3.down, out var hit, FloorProbeDown, GroundMask, QueryTriggerInteraction.Ignore))
            return hit.point;
        return p;
    }
}
}
