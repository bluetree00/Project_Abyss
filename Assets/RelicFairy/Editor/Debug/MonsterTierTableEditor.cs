#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using RelicFairy.Monster;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

/// <summary>
/// 10-11 몬스터 층 · 체력 표 — 일반방 긴장감 · 엘리트 설계(P1) 확인. 편집 중에 돈다(플레이 불필요 · 장면을 건드리지 않는다).
/// 스폰 표(MonsterSpawnTable)의 챕터 × 등급 × 종마다 실제 코드 길로 유효 체력 · 공격을 센다:
///   Config SO 복사본 → JSON 덮어쓰기(<see cref="MonsterJsonData.ApplyToConfig"/>) → 층별 챕터 배율(<see cref="ChapterBalance"/>) · 시기(<see cref="EraBalance"/>).
/// 「시작 1타 13」 기준 타수 · 띠(설계 §3-1) 안 여부 · 방 토큰별 총 체력 기댓값 · 모델 런 길이(DPS 60/80/100/130) · 런 골드를 낸다.
/// 런타임 전용이라 부를 수 없는 식(스포너 활성 · 수량 반올림 · 런 세션 배율)은 같은 식을 옮기고 출처 줄을 달았다.
/// 결과 = Temp/monster_tier_table.txt + 콘솔 「[TierTable] 끝」.
/// </summary>
public static class MonsterTierTableEditor
{
    // ── 상수 ─────────────────────────────────────────────
    private const string SpawnTablePath = "Assets/RelicFairy/Characters/Monster/Monster/Core/MonsterSpawnTable.asset";
    private const string PoolPathFormat = "Assets/RelicFairy/Docs/CHAPTER_{0}_ROOM_POOL.csv";
    private const string OutPath        = "Temp/monster_tier_table.txt";
    private const StoryEra Era          = StoryEra.Sealed;   // 첫 회차(봉인기) 기준
    private const int   AbyssDepth      = 0;
    private const float StartHit        = 13f;   // 설계 §0 「시작 1타」(가웨인 근접 5 + 카타나 T1 6 + 스텝 2)
    private const float GawainHp        = 950f;  // CHARACTER_DATA 가웨인 체력
    private const float GawainDefense   = 37f;   // 방어 32 + 카타나 5 — 받는 피해 ×100/(100+방어)(PlayerController.Combat)
    private const float ShopShelfPrice  = 3351f; // 상점 진열가 합(DieState.GoldScale 주석)
    private const float CoverageTarget  = 0.66f;
    // 런 길이 모델(메모 project_playtime_design_target): 전투방 Σ(25초 + 방 총 체력 ÷ DPS) + 특수방 × 45초 + 보스전방 × 25초 + 보스방 × 120초
    private const float RoomOverheadSec = 25f, SpecialSec = 45f, PreBossSec = 25f, BossSec = 120f;
    private const float SpecialsPerChapter = 3.8f;
    private const float LowerBoundFix   = 1.15f;  // 09-22 정정 — 예전 실측은 하한
    private const float RunLimitMinutes = 50f;
    private const float EventVisitsPerChapter = 0.95f;   // 이벤트 캡 1 × 특수방 3.8/4 — 골드 집계에만
    private static readonly float[] DpsCases = { 60f, 80f, 100f, 130f };
    // 적용 전(옛 편성 · 10-11 적용 직전 데이터)을 같은 모델로 낸 챕터별 전투방 총 체력 합 — 나란히 보기용 고정값
    private static readonly float[] OldChapterHp    = { 2514f, 3492f, 10143f, 46290f };
    private static readonly int[]   OldChapterRooms = { 5, 5, 6, 6 };
    // 층 띠(설계 §3-1 · Ch1 기준값 · 챕터 배율 전) — Common · Rare · Elite
    private static readonly int[] BandHpLo  = { 12, 45, 120 }, BandHpHi  = { 20, 70, 160 };
    private static readonly int[] BandAtkLo = { 20, 40, 70 },  BandAtkHi = { 30, 55, 95 };
    private static readonly string[] TierName = { "잡몹", "위협", "정예" };

    private sealed class Species
    {
        public string       Name;
        public MonsterGrade TableGrade, Grade;
        public int[]        Tags;
        public float        Weight;
        public int          Hp, CoinMin, CoinMax;
        public float        Atk, Def;
        public string       Source;
    }

