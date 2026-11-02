using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using RelicFairy.Monster;
using TMPro;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 보물고 — 비전투 이벤트방. 제한시간 동안 흩어진 상자를 최대한 개봉(욕심 vs 안전).
/// 10-29 개편(설계 §3 · B2) — 상자 5개. 진짜 상자 모델을 세우고(욕심의 상자와 같은 모델), 조작이 풀린 뒤에야 시간이 흐른다.
/// 상자마다 <b>금화 · 강화재료 · 정수</b> 중 하나가 바로 튄다(실버 몫의 절반 × 챕터 — <see cref="EventRewards"/>). 무엇이 든지는 방 시드로 정해져 있다.
/// 시간이 다하거나 전부 열면 개봉 수로 등급(알림 · 기록용): 5개 Platinum / 4 Gold / 3 Silver / 2 Bronze / ≤1 Fail.
/// 10-29 사용자 「보물 창고가 다 모여있어서 찾는 느낌이 안 나 — 방에 고루 넣고 함정도 틈틈이 · 느려지게 하거나 빨라지게 하는 장치」
///   · 상자 = 방 전체에 고루(이미 놓은 상자 · 입구에서 가장 먼 자리부터 차례로)
/// 10-31 사용자 「그냥 걸어가서 완료될 미니게임이면 의미가 없어 — 기능을 이용해서 부족한 시간 안에 열도록」
///   · 진흙 · 바람이 실제 속도를 바꾸지 못했다(이동 배율이 입력 방향에만 곱해져 정규화로 사라졌다) → 바닥 배율 채널(<see cref="PlayerController.FloorSpeedScale"/>)
///   · 상자는 1번부터 차례로 연다 — 다음 상자 = 방금 연 상자에서 걸어서 가장 먼 상자(지그재그). 아무 차례로나 열게 두면
///     작은 방(Ch2 22 m)에선 가장 짧은 길이 28 m라 달리기만으로 4초 안에 다 열렸다 — 이제 구간마다 방을 가로지른다
///   · 상자는 붙어야 연다(1.6 m) · 제한 시간 = 그 차례를 달리기만 했을 때(실제로 걷는 길 · 상자마다 0.5초)의 0.82배(방마다 · 5 ~ 18초) — 달리기만으론 셋 ~ 넷에서 끝난다
///   · 구간마다 바람 문양(두 배 · 1.5초)은 곧은 길 옆 2.2 m — 비켜 밟아야 한다 · 진흙 · 가시는 곧은 길 위 — 곧장 달리면 밟는다
///   · 시간은 첫 걸음부터 흐른다(안내를 읽을 틈 · 안 움직여도 4초 뒤엔 흐른다)
/// </summary>
public sealed class TreasureVaultChallenge : WorldInteractionChallenge
{
    public const int    ChestCount = 5;     // 10-30 문 배지(EventRewards.Badge)가 읽는다
    private const string RoomTitle = "보물 창고";   // 10-30 안내 · 타이머 · 끝 알림 = 문 배지와 같은 이름(챕터 테마 이름은 상자 이름표에만)
    private const ChallengeGrade ChestShare = ChallengeGrade.Silver;   // 상자 하나 = 실버 몫의 절반

    // 상자 흩기
    private const float SpreadSearch    = 16f;    // 방 가운데에서 ±이만큼 2 m 간격으로 자리를 찾는다(방 32 ~ 36 m)
    private const float SpreadStep      = 2f;
    private const float ChestWallClear  = 1.2f;
    private const float ChestEntryClear = 5f;

    // 시간(10-31)
    private const float RunShare     = 0.82f;  // 여는 차례대로 달리기만 했을 때 걸리는 시간의 이만큼
    private const float StartSlack   = 0.4f;   // 출발 가속(걷기 → 달리기 1초)
    private const float TurnSlack    = 0.5f;   // 상자마다 돌아서기 · [F] · 사람 반응(10-31 0.15 → 0.5 — 바람을 타도 빠듯했다)
    private const float LimitMin     = 5f;
    private const float LimitMax     = 18f;
    private const float StartMove    = 1.5f;   // 이만큼 움직이면 시간이 흐른다
    private const float StartWaitMax = 4f;     // 안 움직여도 이만큼 뒤엔 흐른다
    private const float ChestReach   = 1.6f;   // 10-31 상자는 붙어야 연다(다른 방 [F] 2.8 m) — 2.8 m 앞에서 열고 되돌아서면 작은 방에선 달릴 길이 거의 없었다
    private const float WalkStep     = 0.25f;  // 제한 시간 잴 때 길을 따라 걷는 한 걸음

