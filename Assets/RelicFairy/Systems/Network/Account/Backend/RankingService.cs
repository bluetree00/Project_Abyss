using System.Collections.Generic;
using System.Globalization;
using BackEnd;
using BackEnd.Leaderboard;
using Cysharp.Threading.Tasks;
using Steamworks;
using UnityEngine;

/// <summary>
/// 해방기 · 악몽 클리어 시간 랭킹(10-29 사용자 「해방기 클리어 시간 랭킹 · 악몽도 따로 · 뒤끝 랭킹 · 스팀 이름」 · 기획 설계 §5).
/// 뒤끝 콘솔의 <b>비공개 게임 정보 표</b>(한 유저 한 줄 · 열 clearTime 정수 · weapon 문자열)와 그 표를 쓰는 <b>유저 리더보드</b>(오름차순 — 작은 시간이 1위)를 쓴다.
/// 시간은 1/100초 정수(clearTime)로 올리고, 이전 기록보다 빠를 때만 바꾼다. 이름은 스팀 닉네임(없으면 그대로 둔다).
/// 리더보드 uuid는 표 이름으로 뒤끝에서 찾는다(10-29 — 빌드 뒤 콘솔에서 만들어도 그 빌드가 바로 쓴다). 못 찾으면(콘솔에서 아직 안 만듦) 올리지 않고 기기에 남긴다.
/// 올리기에 실패해도 기기에 남겨 다음 비석 열기 때 다시 올린다.
/// </summary>
public static class RankingService
{
    // ── Constants ─────────────────────────────────────────────
    // 뒤끝 콘솔에서 만든 표 이름 — 리더보드는 이 표를 쓰게 만든다
    public const string LiberationTable = "RANK_LIBERATION";
    public const string NightmareTable  = "RANK_NIGHTMARE";
    private const string ScoreColumn    = "clearTime";
    private const string WeaponColumn   = "weapon";
    private const string PendingKey     = "RelicFairy.Ranking.Pending";   // 「시기|1/100초|무기」 — 올리지 못한 기록
    public  const int    TopCount       = 10;
    private const int    MaxNickname    = 16;   // 뒤끝 닉네임 길이 여유(글자 단위)

    // ── Static ────────────────────────────────────────────────
    private static string _liberationUuid;   // 표 이름으로 찾은 리더보드 uuid(못 찾으면 비어 있다 — 다음에 다시 찾는다)
    private static string _nightmareUuid;

    // ── Types ─────────────────────────────────────────────────
    /// <summary>비석 한 줄.</summary>
    public readonly struct Entry
    {
        public readonly int Rank;
        public readonly string Nickname;
        public readonly int Centis;
        public readonly string Weapon;
        public readonly bool IsMine;
        public Entry(int rank, string nickname, int centis, string weapon, bool isMine)
        { Rank = rank; Nickname = nickname; Centis = centis; Weapon = weapon; IsMine = isMine; }
    }

    // ── Public Methods ────────────────────────────────────────

    /// <summary>이 시기에 랭킹이 있나(해방기 · 악몽 · uuid를 찾음).</summary>
    public static bool IsReady(StoryEra era) => !string.IsNullOrEmpty(UuidOf(era));

    /// <summary>리더보드 uuid를 표 이름으로 찾는다 — 이미 찾았으면 바로 true. 콘솔에 아직 없으면 false.</summary>
    public static async UniTask<bool> ResolveAsync(StoryEra era)
    {
        if (era == StoryEra.Sealed) return false;
        if (IsReady(era)) return true;
        var r = await CallAsync<BackendLeaderboardTableReturnObject>(cb => Backend.Leaderboard.User.GetLeaderboards(cb));
        if (r == null || !r.IsSuccess()) { Debug.LogWarning($"[Ranking] 리더보드 목록 읽기 실패: {r?.GetStatusCode()} {r?.GetMessage()}"); return false; }
        foreach (var it in r.GetLeaderboardTableList())
        {
            if (it.table == LiberationTable && string.IsNullOrEmpty(_liberationUuid)) _liberationUuid = it.uuid;
            else if (it.table == NightmareTable && string.IsNullOrEmpty(_nightmareUuid)) _nightmareUuid = it.uuid;
        }
        if (!IsReady(era)) Debug.LogWarning($"[Ranking] {era} 리더보드 없음 — 뒤끝 콘솔에서 {TableOf(era)} 표를 쓰는 리더보드를 만들면 된다");
        return IsReady(era);
    }