    private sealed class RoomCalc
    {
        public ZonePoolEntry Entry;
        public float[] Token = new float[3];   // 토큰 기대 마릿수(배율 전)
        public float[] Count = new float[3];   // 수량 배율 + 두 번째 파도 기대 포함
        public float   Hp;
        public bool    Legacy;
    }

    // ── 메뉴 ──────────────────────────────────────────────

    [MenuItem("RelicFairy/Debug/10-11 몬스터 층 · 체력 표 (편집 모드)")]
    private static void Run()
    {
        var sb = new StringBuilder();
        try { Build(sb); }
        catch (Exception e) { sb.AppendLine($"✗ 예외: {e}"); }

        Directory.CreateDirectory("Temp");
        File.WriteAllText(OutPath, sb.ToString());
        Debug.Log(sb.ToString());
        Debug.Log($"[TierTable] 끝 — {OutPath}");
    }

    // ── 본문 ──────────────────────────────────────────────

    private static void Build(StringBuilder sb)
    {
        var era = EraBalance.For(Era);
        sb.AppendLine($"[TierTable] 몬스터 층 · 체력 표 — 시기 {Era}(스탯 ×{era.StatScale:0.##} · 두 번째 파도 {era.ExtraWaveChance:P0}) · 심연 깊이 {AbyssDepth} · 시작 1타 {StartHit:0}");

        var table = AssetDatabase.LoadAssetAtPath<MonsterSpawnTableSO>(SpawnTablePath);
        if (table == null) { sb.AppendLine($"✗ 스폰 표 없음: {SpawnTablePath}"); return; }

        var addr    = AddressMap();
        var species = LoadSpecies(table, addr, sb);

        // 1) 종 표
        sb.AppendLine("\n━━ 1. 종 표(챕터 × 층 × 종) — 기준값은 Ch1 · 챕터 배율 전 / 유효값 = 이 챕터에서 실제 스폰되는 값 ━━");
        int outBand = 0, mismatch = 0;
        var oneTwoHit = new List<string>();
        for (int ch = 1; ch <= 4; ch++)
        {
            var bal = ChapterBalance.Of((ChapterId)ch);
            sb.AppendLine($"Ch{ch} — 체력 배율 잡몹 ×{bal.CommonHp:0.##} · 위협 ×{bal.RareHp:0.##} · 정예 ×{bal.EliteHp:0.##} · 공격 ×{bal.Attack:0.##} · 마릿수 ×{bal.Count:0.##}");
            for (int g = 0; g < 3; g++)
                foreach (var s in Roster(species, ch, (MonsterGrade)g))
                {
                    int ti = Mathf.Min((int)s.Grade, 2);
                    bool inBand = s.Hp >= BandHpLo[ti] && s.Hp <= BandHpHi[ti] && s.Atk >= BandAtkLo[ti] && s.Atk <= BandAtkHi[ti];
                    int effHp   = EffectiveHp(s, ch);
                    int effAtk  = EffectiveAttack(s, ch);
                    float pct   = effAtk * 100f / (100f + GawainDefense) / GawainHp * 100f;
                    string flag = (inBand ? "띠 안" : "✗ 띠 밖") + (s.TableGrade != s.Grade ? $" · ⚠️ 표 등급 {s.TableGrade} ≠ Config {s.Grade}" : "");
                    sb.AppendLine($"  {TierName[ti]} {s.Name,-14} 기준 체력 {s.Hp,4} · 공격 {s.Atk,3:0} · 방어 {s.Def,2:0} | 유효 체력 {effHp,5} · 공격 {effAtk,4}" +
                                  $" (가웨인 {pct:0.0}%) | 시작 1타 {Hits(effHp, s.Def),4}타 | {flag} | {s.Source}");
                    if (ch == FirstChapter(s))
                    {
                        if (!inBand) outBand++;
                        if (s.TableGrade != s.Grade) mismatch++;
                        if (Hits(s.Hp, s.Def) <= 2) oneTwoHit.Add($"{s.Name}({TierName[ti]})");
                    }
                }
        }
        sb.AppendLine($"요약: 띠 밖 {outBand}종 · 표/Config 등급 다름 {mismatch}종 · 시작 1타로 1~2타(기준값) = {string.Join(", ", oneTwoHit)}");

        // 2) 방 표
        sb.AppendLine("\n━━ 2. 방 토큰별 기대값(배율 · 두 번째 파도 기대 포함) ━━");
        var rooms = new List<RoomCalc>[5];
        var eventRooms = new int[5];
        for (int ch = 1; ch <= 4; ch++)
        {
            rooms[ch] = LoadRooms(ch, species, sb, out eventRooms[ch]);
            sb.AppendLine($"Ch{ch} — 방 {rooms[ch].Count}개(전투 토큰 있는 방만)");
            foreach (var r in rooms[ch])
            {
                string check = RoomCheck(r);
                sb.AppendLine($"  {r.Entry.pool_key,-26} {r.Entry.category,-6} ds {r.Entry.difficulty_scale,-5:0.##} max {r.Entry.max_active_spawners}" +
                              $" | 토큰 C{r.Token[0]:0.#} R{r.Token[1]:0.#} E{r.Token[2]:0.#}" +
                              $" | 기대 C{r.Count[0]:0.0} R{r.Count[1]:0.0} E{r.Count[2]:0.0} | 총 체력 {r.Hp,6:N0}{(r.Legacy ? " · ⚠️ 1구간(레거시) 토큰" : "")}{check}");
            }
        }

        // 3) 런 모델
        sb.AppendLine("\n━━ 3. 모델 런 길이(봉인기 · 특수방 3.8/챕터 × 45초 · 보스전방 25초 · 보스 120초 · 전투방 25초 + 총 체력 ÷ DPS) ━━");
        var chHp = new float[5]; var chRooms = new int[5];
        var kills = new float[5, 3];
        for (int ch = 1; ch <= 4; ch++)
        {
            var rs = LoadStructure(ch, out string src);
            if (rs == null) { sb.AppendLine($"✗ Ch{ch} 런 구조 없음"); continue; }
            ChapterModel(rooms[ch], rs, era, out chHp[ch], out var k);
            chRooms[ch] = rs.BossThreshold;
            for (int g = 0; g < 3; g++) kills[ch, g] = k[g] + EventKills(rooms[ch], eventRooms[ch], g);
            sb.AppendLine($"Ch{ch} 전투방 {rs.BossThreshold} · 정예방 확률 {rs.EliteChance + era.EliteBonus:0.##} · 곡선 {rs.DifficultyAt(0):0.##}→{rs.DifficultyAt(rs.BossThreshold - 1):0.##}" +
                          $" | 방 총 체력 합 {chHp[ch],7:N0} · 방당 {chHp[ch] / Mathf.Max(1, rs.BossThreshold),6:N0} (옛 {OldChapterHp[ch - 1],7:N0}) | 런 구조 {src}");
        }
        sb.AppendLine("DPS | 새 편성(분) · ×1.15 | 옛 편성(분) · ×1.15 | 50분 이하(×1.15)");
        bool allOk = true;
        foreach (float dps in DpsCases)
        {
            float now = RunMinutes(chHp, chRooms, dps), old = RunMinutes(OldChapterHp, OldChapterRooms, dps, true);
            bool ok = now * LowerBoundFix <= RunLimitMinutes;
            if (dps >= 80f && !ok) allOk = false;
            sb.AppendLine($"{dps,4:0} | {now,5:0.0} · {now * LowerBoundFix,5:0.0} | {old,5:0.0} · {old * LowerBoundFix,5:0.0} | {(ok ? "예" : "✗ 넘음")}");
        }
        sb.AppendLine($"합격(DPS 80~130에서 ×1.15 ≤ {RunLimitMinutes:0}분): {(allOk ? "통과" : "✗ 실패")}");

        // 4) 골드
        sb.AppendLine("\n━━ 4. 런 골드(전투방 + 이벤트 전투방 처치 기대 × 종별 코인 × DieState.GoldScale) ━━");
        var goldScale = typeof(DieState).GetMethod("GoldScale", BindingFlags.NonPublic | BindingFlags.Static);
        float gold = 0f; var kt = new float[3];
        for (int ch = 1; ch <= 4; ch++)
            for (int g = 0; g < 3; g++)
            {
                kt[g] += kills[ch, g];
                float scale = goldScale != null ? (float)goldScale.Invoke(null, new object[] { (MonsterGrade)g }) : 0f;
                gold += kills[ch, g] * AvgCoins(Roster(species, ch, (MonsterGrade)g), scale);
            }
        float kAll = kt[0] + kt[1] + kt[2];
        sb.AppendLine($"처치 C{kt[0]:0.0} R{kt[1]:0.0} E{kt[2]:0.0} = {kAll:0.0} (비중 C{kt[0] / kAll:P0} R{kt[1] / kAll:P0} E{kt[2] / kAll:P0})" +
                      $" | 일반몹 골드 {gold:N0} · 충당률 {gold / ShopShelfPrice:P1}(목표 {CoverageTarget:P0}){(goldScale == null ? " · ✗ GoldScale 못 찾음" : "")}");
    }

