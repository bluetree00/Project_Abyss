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
///   · 상자 = 방 전체에 고루(이미 놓은 상자 · 입구에서 가장 먼 자리부터 차례로) · 시간 15 → 24초
///   · 입구 → 상자 → 상자 … 가까운 차례로 걷는 길 위에 장치 하나씩 + 가시 둘:
///     가시 함정(원이 차오르면 솟는다 · 맞으면 최대 체력 4% — 죽지 않는다) · 진흙(서 있는 동안 45% 느려짐) · 바람 문양(밟으면 2.5초 45% 빨라짐)
/// </summary>
public sealed class TreasureVaultChallenge : WorldInteractionChallenge
{
    private const int   ChestCount = 5;
    private const float TimeLimit  = 24f;
    private const ChallengeGrade ChestShare = ChallengeGrade.Silver;   // 상자 하나 = 실버 몫의 절반

    // 상자 흩기
    private const float SpreadSearch    = 16f;    // 방 가운데에서 ±이만큼 2 m 간격으로 자리를 찾는다(방 32 ~ 36 m)
    private const float SpreadStep      = 2f;
    private const float ChestWallClear  = 1.2f;
    private const float ChestEntryClear = 5f;

    // 장치
    private enum Gizmo { Spike, Mud, Wind }
    private static readonly Gizmo[] RouteGizmos = { Gizmo.Spike, Gizmo.Mud, Gizmo.Wind, Gizmo.Spike, Gizmo.Mud };
    private const int   ExtraSpikes     = 2;
    private const float GizmoChestClear = 2.2f;
    private const float SpikeRadius     = 1.3f;
    private const float SpikePeriod     = 2.4f;   // 쉼 → 예고 → 솟음 한 바퀴
    private const float SpikeWarn       = 0.8f;
    private const float MudRadius       = 2.2f;
    private const float MudSlow         = 0.55f;  // 이동 배율(45% 느려짐)
    private const float WindRadius      = 1.2f;
    private const float WindHaste       = 1.45f;
    private const float WindSeconds     = 2.5f;
    private const float WindRecharge    = 3f;
    private static readonly Color SpikeColor = new(0.95f, 0.35f, 0.25f);
    private static readonly Color MudColor   = new(0.45f, 0.32f, 0.2f);
    private static readonly Color WindColor  = new(0.45f, 0.9f, 1f);

    private enum Loot { Gold, Material, Essence }
    private static readonly Loot[] LootSet = { Loot.Gold, Loot.Gold, Loot.Material, Loot.Material, Loot.Essence };

    private sealed class Device
    {
        public Gizmo      Kind;
        public Vector3    Pos;
        public float      Radius, T, Recharge;
        public GameObject Disc;
    }

    private readonly Loot[] _loot = new Loot[ChestCount];
    private readonly List<Device> _devices = new();
    private float       _timeLeft = TimeLimit;
    private int         _opened;
    private bool        _started;
    private int         _shownSecond = -1;
    private Vector3     _center;
    private TextMeshPro _timerLabel;

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

    protected override void PlaceMarkers(Transform player)
    {
        _center = Stage;
        var ct = this.GetCancellationTokenOnDestroy();
        Vector3 entry = player.position;
        var spots = SpreadSpots(entry);
        for (int i = 0; i < spots.Count; i++)
        {
            AddMarker(spots[i], ChallengeFlavor.ChestName(Run), UIPalette.Gold, $"<color={UIPalette.GoldHex}>[F]</color> 개봉", i);
            SpawnChestAsync(spots[i], ct).Forget();   // 글자만 떠 있어 「상자가 안 나온다」로 읽혔다(B2)
        }
        PlaceDevices(entry, spots);
        Debug.Log($"[TreasureVault] 상자 {spots.Count} · 장치 {_devices.Count}");
    }

    protected override void Tick()
    {
        if (IsResolved) return;
        if (!_started)
        {
            // 조작이 풀린 첫 프레임 — 여기서부터 시간이 흐른다(예전엔 화면이 덮인 첫 프레임부터 흘러 약 2초를 잃었다)
            _started = true;
            _timerLabel = MakeText(string.Empty, _center + Vector3.up * 3.2f, 4f, UIPalette.Gold, 12);
            Notice($"{ChallengeFlavor.VaultName(Run)}! <color={UIPalette.GoldHex}>{Mathf.CeilToInt(TimeLimit)}초</color> 안에 흩어진 상자 {ChestCount}개 — " +
                   "<color=#F26A55>가시</color> · <color=#B08A5A>진흙(느려짐)</color> · <color=#73E5FF>바람 문양(빨라짐)</color>");
        }

        if (_timerLabel != null && Camera.main != null)
            _timerLabel.transform.rotation = Camera.main.transform.rotation;

        TickDevices(Time.deltaTime);

        _timeLeft -= Time.deltaTime;
        int sec = Mathf.Max(0, Mathf.CeilToInt(_timeLeft));
        if (_timerLabel != null && sec != _shownSecond)   // 초가 바뀔 때만 글자를 고친다(매 프레임 문자열 할당 금지)
        {
            _shownSecond = sec;
            _timerLabel.text = $"{ChallengeFlavor.VaultName(Run)} · {sec}초";
        }
        if (_timeLeft <= 0f) Resolve();
    }