    /// <summary>
    /// 1/100초 → 단위를 붙인 시간(11-05 사용자 「32:23.48 같이 분인지 초인지도 한눈에 · 영어 버전도 맞게 통일하자 영어로」) —
    /// 언어와 상관없이 「52.97s」 · 「32m 23.48s」 · 「1h 02m 03.45s」. 랭킹판 · 기록 카드 · 비석 · 결과창이 같이 쓴다.
    /// </summary>
    public static string Format(int centis)
    {
        if (centis <= 0) return "—";
        int s = centis / 100, m = s / 60, cs = centis % 100;
        if (m >= 60) return $"{m / 60}h {m % 60:00}m {s % 60:00}.{cs:00}s";
        if (m > 0)   return $"{m}m {s % 60:00}.{cs:00}s";
        return $"{s}.{cs:00}s";
    }

    /// <summary>
    /// 랭킹 이름을 스팀 닉네임에 맞춘다 — 로그인 직후(AppBootstrapper)와 기록을 올리기 전(11-05 사용자 「다른 PC에서 켰을 때 이름 없음」).
    /// 예전엔 기록을 올릴 때만 맞춰, 그때 Steam이 안 붙어 있으면(exe 직접 실행 · 옆에 steam_appid.txt 없음) 이름이 끝까지 비었다.
    /// </summary>
    public static UniTask SyncNicknameAsync() => EnsureNicknameAsync();

    /// <summary>클리어 기록을 올린다 — 이전 기록보다 빠를 때만. 실패하면 기기에 남긴다. 해방기 · 악몽만.</summary>
    public static async UniTask<bool> SubmitAsync(StoryEra era, float seconds, string weapon)
    {
        if (era == StoryEra.Sealed || seconds <= 0f) return false;
        int centis = Mathf.RoundToInt(seconds * 100f);
        if (!await ResolveAsync(era)) { Debug.LogWarning($"[Ranking] {era} 리더보드 uuid 없음 — 올리지 않음(기기에 남김)"); SavePending(era, centis, weapon); return false; }
        bool ok = await SubmitCoreAsync(era, centis, weapon);
        if (!ok) SavePending(era, centis, weapon);
        return ok;
    }

    /// <summary>기기에 남은 기록을 다시 올린다(비석을 열 때).</summary>
    public static async UniTask RetryPendingAsync()
    {
        string raw = PlayerPrefs.GetString(PendingKey, "");
        if (string.IsNullOrEmpty(raw)) return;
        var keep = new List<string>();
        foreach (var line in raw.Split('\n'))
        {
            var p = line.Split('|');
            if (p.Length < 3 || !int.TryParse(p[0], out int e) || !int.TryParse(p[1], out int c)) continue;
            var era = (StoryEra)e;
            if (!await ResolveAsync(era) || !await SubmitCoreAsync(era, c, p[2])) keep.Add(line);
        }
        PlayerPrefs.SetString(PendingKey, string.Join("\n", keep));
        PlayerPrefs.Save();
    }

    /// <summary>상위 <see cref="TopCount"/>명 + 내 줄(상위에 없으면 따로). 실패하면 빈 목록.</summary>
    public static async UniTask<(List<Entry> top, Entry? mine)> LoadAsync(StoryEra era)
    {
        var top = new List<Entry>();
        Entry? mine = null;
        string uuid = UuidOf(era);
        if (string.IsNullOrEmpty(uuid)) return (top, null);

        string myInDate = Backend.UserInDate;
        var list = await CallAsync<BackendUserLeaderboardReturnObject>(cb => Backend.Leaderboard.User.GetLeaderboard(uuid, TopCount, cb));
        if (list != null && list.IsSuccess())
            foreach (var it in list.GetUserLeaderboardList())
            {
                var e = ToEntry(it, myInDate);
                top.Add(e);
                if (e.IsMine) mine = e;
            }
        if (mine == null)
        {
            var me = await CallAsync<BackendUserLeaderboardReturnObject>(cb => Backend.Leaderboard.User.GetMyLeaderboard(uuid, cb));
            if (me != null && me.IsSuccess())
                foreach (var it in me.GetUserLeaderboardList())
                    if (it.gamerInDate == myInDate) { mine = ToEntry(it, myInDate); break; }
        }
        return (top, mine);
    }