    // 장치
    private enum Gizmo { Spike, Mud, Wind }
    private const float GizmoChestClear = 2.2f;
    private const float GizmoGap        = 0.5f;   // 장치끼리 가장자리 사이
    private const float WindLead        = 4f;     // 구간 들머리 — 앞 상자에서 이만큼
    private const float WindSide        = 2.2f;   // 곧은 길에서 옆으로 — 비켜 서야 밟는다(10-31 1.6 → 2.2 — 작은 방에선 그냥 달려도 밟혔다)
    private const float HazardShare     = 0.6f;   // 진흙 · 가시 = 구간의 이만큼(곧은 길 위)
    private const float HazardAfterWind = 3.8f;   // 바람 문양에서 적어도 이만큼 뒤
    private const float WindLegGap      = 0.6f;   // 10-31 바람 문양 가장자리 ↔ 어느 구간이든 곧은 길 — 그냥 달려서는 안 밟히게
    private const float SpikeRadius     = 1.3f;
    private const float SpikePeriod     = 2.4f;   // 쉼 → 예고 → 솟음 한 바퀴
    private const float SpikeWarn       = 0.8f;
    private const float SpikeStumble    = 0.35f;  // 찔리면 비틀거림(이동 배율) — 시간이 걸린 놀이라 체력보다 이게 아프다
    private const float SpikeStumbleSec = 0.7f;
    private const float MudRadius       = 2.2f;
    private const float MudSlow         = 0.45f;  // 이동 배율(55% 느려짐)
    private const float WindRadius      = 1.2f;
    private const float WindHaste       = 2f;
    private const float WindSeconds     = 1.5f;
    private const float WindRecharge    = 3f;
    private static readonly Color SpikeColor = new(0.95f, 0.35f, 0.25f);
    private static readonly Color MudColor   = new(0.45f, 0.32f, 0.2f);
    private static readonly Color WindColor  = new(0.45f, 0.9f, 1f);

    private enum Loot { Gold, Material, Essence }
    private static readonly Loot[] LootSet = { Loot.Gold, Loot.Gold, Loot.Material, Loot.Material, Loot.Essence };

    private static NavMeshPath s_path;

    private sealed class Device
    {
        public Gizmo      Kind;
        public Vector3    Pos;
        public float      Radius, T, Recharge;
        public GameObject Disc;
    }

    private readonly Loot[] _loot = new Loot[ChestCount];
    private readonly List<Device> _devices = new();
    private static readonly Color LaterColor = new(0.62f, 0.64f, 0.68f);   // 아직 차례가 아닌 상자 번호

    private List<Vector3> _route = new();   // 10-31 여는 차례(입구 → 상자 다섯 · 지그재그) — 표식 번호와 같다
    private string      _chestName = string.Empty;
    private int         _next;              // 지금 열 상자(표식 번호)
    private float       _routeMeters;
    private float       _timeLimit = LimitMax;
    private float       _timeLeft = LimitMax;
    private float       _waitStart;
    private int         _opened;
    private bool        _started;
    private bool        _running;
    private int         _shownSecond = -1;
    private int         _shownOpened = -1;
    private int         _labeledKinds;   // 진흙 · 바람 이름표는 종류마다 처음 하나만(10-31 장치가 열 개 남짓으로 늘었다 — 원판이 보이고 뜻은 시작 안내가 말한다)
    private Vector3     _center;
    private Vector3     _entry;
    private Vector3     _startPos;
    private TextMeshPro _timerLabel;
    private bool        _guidesBorrowed;   // 10-30 MinigameFx.BeginGuides를 빌렸다 — 걷힐 때 돌려준다

