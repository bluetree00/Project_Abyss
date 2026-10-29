using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 런 결과 Provider(10-07) — 런이 끝나는 순간 세션 · 로드아웃 · 룬판 · 서약에서 결과창 값을 떠 둔다.
/// <para>순서가 중요하다: <see cref="GameRunSession.EndRun"/>이 룬판을 비우고(ClearBoard) 곧 로드아웃이 지워지므로
/// <see cref="Capture"/>는 EndRun <b>앞</b>, 정산(계약 · 초행 정수)이 끝난 값은 EndRun <b>뒤</b> <see cref="Settle"/>로 채운다.</para>
/// </summary>
public static class RunResultProvider
{
    public static RunResultSnapshot Capture(GameRunSession s, PlayerLoadout loadout, bool cleared)
    {
        var snap = new RunResultSnapshot { Cleared = cleared };
        if (s == null) return snap;
        var stats = s.RunStats ?? new RunStats();

        // ── 머리 ──
        snap.ChapterNumber = (int)s.CurrentChapter;
        snap.ChapterName   = s.ChapterName(s.CurrentChapter);
        snap.Era           = s.StartEra;
        snap.AbyssDepth    = s.AbyssDepth;
        snap.PlaySeconds   = s.PlaySeconds;
        snap.KilledBy      = cleared ? string.Empty : stats.LastHitBy;
        BuildSegments(snap, s, stats);

        // ── 재화(연료 · 계약 수) — 정수 · 골드는 Settle ──
        snap.EnhanceMaterial = stats.EnhanceMaterialGained;
        snap.RuneOre         = stats.RuneOreGained;
        snap.ContractsDone   = RunContracts.Current?.DoneCount(s) ?? 0;

        // ── 빌드 ──
        CaptureWeapons(snap, s);
        CaptureRelic(snap, loadout);
        CaptureBoard(snap, s.ItemInventory);
        CaptureCovenants(snap, s);
        snap.CovenantFires = stats.CovenantFires;

        // ── 전투(피해) ──
        snap.BiggestHit       = stats.BiggestHit;
        snap.BiggestHitSource = stats.BiggestHitSource;
        for (int i = 0; i < RunStats.SourceCount; i++) snap.Damage[i] = stats.DamageOf((DamageSource)i);
        return snap;
    }

    /// <summary>EndRun이 정산한 값(정수 · 계약 · 초행 · 골드 · 처치 · 방)을 채운다.</summary>
    public static void Settle(RunResultSnapshot snap, in EndRunResult r)
    {
        if (snap == null) return;
        snap.Essence              = r.GainedEssence;
        snap.EssenceFromContracts = r.ContractEssence;
        snap.EssenceFromFirstRun  = r.FirstRunBonus;
        snap.Gold                 = r.GainedGold;
        snap.RoomClears           = r.RoomClears;
        snap.Kills                = r.Kills;
        snap.EliteKills           = r.EliteKills;
        snap.BossKills            = r.BossKills;
        snap.FlawlessChapters     = r.FlawlessChapters;
    }

    // ── Private Methods ──

    private static void BuildSegments(RunResultSnapshot snap, GameRunSession s, RunStats stats)
    {
        int prevEnd = 0;
        for (int i = 0; i < stats.ChapterEndCount; i++)
        {
            var (ch, end) = stats.ChapterEndAt(i);
            bool known = stats.TryChapterClockAt(i, out float combat, out float baseSec);
            snap.Segments.Add(new RunResultSnapshot.Segment
            {
                Label = Label(s, ch), Seconds = Mathf.Max(0, end - prevEnd), Done = true,
                CombatSeconds = known ? Mathf.RoundToInt(combat) : -1,
                Grade = known ? RunTimeGrade.LetterInk(combat, baseSec) : string.Empty,
            });
            prevEnd = end;
        }
        // 지금 챕터 — 완주면 마친 챕터라 등급까지, 쓰러졌으면 전투 시간만(등급은 끝낸 챕터에만 · §6)
        int cur = (int)s.CurrentChapter;
        snap.Segments.Add(new RunResultSnapshot.Segment
        {
            Label = Label(s, cur), Seconds = Mathf.Max(0, s.PlaySeconds - prevEnd), Done = true,
            CombatSeconds = Mathf.RoundToInt(stats.CombatSeconds),
            Grade = snap.Cleared ? RunTimeGrade.LetterInk(stats.CombatSeconds, stats.BaseSeconds) : string.Empty,
        });
        for (int c = cur + 1; c <= s.FinalChapterNumber; c++)
            snap.Segments.Add(new RunResultSnapshot.Segment { Label = Label(s, c), Seconds = 0, Done = false, CombatSeconds = -1, Grade = string.Empty });
    }

    private static string Label(GameRunSession s, int chapter)
    {
        string name = s.ChapterName((ChapterId)chapter);
        return string.IsNullOrEmpty(name) ? $"Ch{chapter}" : $"Ch{chapter} {name}";
    }

    private static void CaptureWeapons(RunResultSnapshot snap, GameRunSession s)
    {
        var wm = s.Player != null ? s.Player.WeaponManager : null;
        WeaponData w0 = wm != null ? wm.Weapon0Data : SavedSlot(s, 0);
        WeaponData w1 = wm != null ? wm.Weapon1Data : SavedSlot(s, 1);
        int active = wm != null ? wm.CurrentSlotIndex : Mathf.Max(0, s.SavedCurrentSlotIndex);
        var a = Line(active == 1 ? w1 : w0);
        var b = Line(active == 1 ? w0 : w1);
        snap.Weapon    = a ?? b;
        snap.SubWeapon = a != null ? b : null;
    }

    private static WeaponData SavedSlot(GameRunSession s, int i)
        => s.SavedWeaponSlots != null && i < s.SavedWeaponSlots.Length ? s.SavedWeaponSlots[i] : null;