    // ── 종 ────────────────────────────────────────────────

    private static List<Species> LoadSpecies(MonsterSpawnTableSO table, Dictionary<string, string> addr, StringBuilder sb)
    {
        var list = new List<Species>();
        var cfgProp  = typeof(MonsterBase).GetProperty("ConfigAddress", BindingFlags.Instance | BindingFlags.NonPublic);
        var dataProp = typeof(MonsterBase).GetProperty("DataAddress",   BindingFlags.Instance | BindingFlags.NonPublic);
        foreach (var e in table.entries)
        {
            if (e == null || !e.enabled || e.grade == MonsterGrade.Boss || e.poolTags == null || e.poolTags.Length == 0) continue;
            var prefab = Load<GameObject>(addr, e.addressableKey);
            var mb     = prefab != null ? prefab.GetComponent<MonsterBase>() : null;
            string cfgAddr  = mb != null ? cfgProp?.GetValue(mb) as string : null;
            string dataAddr = mb != null ? dataProp?.GetValue(mb) as string : null;
            var cfg = Load<MonsterConfigSO>(addr, cfgAddr);
            if (cfg == null) { sb.AppendLine($"✗ {e.displayName}: Config 못 찾음({cfgAddr ?? "프리팹 없음"})"); continue; }

            // MonsterBase.InitAsync와 같은 길 — 원본 보호용 복사본 → JSON 덮어쓰기(LoadAndApplyJsonDataAsync)
            var copy = UnityEngine.Object.Instantiate(cfg);
            string source = "SO";
            var json = string.IsNullOrEmpty(dataAddr) ? null : Load<TextAsset>(addr, dataAddr);
            if (json != null)
            {
                JsonUtility.FromJson<MonsterJsonData>(json.text)?.ApplyToConfig(copy);
                source = "SO+JSON";
            }
            list.Add(new Species
            {
                Name = e.displayName.Replace("Monster", ""), TableGrade = e.grade, Grade = copy.grade, Tags = e.poolTags,
                Weight = Mathf.Max(0f, e.weight), Hp = copy.stat.maxHp, Atk = copy.stat.attackPower, Def = copy.stat.defense,
                CoinMin = copy.drop != null ? copy.drop.coinMin : 0, CoinMax = copy.drop != null ? copy.drop.coinMax : 0, Source = source,
            });
            UnityEngine.Object.DestroyImmediate(copy);
        }
        return list;
    }