    /// <summary>SetupEventRoom이 호출. seed는 방 결정적 RNG 파생 — 상자마다 든 것(다시 들어와도 같다).</summary>
    public void Initialize(GameRunSession run, int seed)
    {
        InitBase(run);
        Array.Copy(LootSet, _loot, ChestCount);
        var rng = new System.Random(seed);
        for (int i = ChestCount - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (_loot[i], _loot[j]) = (_loot[j], _loot[i]);
        }
    }

    /// <summary>10-30 자체 타이머(남은 시간 · 상자 수)를 띄운다 — 바탕의 60초 남은 시간 글은 끈다(제한 시간 안에 이미 끝난다).</summary>
    protected override bool ShowsAutoResolveTimer => false;

    /// <summary>10-31 상자에 붙어야 연다.</summary>
    protected override float ActivateRange => ChestReach;

    protected override void PlaceMarkers(Transform player)
    {
        _center = Stage;
        // 10-30 장치 원판(진흙 · 바람 · 가시 예고)을 놀이방과 같은 원 데칼로 — 기본 도형 폴백은 빌드에서 셰이더가 빠져 예외였다
        if (!_guidesBorrowed) { MinigameFx.BeginGuides(); _guidesBorrowed = true; }
        var ct = this.GetCancellationTokenOnDestroy();
        Vector3 entry = player.position;
        _entry = entry;
        _route = ZigzagOrder(entry, SpreadSpots(entry), out _routeMeters);   // 10-31 표식 번호 = 여는 차례
        _chestName = ChallengeFlavor.ChestName(Run);
        for (int i = 0; i < _route.Count; i++)
        {
            AddMarker(_route[i], string.Empty, UIPalette.Gold, string.Empty, i);   // 이름표 · [F] 글은 RefreshOrder가 차례대로 쓴다
            SpawnChestAsync(_route[i], ct).Forget();   // 글자만 떠 있어 「상자가 안 나온다」로 읽혔다(B2)
        }
        RefreshOrder();
        _timeLimit = LimitFor(_routeMeters, Run?.Player);
        _timeLeft = _timeLimit;
        PlaceDevices(entry, _route, _route);
        Debug.Log($"[TreasureVault] 상자 {_route.Count} · 장치 {_devices.Count} · 차례대로 달릴 길 {_routeMeters:0.0}m · 제한 {_timeLimit:0}초");
    }

    protected override void Tick()
    {
        if (IsResolved) return;
        var player = Run?.Player;
        if (!_started)
        {
            // 조작이 풀린 첫 프레임 — 안내 · 타이머를 띄운다. 시간은 첫 걸음부터(10-31)
            _started = true;
            _waitStart = Time.time;
            _startPos = player != null ? player.transform.position : _center;
            _timerLabel = MakeText(string.Empty, _center + Vector3.up * 3.2f, 4f, UIPalette.Gold, 12);
            // 10-30 할 일을 첫 줄에, 장치 뜻을 둘째 줄에(알림 칸이 하나라 두 줄 한 번에)
            Notice(Loc.F("{0} — 상자 {1}개를 1번부터 차례로 <color={2}>{3:0}초</color> 안에 [F]로 연다 · 움직이면 시간이 흐른다\n", RoomTitle, ChestCount, UIPalette.GoldHex, _timeLimit) +
                   "달리기만으론 모자란다 — <color=#73E5FF>바람 문양</color>을 밟으면 두 배로 빨라진다 · <color=#B08A5A>진흙</color>은 느려지고 <color=#F26A55>가시</color>는 비틀거린다");
            var first = MarkerAt(_next);
            if (first != null) RunFx.Play(RunFxSlot.Ring, first.Pos, 0.5f, UIPalette.Gold);   // 1번 상자 자리 한 번 — 눈길
        }

        if (_timerLabel != null && Camera.main != null)
            _timerLabel.transform.rotation = Camera.main.transform.rotation;

        if (!_running)
            _running = Time.time - _waitStart >= StartWaitMax
                    || (player != null && Flat(player.transform.position - _startPos) >= StartMove);

        TickDevices(Time.deltaTime);

        if (_running) _timeLeft -= Time.deltaTime;
        int sec = Mathf.Max(0, Mathf.CeilToInt(_timeLeft));
        if (_timerLabel != null && (sec != _shownSecond || _opened != _shownOpened))   // 초 · 연 수가 바뀔 때만 글자를 고친다(매 프레임 문자열 할당 금지)
        {
            _shownSecond = sec;
            _shownOpened = _opened;
            _timerLabel.SetText(Loc.T("남은 시간 {0}초 · 상자 {1}/{2}"), sec, _opened, ChestCount);   // 10-30 몇 개 남았는지가 없었다
        }
        if (_timeLeft <= 0f) Resolve();
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null || m.Consumed) return;
        if (markerIndex != _next) return;   // 10-31 차례가 아닌 상자는 안 열린다(가까이 가면 「n번 상자부터」가 뜬다)