    // ── Private Methods ───────────────────────────────────────

    private static string UuidOf(StoryEra era) => era switch
    {
        StoryEra.Liberated     => _liberationUuid,
        StoryEra.NightmareMode => _nightmareUuid,
        _                      => "",
    };

    private static string TableOf(StoryEra era) => era == StoryEra.NightmareMode ? NightmareTable : LiberationTable;

    private static UniTask<bool> SubmitCoreAsync(StoryEra era, int centis, string weapon)
        => SubmitRowAsync(TableOf(era), UuidOf(era), centis, weapon, era.ToString());

    // ── 11-02 Q14 보스 · 챕터 기록 표(11-03 시기별 RANK_BOSS_n_LIB · RANK_ROOMS_n_NM 등) — 이름으로 찾고 콘솔에 없으면 올리지 않는다(기기 기록은 RecordBoard) ──
    private static readonly Dictionary<string, string> s_tableUuids = new();
    private static bool  s_tablesListed;
    private static float s_listedAt;
    private const float  RelistSeconds = 60f;   // 없던 표는 1분 뒤 다시 훑는다(11-02 — 콘솔에서 나중에 만든 표)

    /// <summary>표 이름 → 리더보드 uuid(한 번 목록을 훑어 둔다 · 없으면 null).</summary>
    public static async UniTask<string> ResolveTableAsync(string table)
    {
        if (string.IsNullOrEmpty(table)) return null;   // 봉인기 — 챕터 기록 표가 없다(RecordBoard.TableOf)
        if (s_tableUuids.TryGetValue(table, out var u)) return u;
        if (s_tablesListed && Time.realtimeSinceStartup - s_listedAt < RelistSeconds) return null;   // 방금 훑었는데 없었다
        var r = await CallAsync<BackendLeaderboardTableReturnObject>(cb => Backend.Leaderboard.User.GetLeaderboards(cb));
        if (r == null || !r.IsSuccess()) return null;
        s_tablesListed = true;
        s_listedAt     = Time.realtimeSinceStartup;
        foreach (var it in r.GetLeaderboardTableList())
            if (!string.IsNullOrEmpty(it.table) && !s_tableUuids.ContainsKey(it.table)) s_tableUuids[it.table] = it.uuid;
        return s_tableUuids.TryGetValue(table, out u) ? u : null;
    }

    /// <summary>표에 기록을 올린다 — 지난 기록보다 빠를 때만(한 유저 한 줄). 표가 없으면 false.</summary>
    public static async UniTask<bool> SubmitTableAsync(string table, int centis, string weapon)
    {
        string uuid = await ResolveTableAsync(table);
        return !string.IsNullOrEmpty(uuid) && await SubmitRowAsync(table, uuid, centis, weapon, table);
    }

    /// <summary>표 상위 <paramref name="count"/>명(1등 · 2등 등) — 표가 없거나 실패하면 빈 목록.</summary>
    public static async UniTask<List<Entry>> LoadTopAsync(string table, int count)
    {
        var top = new List<Entry>();
        string uuid = await ResolveTableAsync(table);
        if (string.IsNullOrEmpty(uuid)) return top;
        string myInDate = Backend.UserInDate;
        var list = await CallAsync<BackendUserLeaderboardReturnObject>(cb => Backend.Leaderboard.User.GetLeaderboard(uuid, count, cb));
        if (list != null && list.IsSuccess())
            foreach (var it in list.GetUserLeaderboardList()) top.Add(ToEntry(it, myInDate));
        return top;
    }