    /// <summary>스포너가 고르는 칸 — 표 등급이 정확히 그 등급(MonsterSpawner.PassesGradeFilter · Exact)이고 풀 태그에 챕터(ApplyCurrentChapterGroup)가 있는 종.</summary>
    private static List<Species> Roster(List<Species> all, int ch, MonsterGrade g)
        => all.FindAll(s => s.TableGrade == g && Array.IndexOf(s.Tags, ch) >= 0);

    private static int FirstChapter(Species s)
    {
        int min = int.MaxValue;
        foreach (int t in s.Tags) min = Mathf.Min(min, t);
        return min;
    }

    /// <summary>GameRunSession.CurrentDifficultyScaleFor × MonsterBase.EffectiveMaxHp(보스 · 분열 배율 1).</summary>
    private static int EffectiveHp(Species s, int ch)
        => Mathf.RoundToInt(s.Hp * ChapterBalance.Of((ChapterId)ch).HpFor(s.Grade) * (1f + AbyssDepth * 0.25f) * EraBalance.For(Era).StatScale);

    /// <summary>GameRunSession.CurrentAttackScale × attackPower(MonsterBase 근접 판정 · 패턴 배율 전).</summary>
    private static int EffectiveAttack(Species s, int ch)
        => (int)(s.Atk * ChapterBalance.Of((ChapterId)ch).Attack * (1f + AbyssDepth * 0.25f) * EraBalance.For(Era).StatScale);