        ConsumeMarker(markerIndex);
        Managers.Sound?.PlayEvent(SoundEvent.VaultOpen);
        _opened++;
        _next++;
        SetLabel(markerIndex, PopLoot((int)m.Tag, m.Pos), new Color(0.6f, 0.62f, 0.66f));

        if (_opened >= ChestCount) { Resolve(); return; }
        RefreshOrder();
        var nm = MarkerAt(_next);
        if (nm != null) RunFx.Play(RunFxSlot.Ring, nm.Pos, 0.5f, UIPalette.Gold);   // 다음 상자 자리 한 번 — 눈길
    }

    /// <summary>상자마다 바로 튀었다 — 끝에 더 줄 것은 없다.</summary>
    protected override UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct) => UniTask.CompletedTask;

    protected override void OnDestroyChallenge()
    {
        ClearFloorSpeed();   // 10-31 끝나기 전에 방이 사라져도 바닥 배율이 남지 않게
        if (!_guidesBorrowed) return;
        _guidesBorrowed = false;
        MinigameFx.EndGuides();
    }

    // ── Private Methods ───────────────────────────────────
    private void Resolve()
    {
        if (IsResolved) return;
        if (_timerLabel != null) _timerLabel.gameObject.SetActive(false);
        ClearDevices();
        ClearFloorSpeed();

        ChallengeGrade grade = _opened >= 5 ? ChallengeGrade.Platinum
                             : _opened == 4 ? ChallengeGrade.Gold
                             : _opened == 3 ? ChallengeGrade.Silver
                             : _opened == 2 ? ChallengeGrade.Bronze
                             :                ChallengeGrade.Fail;
        // 10-30 보상은 상자마다 이미 나왔다 — 등급은 보상과 상관없어 알림에서 뺀다(등급은 기록 · 로그용으로만)
        Notice(Loc.F("{0} 끝 — 상자 {1}/{2}개 열었다", RoomTitle, _opened, ChestCount));
        FinishWith(grade, _center);
    }

    /// <summary>10-31 진흙 · 가시 · 바람이 건 바닥 배율을 바로 1로 — 놀이가 끝나면 남기지 않는다(타이머가 짧아도 확실히).</summary>
    private void ClearFloorSpeed()
    {
        var player = Run?.Player;
        if (player != null) player.ClearFloorSpeed();
    }

    /// <summary>
    /// 상자 자리 — 방 가운데 둘레(±16 m · 2 m 간격)의 걸을 수 있는 자리(벽에서 1.2 m · 입구에서 5 m 밖 · 입구와 길로 이어진 곳) 가운데
    /// 이미 고른 자리 · 입구에서 가장 먼 곳을 차례로 고른다 — 방 전체에 고루 퍼진다(「찾는 느낌」). 자리가 모자라면 무대 둥근 줄로 채운다.
    /// </summary>
    private List<Vector3> SpreadSpots(Vector3 entry)
    {
        var cands = new List<Vector3>(256);
        Vector3 root = transform.position;
        for (float x = -SpreadSearch; x <= SpreadSearch; x += SpreadStep)
        for (float z = -SpreadSearch; z <= SpreadSearch; z += SpreadStep)
        {
            if (!MinigameArena.Snap(new Vector3(root.x + x, entry.y, root.z + z), 0.8f, out var p)) continue;
            if (!InRoom(p)) continue;   // 10-30 방 밖 복도(출구 · 입구 통로) 바닥 — Ch2 보물 창고에서 상자가 닫힌 출구 복도 위에 섰다
            if (Flat(p - entry) < ChestEntryClear) continue;
            if (!NavMesh.FindClosestEdge(p, out var edge, MinigameArena.Filter) || edge.distance < ChestWallClear) continue;
            if (!MinigameArena.IsLinked(entry, p)) continue;
            cands.Add(p);
        }

        var picked = new List<Vector3>(ChestCount);
        while (picked.Count < ChestCount && cands.Count > 0)
        {
            int best = -1; float bestD = -1f;
            for (int i = 0; i < cands.Count; i++)
            {
                float d = Flat(cands[i] - entry) * 0.6f;   // 입구와는 조금 덜 떨어져도 된다
                for (int k = 0; k < picked.Count; k++) d = Mathf.Min(d, Flat(cands[i] - picked[k]));
                if (d > bestD) { bestD = d; best = i; }
            }
            picked.Add(cands[best]);
            cands.RemoveAt(best);
        }
        for (int i = picked.Count; i < ChestCount; i++) picked.Add(StageSpot(360f * i / ChestCount, 4.2f));
        return picked;
    }

    /// <summary>
    /// 10-31 여는 차례 — 입구에서 걸어서 가장 먼 상자가 1번, 그다음은 방금 연 상자에서 걸어서 가장 먼 상자(지그재그).
    /// 구간마다 방을 가로질러 방이 작아도 달릴 길이 나온다. <paramref name="meters"/> = 그 차례로 실제로 걷는 거리 —
    /// 걸어서 가는 길(NavMesh)을 따라 상자에 붙는 자리까지 걷고, 다음 구간은 그 자리에서 시작한다(되돌아서는 구간이 짧아지는 것까지).
    /// </summary>
    private static List<Vector3> ZigzagOrder(Vector3 entry, List<Vector3> spots, out float meters)
    {
        var left  = new List<Vector3>(spots);
        var order = new List<Vector3>(spots.Count);
        meters = 0f;
        Vector3 at = entry;
        while (left.Count > 0)
        {
            int far = 0; float farD = -1f;
            for (int i = 0; i < left.Count; i++)
            {
                float d = PathLength(at, left[i]);
                if (d > farD) { farD = d; far = i; }
            }
            order.Add(left[far]);
            meters += WalkToReach(at, left[far], out at);
            left.RemoveAt(far);
        }
        return order;
    }

    /// <summary>걸어서 가는 길을 따라 <paramref name="chest"/>에 붙는(열 수 있는) 첫 자리까지 — 걸은 거리 · 그 자리.</summary>
    private static float WalkToReach(Vector3 from, Vector3 chest, out Vector3 stopAt)
    {
        s_path ??= new NavMeshPath();
        var pts = NavMesh.CalculatePath(from, chest, MinigameArena.Filter, s_path) && s_path.status == NavMeshPathStatus.PathComplete
            ? s_path.corners : new[] { from, chest };
        Vector3 at = from;
        float walked = 0f;
        for (int i = 1; i < pts.Length && Flat(chest - at) > ChestReach; i++)
        {
            while (Flat(chest - at) > ChestReach)
            {
                Vector3 d = pts[i] - at; d.y = 0f;
                float len = d.magnitude;
                if (len < 0.01f) break;
                float s = Mathf.Min(WalkStep, len);
                at += d / len * s;
                walked += s;
            }
        }
        stopAt = at;
        return walked;
    }

    /// <summary>10-31 차례 표시 — 지금 열 상자 = 금색 「n번 · 이름」 + [F] 열기, 뒤 상자 = 회색 「n번」(가까이 가면 「m번 상자부터」). 열 때마다 한 번.</summary>
    private void RefreshOrder()
    {
        for (int i = 0; i < _route.Count; i++)
        {
            var m = MarkerAt(i);
            if (m == null || m.Consumed) continue;
            bool now = i == _next;
            SetLabel(i, now ? Loc.F("{0}번 · {1}", i + 1, _chestName) : Loc.F("{0}번", i + 1), now ? UIPalette.Gold : LaterColor);
            if (m.Prompt != null)
                m.Prompt.text = now ? Loc.F("<color={0}>[F]</color> 열기", UIPalette.GoldHex) : Loc.F("<color=#9AA0A8>{0}번 상자부터</color>", _next + 1);
        }
    }

    /// <summary>걸어서 가는 길이(NavMesh 꺾인 점 합) — 길을 못 찾으면 곧은 거리.</summary>
    private static float PathLength(Vector3 a, Vector3 b)
    {
        s_path ??= new NavMeshPath();
        if (!NavMesh.CalculatePath(a, b, MinigameArena.Filter, s_path) || s_path.status != NavMeshPathStatus.PathComplete)
            return Flat(b - a);
        var c = s_path.corners;
        float sum = 0f;
        for (int i = 1; i < c.Length; i++) sum += Flat(c[i] - c[i - 1]);
        return sum;
    }

    /// <summary>
    /// 10-31 제한 시간 = 여는 차례대로 달리기만 했을 때(지금 달리기 속도 · 아이템 배율 포함 · 상자마다 0.5초) × 0.82 — 0.5초 단위로 올림 · 5 ~ 18초.
    /// 달리기만으론 셋째 상자 남짓에서 끝나고, 바람 문양을 이어 밟고 진흙을 돌아가면 약 0.6배로 다섯을 다 연다.
    /// </summary>
    private static float LimitFor(float runMeters, PlayerController player)
    {
        var cd = player != null ? player.CharacterData : null;
        float run = cd != null && cd.baseRunSpeed > 0.01f ? cd.baseRunSpeed : 8f;
        if (player != null && player.RuntimeStats != null) run *= player.RuntimeStats.MoveSpeedMultiplier;
        float runSeconds = runMeters / Mathf.Max(1f, run) + StartSlack + TurnSlack * ChestCount;
        return Mathf.Clamp(Mathf.Ceil(runSeconds * RunShare * 2f) * 0.5f, LimitMin, LimitMax);   // 0.5초 단위로 올림
    }

    /// <summary>
    /// 10-31 여는 차례의 구간마다 — 바람 문양 = 구간 들머리(앞 상자에서 4 m · 곧은 길 옆 2.2 m: 비켜 서야 밟는다),
    /// 진흙 · 가시(번갈아) = 구간의 60%쯤 곧은 길 위(곧장 달리면 밟는다). 예전엔 구간 가운데 장치 하나씩이라 곧장 걸어도 다 열렸다.
    /// </summary>
    private void PlaceDevices(Vector3 entry, List<Vector3> route, List<Vector3> chests)
    {
        Vector3 at = entry;
        for (int g = 0; g < route.Count; g++)
        {
            Vector3 to = route[g];
            Vector3 d = to - at; d.y = 0f;
            float len = d.magnitude;
            if (len > 1f)
            {
                Vector3 dir  = d / len;
                Vector3 side = Vector3.Cross(Vector3.up, dir) * (g % 2 == 0 ? WindSide : -WindSide);
                float lead = g == 0 ? ChestEntryClear + 0.5f : WindLead;
                if (len - lead > ChestReach + 1f)
                {
                    // 10-31 다른 구간의 곧은 길 위면 그냥 달려도 밟힌다(작은 방에선 지그재그 구간이 서로 엇갈린다) — 반대쪽 옆도 본다
                    Vector3 lane = at + dir * lead;
                    if (!TryAddDevice(Gizmo.Wind, lane + side, chests, entry, route))
                        TryAddDevice(Gizmo.Wind, lane - side, chests, entry, route);
                }
                TryAddDevice(g % 2 == 0 ? Gizmo.Mud : Gizmo.Spike, at + dir * Mathf.Max(len * HazardShare, lead + HazardAfterWind), chests, entry);
            }
            at = to;
        }
    }

    /// <summary>장치 하나를 세운다 — 세웠으면 true. <paramref name="route"/>를 주면(바람 문양) 어느 구간이든 곧은 길 가까이엔 세우지 않는다.</summary>
    private bool TryAddDevice(Gizmo kind, Vector3 want, List<Vector3> chests, Vector3 entry, List<Vector3> route = null)
    {
        if (!MinigameArena.Snap(want, 1.5f, out var p)) return false;
        if (!InRoom(p)) return false;   // 10-30 복도 바닥엔 장치를 두지 않는다
        float r = kind == Gizmo.Mud ? MudRadius : kind == Gizmo.Wind ? WindRadius : SpikeRadius;
        foreach (var c in chests) if (Flat(p - c) < GizmoChestClear) return false;
        foreach (var d in _devices) if (Flat(p - d.Pos) < d.Radius + r + GizmoGap) return false;
        if (Flat(p - entry) < ChestEntryClear) return false;
        if (route != null && NearLeg(p, entry, route, r + WindLegGap)) return false;

        var dev = new Device { Kind = kind, Pos = p, Radius = r, T = UnityEngine.Random.value * SpikePeriod };
        switch (kind)
        {
            case Gizmo.Mud:
                dev.Disc = Keep(PatternGuideHelper.Disc(p, r, MudColor));
                if ((_labeledKinds & 1 << (int)kind) == 0) DeviceLabel("진흙 · 느려짐", p, MudColor);
                break;
            case Gizmo.Wind:
                dev.Disc = Keep(PatternGuideHelper.Disc(p, r, WindColor));
                if ((_labeledKinds & 1 << (int)kind) == 0) DeviceLabel("바람 · 두 배 빨라짐", p, WindColor);
                break;
            default:
                DeviceLabel("가시 · 찔리면 비틀", p, SpikeColor);   // 가시는 쉴 땐 원이 없다 — 이름표가 자리 표시라 하나하나 단다
                break;
        }
        _labeledKinds |= 1 << (int)kind;
        _devices.Add(dev);
        return true;
    }

    /// <summary>입구 → 상자 → … 곧은 길 어느 구간에서든 <paramref name="within"/> 안인가.</summary>
    private static bool NearLeg(Vector3 p, Vector3 entry, List<Vector3> route, float within)
    {
        Vector3 a = entry;
        foreach (var b in route)
        {
            Vector3 ab = b - a, ap = p - a;
            ab.y = 0f; ap.y = 0f;
            float t = ab.sqrMagnitude > 0.0001f ? Mathf.Clamp01(Vector3.Dot(ap, ab) / ab.sqrMagnitude) : 0f;
            if ((ap - ab * t).magnitude < within) return true;
            a = b;
        }
        return false;
    }

    private void TickDevices(float dt)
    {
        var player = Run?.Player;
        if (player == null) return;
        Vector3 pp = player.transform.position;
        foreach (var d in _devices)
        {
            bool inside = Flat(pp - d.Pos) <= d.Radius;
            switch (d.Kind)
            {
                case Gizmo.Mud:
                    if (inside) player.ApplyFloorSlow(MudSlow, 0.25f);   // 서 있는 동안 계속(나오면 0.25초 뒤 풀림)
                    break;
                case Gizmo.Wind:
                    d.Recharge -= dt;
                    if (inside && d.Recharge <= 0f)
                    {
                        d.Recharge = WindRecharge;
                        player.ApplyHaste(WindHaste, WindSeconds);
                        RunFx.Play(RunFxSlot.Ring, d.Pos, 0.4f, WindColor);
                        Managers.Sound?.PlayUiEvent(SoundEvent.UiSelect, 1.4f, 0.6f);
                    }
                    break;
                default:
                    TickSpike(d, dt, pp, player);
                    break;
            }
        }
    }

    /// <summary>가시 — 쉼(옅은 원 없음) → 예고(원이 차오른다 0.8초) → 솟음(원 안이면 4% · 비틀거림) → 처음으로.</summary>
    private void TickSpike(Device d, float dt, Vector3 playerPos, PlayerController player)
    {
        float before = d.T;
        d.T = (d.T + dt) % SpikePeriod;
        float warnStart = SpikePeriod - SpikeWarn;
        if (d.T >= warnStart)
        {
            if (d.Disc == null)
            {
                d.Disc = Keep(PatternGuideHelper.Prepare(PatternGuideHelper.Disc(d.Pos, d.Radius, SpikeColor), SpikeColor));
            }
            PatternGuideHelper.SetProgress(d.Disc, (d.T - warnStart) / SpikeWarn);
            return;
        }
        if (d.Disc == null || before < warnStart) return;
        // 한 바퀴를 넘겼다 — 솟는다
        PatternGuideHelper.Arm(d.Disc);
        RunFx.Play(RunFxSlot.Ring, d.Pos, 0.45f, SpikeColor);
        if (Flat(playerPos - d.Pos) <= d.Radius + 0.3f && !player.IsInvincible)   // 회피로 피했으면 비틀거리지도 않는다(피해와 같은 규칙)
        {
            MinigameFx.Hurt(player);
            player.ApplyFloorSlow(SpikeStumble, SpikeStumbleSec);
        }
        PatternGuideHelper.SafeDestroy(ref d.Disc);
    }

    /// <summary>
    /// 방 안 바닥인가(10-30) — 탐색 범위(±16 m)가 방(22 ~ 32 m)보다 넓어 NavMesh로 이어진 출구 · 입구 복도(MapBuilder 「Corridor_F_…」)까지 후보에 들었다.
    /// 바로 아래 바닥 콜라이더가 복도 블록이면 방 밖으로 본다.
    /// </summary>
    private static bool InRoom(Vector3 p)
    {
        if (!Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out var hit, 4f, ~0, QueryTriggerInteraction.Ignore)) return true;
        return !hit.collider.name.StartsWith("Corridor", System.StringComparison.Ordinal);
    }

    private void ClearDevices()
    {
        foreach (var d in _devices) PatternGuideHelper.SafeDestroy(ref d.Disc);
        _devices.Clear();
    }

    /// <summary>장치 이름 — 바닥 가까이 작게. 방 카메라는 방위가 고정이라 세울 때 한 번 카메라를 보게 한다(거울 글자 막기).</summary>
    private void DeviceLabel(string text, Vector3 at, Color color)
    {
        var t = MakeText(text, at + Vector3.up * 0.6f, 2.4f, color, 9);
        if (Camera.main != null) t.transform.rotation = Camera.main.transform.rotation;
    }

    /// <summary>세운 것을 챌린지에 매단다 — 방 · 컴포넌트와 함께 사라진다.</summary>
    private GameObject Keep(GameObject go)
    {
        if (go != null) go.transform.SetParent(Props, true);
        return go;
    }

    private static float Flat(Vector3 v) { v.y = 0f; return v.magnitude; }

    /// <summary>상자 하나에 든 것을 튀긴다. 금화 · 정수는 상자와 플레이어 사이에 떨어진다(자석 거리 안). 표시할 글자를 돌려준다.</summary>
    private string PopLoot(int chest, Vector3 chestPos)
    {
        int ch = EventRewards.Chapter(Run);
        Vector3 at = Vector3.Lerp(chestPos, EventRewards.PlayerPosOr(chestPos), 0.5f);
        switch (_loot[Mathf.Clamp(chest, 0, ChestCount - 1)])
        {
            case Loot.Material:
            {
                int n = EventRewards.Material(ChestShare, ch) / 2;
                EventRewards.AddMaterial(Run, n);
                return Loc.F("강화재료 +{0}", n);
            }
            case Loot.Essence:
            {
                int n = EventRewards.Essence(ChestShare, ch) / 2;
                EventRewards.DropEssence(at, n);
                return Loc.F("정수 +{0}", n);
            }
            default:
            {
                int n = EventRewards.Gold(ChestShare, ch) / 2;
                EventRewards.DropGold(at, n);
                return Loc.F("금화 +{0}", n);
            }
        }
    }

    /// <summary>상자 모델 하나 — 욕심의 상자와 같은 모델(의태 상자의 모델만 · 닫힌 자세). 방이 사라지거나 이 컴포넌트를 걷으면 함께 사라진다.</summary>
    private async UniTaskVoid SpawnChestAsync(Vector3 pos, CancellationToken ct)
    {
        try
        {
            Vector3 look = _center - pos; look.y = 0f;
            var rot  = Quaternion.LookRotation(look.sqrMagnitude > 0.01f ? look.normalized : Vector3.forward);
            var prop = await GreedChestGame.SpawnChestPropAsync(pos, rot, 0f, ct);
            if (prop == null) return;
            if (this == null) { Destroy(prop); return; }
            prop.transform.SetParent(Props, true);
        }
        catch (OperationCanceledException) { }
    }
}