    private static RunResultSnapshot.WeaponLine Line(WeaponData w)
    {
        if (w == null || string.IsNullOrEmpty(w.displayName)) return null;
        return new RunResultSnapshot.WeaponLine
        {
            Name     = w.displayName,
            Icon     = w.icon,
            Enhance  = w.enhanceLevel,
            FormName = w.form != null ? w.form.DisplayName : string.Empty,
            FormTier = w.formTier,
            Ranged   = SkillTierResolver.IsRanged(w),
            Build    = BuildOf(w),
        };
    }

    /// <summary>
    /// 무기 빌드 한 줄(10-22 사용자 「무기 빌드도 간단히 보이게」) — 근접 = 재련으로 고른 증강 「이름 단계」, 원거리 = 켠 파츠 「이름 Lv」.
    /// 런 끝(EndRun) 전에 찍는다 — 파츠 상태는 EndRun에서 비워진다.
    /// </summary>
    private static string BuildOf(WeaponData w)
    {
        var parts = new List<string>();
        if (SkillTierResolver.IsRanged(w))
        {
            var st = RangedPartsState.Current;
            if (st != null)
                foreach (var e in st.Equipped_)
                {
                    var def = Managers.WeaponParts?.GetById(e.partId);
                    if (def != null) parts.Add($"{def.part_name} Lv{e.level}");
                }
        }
        else
        {
            foreach (var (id, rank) in w.AugmentList())
            {
                var def = MeleeAugmentCatalog.Get(id);
                if (def != null) parts.Add(def.IsLegend || def.IsGrowth ? def.Name : $"{def.Name} {MeleeAugmentDef.RankName(rank)}".TrimEnd());   // 전설 · 자람(10-26 재련으로 자람)은 단계가 없다
            }
        }
        return string.Join(" · ", parts);
    }

    private static void CaptureRelic(RunResultSnapshot snap, PlayerLoadout loadout)
    {
        if (loadout?.Relic == null) return;
        snap.RelicName = loadout.Relic.DisplayName;
        snap.RelicIcon = loadout.Relic.QSkillIcon;
        foreach (var id in loadout.RelicPartIds)
        {
            switch (loadout.GetRelicPartGrade(id))
            {
                case RelicMemoryGrade.Radiant: snap.MemRadiant++; break;
                case RelicMemoryGrade.Clear:   snap.MemClear++;   break;
                default:                       snap.MemFaint++;   break;
            }
        }
    }

    /// <summary>룬판 — <see cref="RuneBoardMini.Build"/>와 같은 규칙(존맵 · 점유 · 배치 기록)으로 칸만 뜬다.</summary>
    private static void CaptureBoard(RunResultSnapshot snap, RunItemInventory inventory)
    {
        snap.RuneCount = inventory != null ? inventory.PlacedCount : 0;
        var rows = Managers.RuneData?.GetZoneMapRows();
        var bridge = MerlinRuneBridge.Instance;
        if (rows == null || rows.Count == 0) return;

        int maxLen = 0;
        foreach (var r in rows) if (r?.pattern != null) maxLen = Mathf.Max(maxLen, r.pattern.Length);

        var occupied = new HashSet<Vector2Int>();
        var cells = bridge != null ? bridge.CaptureRuneCells() : null;
        if (cells != null) foreach (var c in cells) occupied.Add(c);

        var cellElement = new Dictionary<Vector2Int, string>();
        var placements = bridge != null ? bridge.CaptureRunePlacements() : null;
        if (placements != null && inventory != null)
            foreach (var p in placements)
            {
                var item = FindPlaced(inventory, p?.instanceId);
                if (item == null || p.cells == null) continue;
                foreach (var c in p.cells) cellElement[c] = item.element;
            }

        foreach (var r in rows)
        {
            if (r?.pattern == null) continue;
            int colOffset = (maxLen - r.pattern.Length) / 2;
            for (int c = 0; c < r.pattern.Length; c++)
            {
                char code = r.pattern[c];
                var pos = new Vector2Int(c + colOffset, r.hex_row);
                string el = null;
                if (occupied.Contains(pos))
                {
                    el = cellElement.TryGetValue(pos, out var e) ? e : ElementDef.CodeToId(code);
                    snap.BoardFilled++;
                }
                snap.BoardTotal++;
                snap.Board.Add(new RunResultSnapshot.RuneCell { X = pos.x, Y = pos.y, Element = el, Zone = code });
            }
        }

        if (bridge == null) return;
        foreach (var id in ElementDef.Order)
        {
            int tier = bridge.GetZoneTier(id);
            if (tier > 0) snap.Synergies.Add((id, tier));
        }
    }

    private static RuntimeItemData FindPlaced(RunItemInventory inventory, string instanceId)
    {
        if (string.IsNullOrEmpty(instanceId)) return null;
        foreach (var it in inventory.PlacedItems) if (it != null && it.instanceId == instanceId) return it;
        return null;
    }

    private static void CaptureCovenants(RunResultSnapshot snap, GameRunSession s)
    {
        var list = s.CovenantHandler?.Covenants;
        if (list == null) return;
        foreach (var c in list)
        {
            if (c == null) continue;
            var line = new RunResultSnapshot.CovenantLine { Name = c.DisplayName ?? string.Empty, Cause = c.CauseText ?? string.Empty };
            if (c is CovenantSentence sen && sen.Resolved)
            {
                line.Tiers = new CovenantTier[sen.ResultCount + 1];
                line.Tiers[0] = sen.CauseTier;
                for (int i = 0; i < sen.ResultCount; i++) line.Tiers[i + 1] = sen.ResultTier(i);
            }
            snap.Covenants.Add(line);
        }
    }
}