    /// <summary>MonsterBase.TakeDamage — max(1, 피해 − 방어).</summary>
    private static int Hits(int hp, float def) => Mathf.CeilToInt(hp / Mathf.Max(1f, StartHit - def));

    private static float AvgHp(List<Species> roster, int ch)
    {
        float w = 0f, sum = 0f;
        foreach (var s in roster) { w += s.Weight; sum += s.Weight * EffectiveHp(s, ch); }
        return w > 0f ? sum / w : 0f;
    }

    /// <summary>DieState.SpawnGoldDrop — 코인 min~max 균등 × 등급 배율, 반올림(Mathf.RoundToInt) · 최소 1.</summary>
    private static float AvgCoins(List<Species> roster, float scale)
    {
        float w = 0f, sum = 0f;
        foreach (var s in roster)
        {
            int lo = Mathf.Max(0, s.CoinMin), hi = Mathf.Max(lo, s.CoinMax);
            float c = 0f; int n = 0;
            for (int k = lo; k <= hi; k++, n++) c += k <= 0 ? 0 : Mathf.Max(1, Mathf.RoundToInt(k * scale));
            w += s.Weight; sum += s.Weight * (n > 0 ? c / n : 0f);
        }
        return w > 0f ? sum / w : 0f;
    }

    // ── 방 ────────────────────────────────────────────────

    private static List<RoomCalc> LoadRooms(int ch, List<Species> species, StringBuilder sb, out int eventRooms)
    {
        var result = new List<RoomCalc>();
        eventRooms = 0;
        var asset  = AssetDatabase.LoadAssetAtPath<TextAsset>(string.Format(PoolPathFormat, ch));
        var parse  = typeof(ZoneLayoutManager).GetMethod("ParsePoolCsv", BindingFlags.NonPublic | BindingFlags.Static);
        if (asset == null || parse == null) { sb.AppendLine($"✗ Ch{ch} 룸 풀 CSV 또는 파서 없음"); return result; }

        var pool = new List<ZonePoolEntry>();
        parse.Invoke(null, new object[] { asset.text.TrimStart(), pool });   // ZoneLayoutManager.LoadPoolAsync Addressables 폴백과 같은 길

        var bal     = ChapterBalance.Of((ChapterId)ch);
        float cs    = bal.Count * EraBalance.For(Era).CountScale;           // GameRunSession.CurrentMonsterCountScale
        var avgHp   = new float[3];
        for (int g = 0; g < 3; g++) avgHp[g] = AvgHp(Roster(species, ch, (MonsterGrade)g), ch);
        int extraCommons = ExtraWaveCommons();

        foreach (var e in pool)
        {
            if (string.Equals(e.category, "Event", StringComparison.OrdinalIgnoreCase)) eventRooms++;
            if (string.IsNullOrEmpty(e.grid_csv)) continue;
            var infos = new Dictionary<Vector2Int, MapDataLoader.CellSpawnInfo>();
            var grid  = MapDataLoader.Parse(e.grid_csv, infos, new Dictionary<Vector2Int, string>(), new Dictionary<Vector2Int, DoorInfo>());
            if (grid == null || infos.Count == 0) continue;

            var r = new RoomCalc { Entry = e };
            var fixedSpots = MapDataLoader.FindAll(grid, TileType.MonsterSpawn);
            var candSpots  = MapDataLoader.FindAll(grid, TileType.MonsterSpawnCandidate);
            // GameRunBootstrapper.ApplyMonsterSpawnerPlan — 확정 전부 + 후보 중 (max − 확정)개 무작위 → 후보 한 칸의 활성 확률
            int max = e.max_active_spawners;
            float pCand = max <= 0 ? 1f
                        : fixedSpots.Count >= max || candSpots.Count == 0 ? 0f
                        : Mathf.Min(max - fixedSpots.Count, candSpots.Count) / (float)candSpots.Count;
            foreach (var kv in infos)
            {
                float p = grid[kv.Key.x, kv.Key.y] == TileType.MonsterSpawn ? 1f : pCand;
                var info = kv.Value;
                if (info.waves != null && info.waves.Length >= 2)
                {
                    foreach (var w in info.waves)
                    {
                        int g = Mathf.Min((int)w.grade, 2);
                        r.Token[g] += p * w.count;
                        r.Count[g] += p * ExpectedScaled(w.count, cs);   // MonsterSpawner.SpawnGroupsAsync — 묶음마다 ScaleCount
                    }
                }
                else
                {
                    r.Legacy = true;   // 1구간 토큰 = 레거시 자동 루프(AtMost) — 근사로 그 등급에 센다
                    int g = Mathf.Min((int)info.maxGrade, 2);
                    r.Token[g] += p * info.totalCount;
                    r.Count[g] += p * info.totalCount;
                }
            }

            // 시기 두 번째 파도(EraBalance.RollExtraWave · MonsterSpawner.SpawnExtraAsync) — 일반 · 정예 전투방만 · 정예 수는 배율 없음
            var kind = string.Equals(e.category, "Elite", StringComparison.OrdinalIgnoreCase) ? RoomPlanKind.Elite
                     : string.Equals(e.category, "Normal", StringComparison.OrdinalIgnoreCase) ? RoomPlanKind.Normal : RoomPlanKind.Event;
            var spec = EraBalance.For(Era);
            if (kind != RoomPlanKind.Event && spec.ExtraWaveChance > 0f)
            {
                int elites = kind == RoomPlanKind.Elite && Era == StoryEra.NightmareMode ? 2 : 1;
                r.Count[2] += spec.ExtraWaveChance * elites;
                r.Count[0] += spec.ExtraWaveChance * ExpectedScaled(extraCommons, cs);
            }
            for (int g = 0; g < 3; g++) r.Hp += r.Count[g] * avgHp[g];
            result.Add(r);
        }
        return result;
    }

