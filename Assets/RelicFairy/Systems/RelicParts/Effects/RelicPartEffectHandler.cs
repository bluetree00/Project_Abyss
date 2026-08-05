using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 플레이어(<see cref="PlayerController"/>)에 붙는 유물 파츠 효과 허브.
///
/// 보스 드래프트로 획득한 파츠 id를 받아 <see cref="RelicPartEffectFactory"/>로 효과 인스턴스를 만들고,
/// 전투 신호(적중·치명·피격·스킬·처치·Tick)를 활성 효과들에 팬아웃한다.
/// 신호는 룬 디스패처와 같은 지점(PlayerController.NotifyDamaged/Tick, ItemEffectManager.NotifyHit 등)에서
/// 한 줄씩 갈라 들어온다.
///
/// 파츠는 런 고정이라 활성 목록은 획득 때만 늘고, 런 종료(Detach)에서 한 번에 회수된다.
/// 미구현 effect_key는 빈 스켈레톤이 등록돼 아무 일도 하지 않는다(연결만 보장).
/// </summary>
public sealed class RelicPartEffectHandler
{
    private readonly PlayerController        _player;
    private readonly List<IRelicPartEffect>  _active     = new();
    private readonly HashSet<string>         _activeKeys = new();

    public RelicPartEffectHandler(PlayerController player) => _player = player;

    public IReadOnlyList<IRelicPartEffect> Active => _active;

    /// <summary>이 effect_key가 현재 활성인지. 파츠 간 상호 참조·중복 방지에 사용.</summary>
    public bool IsActive(string effectKey) => _activeKeys.Contains(effectKey);

    /// <summary>
    /// 로드아웃의 파츠 id 목록으로 효과를 동기화한다. 이미 활성인 key는 건너뛰고,
    /// 새로 들어온 key만 만들어 OnAcquire를 발화한다(플레이어 스폰·파츠 획득 시 호출).
    /// </summary>
    public void SyncFromLoadout(IReadOnlyList<string> partIds)
    {
        if (partIds == null) return;

        for (int i = 0; i < partIds.Count; i++)
        {
            var entry = Managers.RelicParts?.GetById(partIds[i]);
            if (entry == null || string.IsNullOrEmpty(entry.effect_key)) continue;
            if (!_activeKeys.Add(entry.effect_key)) continue;   // 이미 활성

            var effect = RelicPartEffectFactory.Create(entry.effect_key);
            _active.Add(effect);
            effect.OnAcquire(_player);
        }
    }

    // ── 신호 팬아웃 (룬 디스패처와 같은 진입점에서 갈라져 들어온다) ──

    public void NotifyHit(in HitInfo hit)
    {
        for (int i = 0; i < _active.Count; i++)
        {
            _active[i].OnHit(hit, _player);
            if (hit.IsCritical) _active[i].OnCrit(hit, _player);
        }
    }

    public void NotifyDamaged(in HitInfo hit)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnDamaged(hit, _player);
    }

    public void NotifySkillUsed()
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnSkillUsed(_player);
    }

    /// <summary>적 처치 통지. deadEnemy = 방금 죽은 적(전염·장판 등 위치 참조용). ItemEffectManager.OnKill에서 호출.</summary>
    public void NotifyKill(GameObject deadEnemy)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnKill(deadEnemy, _player);
    }

    public void Tick(float dt)
    {
        for (int i = 0; i < _active.Count; i++) _active[i].Tick(dt, _player);
    }

    /// <summary>런 종료 시 전체 회수. 각 효과의 패시브를 되돌린다.</summary>
    public void Detach()
    {
        for (int i = 0; i < _active.Count; i++) _active[i].OnRemove(_player);
        _active.Clear();
        _activeKeys.Clear();
    }
}