    /// <summary>한 표의 판(11-02 랭킹 쪽 · 기록 카드) — 콘솔에 표가 있나 · 상위 · 내 줄 · 전체 인원.</summary>
    public readonly struct Board
    {
        public readonly bool        Ready;   // 콘솔에 표가 있다(없으면 기기 기록만)
        public readonly List<Entry> Top;
        public readonly Entry?      Mine;
        public readonly int         Total;
        public Board(bool ready, List<Entry> top, Entry? mine, int total) { Ready = ready; Top = top; Mine = mine; Total = total; }
    }

    /// <summary>표 이름으로 상위 <paramref name="count"/>명 · 내 줄(상위에 없으면 따로 읽는다) · 전체 인원. 표가 없으면 Ready = false.</summary>
    public static async UniTask<Board> LoadBoardAsync(string table, int count = TopCount)
    {
        var top = new List<Entry>();
        string uuid = await ResolveTableAsync(table);
        if (string.IsNullOrEmpty(uuid)) return new Board(false, top, null, 0);

        string myInDate = Backend.UserInDate;
        Entry? mine  = null;
        int    total = 0;
        var list = await CallAsync<BackendUserLeaderboardReturnObject>(cb => Backend.Leaderboard.User.GetLeaderboard(uuid, count, cb));
        if (list != null && list.IsSuccess())
        {
            total = (int)list.GetTotalCount();
            foreach (var it in list.GetUserLeaderboardList())
            {
                var e = ToEntry(it, myInDate);
                top.Add(e);
                if (e.IsMine) mine = e;
            }
        }
        if (mine == null)
        {
            var me = await CallAsync<BackendUserLeaderboardReturnObject>(cb => Backend.Leaderboard.User.GetMyLeaderboard(uuid, cb));
            if (me != null && me.IsSuccess())
                foreach (var it in me.GetUserLeaderboardList())
                    if (it.gamerInDate == myInDate) { mine = ToEntry(it, myInDate); break; }
        }
        return new Board(true, top, mine, Mathf.Max(total, top.Count));
    }

    private static async UniTask<bool> SubmitRowAsync(string table, string uuid, int centis, string weapon, string label)
    {
        await EnsureNicknameAsync();

        // 내 줄(한 유저 한 줄) — 없으면 넣는다. 있으면 지난 기록보다 빠를 때만 바꾼다.
        var mine = await CallAsync(cb => Backend.GameData.GetMyData(table, new Where(), cb));
        if (mine == null || !mine.IsSuccess()) { Debug.LogWarning($"[Ranking] {table} 내 줄 읽기 실패: {mine?.GetMessage()}"); return false; }
        string inDate = null;
        var rows = mine.FlattenRows();
        if (rows != null && rows.Count > 0)
        {
            inDate = rows[0]["inDate"].ToString();
            if (rows[0].ContainsKey(ScoreColumn) && int.TryParse(rows[0][ScoreColumn].ToString(), out int best) && best > 0 && best <= centis)
            {
                Debug.Log($"[Ranking] {label} 기록 {Format(centis)} — 이전 {Format(best)}보다 느려 그대로");
                return true;
            }
        }
        else
        {
            var first = new Param();
            first.Add(ScoreColumn, centis);
            first.Add(WeaponColumn, weapon ?? "");
            var ins = await CallAsync(cb => Backend.GameData.Insert(table, first, cb));
            if (ins == null || !ins.IsSuccess()) { Debug.LogWarning($"[Ranking] {table} 줄 넣기 실패: {ins?.GetMessage()}"); return false; }
            inDate = ins.GetInDate();
        }

        var param = new Param();
        param.Add(ScoreColumn, centis);
        param.Add(WeaponColumn, weapon ?? "");
        var up = await CallAsync<BackendReturnObject>(cb => Backend.Leaderboard.User.UpdateMyDataAndRefreshLeaderboard(uuid, table, inDate, param, cb));
        bool ok = up != null && up.IsSuccess();
        Debug.Log(ok ? $"[Ranking] {label} 기록 올림 {Format(centis)} · {weapon}" : $"[Ranking] {label} 기록 올리기 실패: {up?.GetStatusCode()} {up?.GetMessage()}");
        return ok;
    }