    /// <summary>MonsterSpawner.ScaleCount의 기대값 — floor + 확률 반올림 · 최소 1.</summary>
    private static float ExpectedScaled(int n, float scale) => n <= 0 ? 0f : Mathf.Max(1f, n * scale);

    private static int ExtraWaveCommons()
    {
        var f = typeof(EraBalance).GetField("ExtraWaveCommons", BindingFlags.NonPublic | BindingFlags.Static);
        return f != null ? Convert.ToInt32(f.GetRawConstantValue()) : 3;
    }

    private static string RoomCheck(RoomCalc r)
    {
        string cat = r.Entry.category;
        if (string.Equals(cat, "Elite", StringComparison.OrdinalIgnoreCase))
        {
            // 정예방 정예 2~3 — 정예 묶음 하나(e2)면 수량 배율 반올림으로 2~3에 머문다
            return r.Token[2] >= 2f && r.Token[2] <= 3f ? " | 정예 2~3 예" : " | ✗ 정예방 정예 2~3 아님";
        }
        var sb = new StringBuilder(" |");
        sb.Append(r.Token[2] <= 1f ? " 정예 ≤1 예" : " ✗ 정예 >1");
        sb.Append(r.Token[1] >= 1f && r.Token[1] <= 4f ? " · 위협 1~4 예" : " · ✗ 위협 1~4 아님");
        float perThreat = r.Token[1] > 0f ? r.Token[0] / r.Token[1] : 0f;
        sb.Append(perThreat >= 3f && perThreat <= 5f ? $" · 잡몹/위협 {perThreat:0.#}" : $" · △ 잡몹/위협 {perThreat:0.#}");
        return sb.ToString();
    }

    /// <summary>이벤트방 한 번 방문의 처치 기대 — 이벤트는 난이도 창 없이 챕터 이벤트 풀에서 고르게 뽑는다(RunSequencer.PickEntry).
    /// 전투형만 토큰이 있으므로 비전투 이벤트(0마리)까지 포함한 방 수로 나눈다.</summary>
    private static float EventKills(List<RoomCalc> rooms, int eventRooms, int g)
    {
        float sum = 0f;
        foreach (var r in rooms)
            if (string.Equals(r.Entry.category, "Event", StringComparison.OrdinalIgnoreCase)) sum += r.Count[g];
        return eventRooms > 0 ? EventVisitsPerChapter * sum / eventRooms : 0f;
    }

    // ── 런 구조 ───────────────────────────────────────────