    protected override void OnActivate(int markerIndex)
    {
        var m = MarkerAt(markerIndex);
        if (m == null || m.Consumed) return;

        ConsumeMarker(markerIndex);
        Managers.Sound?.PlayEvent(SoundEvent.VaultOpen);
        _opened++;
        SetLabel(markerIndex, PopLoot((int)m.Tag, m.Pos), new Color(0.6f, 0.62f, 0.66f));

        if (_opened >= ChestCount) Resolve();
    }

    /// <summary>상자마다 바로 튀었다 — 끝에 더 줄 것은 없다.</summary>
    protected override UniTask GrantRewardAsync(ChallengeGrade grade, Vector3 center, CancellationToken ct) => UniTask.CompletedTask;

    // ── Private Methods ───────────────────────────────────
    private void Resolve()
    {
        if (IsResolved) return;
        if (_timerLabel != null) _timerLabel.gameObject.SetActive(false);
        ClearDevices();

        ChallengeGrade grade = _opened >= 5 ? ChallengeGrade.Platinum
                             : _opened == 4 ? ChallengeGrade.Gold
                             : _opened == 3 ? ChallengeGrade.Silver
                             : _opened == 2 ? ChallengeGrade.Bronze
                             :                ChallengeGrade.Fail;
        Notice($"{ChallengeFlavor.VaultName(Run)} 종료 — {_opened}/{ChestCount} 개봉 · <color=#8fd3ff>{GradeName(grade)}</color>");
        FinishWith(grade, _center);
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

    /// <summary>입구에서 가까운 상자 차례로 걷는 길 — 구간마다 가운데에 장치 하나(가시 · 진흙 · 바람) + 아무 데나 가시 둘.</summary>
    private void PlaceDevices(Vector3 entry, List<Vector3> chests)
    {
        var left = new List<Vector3>(chests);
        Vector3 at = entry;
        for (int g = 0; left.Count > 0; g++)
        {
            int next = 0;
            for (int i = 1; i < left.Count; i++) if (Flat(left[i] - at) < Flat(left[next] - at)) next = i;
            Vector3 to = left[next];
            left.RemoveAt(next);
            TryAddDevice(RouteGizmos[g % RouteGizmos.Length], Vector3.Lerp(at, to, 0.5f), chests, entry);
            at = to;
        }
        for (int i = 0; i < ExtraSpikes; i++)
        {
            Vector3 a = chests[i % chests.Count], b = chests[(i + 2) % chests.Count];
            TryAddDevice(Gizmo.Spike, Vector3.Lerp(a, b, 0.5f), chests, entry);
        }
    }

    private void TryAddDevice(Gizmo kind, Vector3 want, List<Vector3> chests, Vector3 entry)
    {
        if (!MinigameArena.Snap(want, 1.5f, out var p)) return;
        foreach (var c in chests) if (Flat(p - c) < GizmoChestClear) return;
        foreach (var d in _devices) if (Flat(p - d.Pos) < d.Radius + 1.5f) return;
        if (Flat(p - entry) < ChestEntryClear) return;

        var dev = new Device { Kind = kind, Pos = p, T = UnityEngine.Random.value * SpikePeriod };
        switch (kind)
        {
            case Gizmo.Mud:
                dev.Radius = MudRadius;
                dev.Disc = Keep(PatternGuideHelper.Disc(p, MudRadius, MudColor));
                DeviceLabel("진흙 · 느려짐", p, MudColor);
                break;
            case Gizmo.Wind:
                dev.Radius = WindRadius;
                dev.Disc = Keep(PatternGuideHelper.Disc(p, WindRadius, WindColor));
                DeviceLabel("바람 · 빨라짐", p, WindColor);
                break;
            default:
                dev.Radius = SpikeRadius;
                break;
        }
        _devices.Add(dev);
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
                    if (inside) player.ApplySlow(MudSlow, 0.25f);   // 서 있는 동안 계속(나오면 0.25초 뒤 풀림)
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

    /// <summary>가시 — 쉼(옅은 원 없음) → 예고(원이 차오른다 0.8초) → 솟음(원 안이면 4%) → 처음으로.</summary>
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
        if (Flat(playerPos - d.Pos) <= d.Radius + 0.3f) MinigameFx.Hurt(player);
        PatternGuideHelper.SafeDestroy(ref d.Disc);
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
                return $"강화재료 +{n}";
            }
            case Loot.Essence:
            {
                int n = EventRewards.Essence(ChestShare, ch) / 2;
                EventRewards.DropEssence(at, n);
                return $"정수 +{n}";
            }
            default:
            {
                int n = EventRewards.Gold(ChestShare, ch) / 2;
                EventRewards.DropGold(at, n);
                return $"금화 +{n}";
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