    /// <summary>랭킹 이름 = 스팀 닉네임 — 같은 이름이 있으면 뒤에 번호를 붙인다. 스팀이 없으면 그대로 둔다.</summary>
    private static async UniTask EnsureNicknameAsync()
    {
        if (!SteamManager.Initialized) { Debug.LogWarning("[Ranking] Steam 연결 없음 — 랭킹 이름을 스팀 이름으로 못 맞춤(Steam에서 실행하거나 exe 옆에 steam_appid.txt)"); return; }
        string name = SteamFriends.GetPersonaName()?.Trim();
        if (string.IsNullOrEmpty(name)) { Debug.LogWarning("[Ranking] 스팀 이름이 비어 있다 — 랭킹 이름 그대로"); return; }
        var letters = new StringInfo(name);
        if (letters.LengthInTextElements > MaxNickname) name = letters.SubstringByTextElements(0, MaxNickname);   // 글자 단위로 자른다(이모지 반쪽 방지)
        // 이미 그 이름이면 그대로 — 내 이름을 다시 넣어도 409(같은 이름)라 클리어마다 번호가 붙었다(10-30 실측 「bg000727#4257」)
        string current = Backend.UserNickName;
        if (current == name || (current != null && current.StartsWith(name + "#"))) return;
        for (int i = 0; i < 3; i++)
        {
            string tryName = i == 0 ? name : $"{name}#{Random.Range(1000, 9999)}";
            var r = await CallAsync(cb => Backend.BMember.UpdateNickname(tryName, cb));
            if (r != null && r.IsSuccess()) { Debug.Log($"[Ranking] 랭킹 이름 = {tryName}"); return; }
            if (r != null && r.GetStatusCode() == "409") continue;   // 같은 이름 — 번호를 붙여 다시
            r = await CallAsync(cb => Backend.BMember.CreateNickname(tryName, cb));
            if (r != null && r.IsSuccess()) { Debug.Log($"[Ranking] 랭킹 이름 = {tryName}"); return; }
            if (r == null || r.GetStatusCode() != "409")
            {
                Debug.LogWarning($"[Ranking] 랭킹 이름 정하기 실패({tryName}): {r?.GetStatusCode()} {r?.GetMessage()}");
                return;
            }
        }
        Debug.LogWarning($"[Ranking] 랭킹 이름 정하기 실패 — 「{name}」과 같은 이름이 이미 있다(번호를 붙여도)");
    }

    private static Entry ToEntry(UserLeaderboardItem it, string myInDate)
    {
        int.TryParse(it.rank, out int rank);
        int.TryParse(it.score, out int centis);
        return new Entry(rank, string.IsNullOrEmpty(it.nickname) ? "이름 없음" : it.nickname, centis, it.extraData ?? "", it.gamerInDate == myInDate);
    }

    private static void SavePending(StoryEra era, int centis, string weapon)
    {
        string raw = PlayerPrefs.GetString(PendingKey, "");
        string line = $"{(int)era}|{centis}|{(weapon ?? "").Replace("|", " ").Replace("\n", " ")}";
        PlayerPrefs.SetString(PendingKey, string.IsNullOrEmpty(raw) ? line : raw + "\n" + line);
        PlayerPrefs.Save();
    }

    /// <summary>뒤끝 콜백 하나를 기다린다(메인 스레드로 돌아온다) — 게임 정보 · 닉네임(일반 콜백).</summary>
    private static UniTask<BackendReturnObject> CallAsync(System.Action<Backend.BackendCallback> call)
        => CallAsync<BackendReturnObject>(cb => call(r => cb(r)));

    /// <summary>뒤끝 콜백 하나를 기다린다(메인 스레드로 돌아온다) — 리더보드(제네릭 콜백).</summary>
    private static async UniTask<T> CallAsync<T>(System.Action<Backend.BackendCallback<T>> call) where T : BackendReturnObject
    {
        var tcs = new UniTaskCompletionSource<T>();
        try { call(r => tcs.TrySetResult(r)); }
        catch (System.Exception e) { Debug.LogWarning($"[Ranking] 뒤끝 호출 예외: {e.Message}"); return null; }
        var res = await tcs.Task;
        await UniTask.SwitchToMainThread();
        return res;
    }
}