    /// <summary>RUN_STRUCTURE 정본(CDN) — 편집 중엔 RunStructureDataManager 로컬 캐시(persistentDataPath/run_structure.json)를 읽는다. 없으면 SO 폴백.</summary>
    private static IRunStructure LoadStructure(int ch, out string source)
    {
        string path = Path.Combine(Application.persistentDataPath, "run_structure.json");
        if (File.Exists(path))
        {
            var col = JsonUtility.FromJson<RunStructureCollection>(File.ReadAllText(path));
            var hit = col?.entries?.Find(x => x.chapter_id == ((ChapterId)ch).ToString());
            if (hit != null) { source = "CDN 캐시"; return hit; }
        }
        source = "SO 폴백(RunStructure_Ch" + ch + ")";
        return AssetDatabase.LoadAssetAtPath<RunStructureConfig>($"Assets/RelicFairy/Systems/Stage/MapGen/Data/RunStructure_Ch{ch}.asset");
    }

    /// <summary>
    /// 챕터 전투방 기대 — RunSequencer: 보스까지 BossThreshold개 전투방, 방마다 정예 확률(EliteChance + 시기 가산),
    /// 방은 곡선 목표(DifficultyAt(bossProgress)) ± DifficultyTolerance 안에서 균등(PickEntry · 쿨다운은 무시).
    /// </summary>
    private static void ChapterModel(List<RoomCalc> rooms, IRunStructure rs, EraBalance.Spec era, out float hp, out float[] kills)
    {
        hp = 0f; kills = new float[3];
        var tolField = typeof(RunSequencer).GetField("DifficultyTolerance", BindingFlags.NonPublic | BindingFlags.Static);
        float tol = tolField != null ? Convert.ToSingle(tolField.GetRawConstantValue()) : 0.35f;
        var normal = rooms.FindAll(r => string.Equals(r.Entry.category, "Normal", StringComparison.OrdinalIgnoreCase));
        var elite  = rooms.FindAll(r => string.Equals(r.Entry.category, "Elite",  StringComparison.OrdinalIgnoreCase));
        float pe = Mathf.Clamp01(rs.EliteChance + era.EliteBonus);
        for (int k = 0; k < rs.BossThreshold; k++)
        {
            float target = rs.DifficultyAt(k);
            Accumulate(normal, target, tol, 1f - pe, ref hp, kills);
            Accumulate(elite,  target, tol, pe,      ref hp, kills);
        }
    }

    private static void Accumulate(List<RoomCalc> pool, float target, float tol, float weight, ref float hp, float[] kills)
    {
        if (pool.Count == 0 || weight <= 0f) return;
        var win = pool.FindAll(r => Mathf.Abs(r.Entry.difficulty_scale - target) <= tol);
        if (win.Count == 0) win = pool;
        foreach (var r in win)
        {
            hp += weight * r.Hp / win.Count;
            for (int g = 0; g < 3; g++) kills[g] += weight * r.Count[g] / win.Count;
        }
    }

    private static float RunMinutes(float[] chapterHp, int[] rooms, float dps, bool zeroBased = false)
    {
        float sec = 4f * (SpecialsPerChapter * SpecialSec + PreBossSec + BossSec);
        for (int ch = 1; ch <= 4; ch++)
        {
            int i = zeroBased ? ch - 1 : ch;
            sec += rooms[i] * RoomOverheadSec + chapterHp[i] / dps;
        }
        return sec / 60f;
    }

    // ── 에셋 ──────────────────────────────────────────────

    /// <summary>Addressables 주소 → 에셋 경로(AddressableManager가 로드하는 그 항목).</summary>
    private static Dictionary<string, string> AddressMap()
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) return map;
        foreach (var group in settings.groups)
        {
            if (group == null) continue;
            foreach (var entry in group.entries)
                if (entry != null && !string.IsNullOrEmpty(entry.address)) map[entry.address] = entry.AssetPath;
        }
        return map;
    }

    private static T Load<T>(Dictionary<string, string> map, string address) where T : UnityEngine.Object
    {
        if (string.IsNullOrEmpty(address)) return null;
        if (map.TryGetValue(address, out var path)) return AssetDatabase.LoadAssetAtPath<T>(path);
        // 주소가 없으면 이름으로(주소 끝 = 에셋 이름 규칙)
        string name = address.Substring(address.LastIndexOf('/') + 1);
        foreach (var guid in AssetDatabase.FindAssets($"{name} t:{typeof(T).Name}"))
        {
            var p = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(p) == name) return AssetDatabase.LoadAssetAtPath<T>(p);
        }
        return null;
    }
}
#endif
