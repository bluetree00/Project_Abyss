using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 붕괴형 아레나 바닥 — 리치 「천공의 대제단」.
///
/// floorRoot 아래 <c>Tile_i_j</c> 이름의 타일을 정사각 격자로 등록하고, 링·사분면·칸 단위로 무너뜨린다.
/// 설계: 기획/RelicFairy_리치전용아레나_천공의대제단_레벨설계_20260916 §5.
///
/// ■ 규칙
///   · 링 = 격자 중심에서의 체비쇼프 거리(0 = 중앙). permanentRingMax 이하 링은 절대 무너지지 않는다(코어).
///   · 전체 타일의 collapseCapRatio 까지만 무너진다. 넘치는 요청은 버린다 — 패턴은 지형 변화 없이 피해만 준다.
///   · 사분면 붕괴는 코어와 최외곽 링을 뺀 칸이 대상이고, 예산이 모자라면 코어에서 먼 칸부터 고른다
///     (남은 바닥이 코어와 끊기지 않게).
///   · 붕괴 = 흔들림(아직 밟을 수 있음) → 콜라이더를 끄는 순간 낙하 시작 → 비활성.
///   · 일시 파괴(연출·UX 시나리오 §12-4) = 붉은 흔들림 → 가라앉아 사라짐 → <see cref="RestoreBroken"/>(복구 패턴)이 부를 때까지 구멍으로 남는다
///     (시간을 주면 그 뒤 스스로 복구). 영구 붕괴 예산과 별개이고, 동시에 부서져 있을 수 있는 칸 수에 한도가 있다(누적 상한).
///     부서진 동안 영구 붕괴가 오면 복구하지 않고 그대로 사라진다.
///   · 복구는 구멍 기둥 안에 플레이어가 있으면(떨어지는 중) 기다린다 — 떠오르는 발판에 끼지 않게.
///
/// NavMesh는 다시 굽지 않는다 — 리치·플레이어·리치 해골 모두 NavMesh에 의존하지 않는다(대마법사 기획 §4-4 ①).
/// </summary>
public class ArenaTileGrid : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const string TilePrefix = "Tile_";

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId     = Shader.PropertyToID("_Color");

    // ── Static ────────────────────────────────────────────────────
    /// <summary>현재 활성인 붕괴형 아레나. 없으면 null — 보스 패턴은 지형 변화 없이 동작한다.</summary>
    public static ArenaTileGrid Active { get; private set; }

    /// <summary>일시 파괴된 칸이 떠오르기 시작했다(칸 중심 월드 위치 · 떠오르는 시간). 보스가 복구 이펙트·소리를 붙인다.</summary>
    public static event Action<Vector3, float> TileRestoring;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => TileRestoring = null;

    // ── Serialized ────────────────────────────────────────────────
    [Header("격자")]
    [Tooltip("Tile_i_j 타일들의 부모.")]
    [SerializeField] private Transform floorRoot;

    [Tooltip("한 변의 칸 수.")]
    [SerializeField, Min(2)] private int gridSize = 12;

    [Tooltip("타일 한 칸의 크기(m).")]
    [SerializeField, Min(0.1f)] private float cellSize = 5f;

    [Tooltip("이 링 이하는 영구 코어 — 절대 무너지지 않는다.")]
    [SerializeField, Min(0)] private int permanentRingMax = 1;

    [Header("붕괴 예산")]
    [Tooltip("전체 타일 중 무너질 수 있는 최대 비율.")]
    [SerializeField, Range(0f, 1f)] private float collapseCapRatio = 0.5f;

    [Header("붕괴 연출")]
    [Tooltip("호출 시 warnSeconds를 주지 않으면 쓰는 흔들림 시간(초). 이 동안은 아직 밟을 수 있다.")]
    [SerializeField, Min(0f)] private float defaultWarnSeconds = 1f;

    [Tooltip("흔들림 최대 폭(m). 붕괴 직전으로 갈수록 커진다.")]
    [SerializeField, Min(0f)] private float shakeAmplitude = 0.08f;

    [Tooltip("낙하 시간(초). 끝나면 타일을 끈다.")]
    [SerializeField, Min(0.1f)] private float fallDuration = 1.8f;

    [Tooltip("낙하 거리(m). 구름바다 아래로 사라질 만큼.")]
    [SerializeField, Min(1f)] private float fallDistance = 70f;

    [Tooltip("낙하 중 기울어지는 각도(도).")]
    [SerializeField, Min(0f)] private float fallTiltDegrees = 40f;

    [Tooltip("흔들리는 동안 타일에 번지는 색 — 붕괴 예고(색 규약: 빨강 = 즉시 피하라). 흰색이면 색 없음.")]
    [SerializeField] private Color warnTint = new Color(1f, 0.32f, 0.28f, 1f);

    [Header("일시 파괴 · 복구")]
    [Tooltip("동시에 부서져 있을 수 있는 최대 칸 수(누적 상한) — 설 자리를 너무 빼앗지 않게. 넘치는 요청은 버린다.")]
    [SerializeField, Min(0)] private int breakLimit = 24;
    [Tooltip("이 링 이하는 일시 파괴도 안 된다(늘 설 수 있는 한가운데). 영구 코어(permanentRingMax)보다 좁게 — 복구되는 파괴는 코어도 흔든다.")]
    [SerializeField, Min(0)] private int breakSafeRingMax = 0;
    [Tooltip("부서진 칸이 가라앉는 시간(초).")]
    [SerializeField, Min(0.05f)] private float breakSinkSeconds = 0.45f;
    [Tooltip("가라앉는 깊이(m).")]
    [SerializeField, Min(0.5f)] private float breakSinkDistance = 6f;
    [Tooltip("복구 때 떠오르는 시간(초). 끝나는 순간 다시 밟을 수 있다.")]
    [SerializeField, Min(0.1f)] private float restoreRiseSeconds = 0.8f;
    [Tooltip("떠오르는 동안 타일에 번지는 색 — 복구(청록).")]
    [SerializeField] private Color restoreTint = new Color(0.45f, 0.95f, 1f, 1f);

    [Header("낙하")]
    [Tooltip("구멍으로 떨어졌을 때 마지막으로 밟은 바닥보다 이만큼 아래에서 낙사 복구(m). 기본 낙사 판정(−5)은 너무 얕아 떨어지는 감각이 없다(09-19 사용자 제보).")]
    [SerializeField, Min(0f)] private float fallDepth = 28f;

    // ── Private ───────────────────────────────────────────────────
    private readonly Dictionary<Vector2Int, Cell> _cells = new();
    private readonly List<Cell> _buffer = new();
    private MaterialPropertyBlock _mpb;
    private Vector3 _originLocal;   // floorRoot 로컬 기준 (0,0)칸 중심
    private bool    _hasOrigin;
    private int     _cap;
    private int     _spent;         // 붕괴가 확정된 칸 수(진행 중 포함)
    private int     _outerRing;
    private int     _broken;        // 지금 일시 파괴 중인 칸 수
    private Transform _player;       // 복구를 막는 것 — 구멍에 떨어지는 중인 플레이어(태그로 한 번 찾아 둔다)

    private sealed class Cell
    {
        public Vector2Int Index;
        public Transform  Tf;
        public Collider[] Colliders;
        public Renderer[] Renderers;
        public Vector3    HomeLocal;
        public int        Ring;
        public float      CenterDistSqr;
        public bool       Doomed;   // 붕괴 확정 — 되돌리지 않는다
        public bool       Broken;   // 일시 파괴 중 — 복구된다(그사이 Doomed가 되면 복구하지 않는다)
        public float      RestoreAt = -1f;   // 복구 시작 시각(-1 = 아직 요청 없음)
        public float      TopLocalY;         // 발판 윗면 높이(floorRoot 로컬)
    }

    // ── Properties ────────────────────────────────────────────────
    public int TileCount       => _cells.Count;
    public int CollapsedCount  => _spent;
    public int RemainingBudget => Mathf.Max(0, _cap - _spent);
    /// <summary>지금 일시 파괴 중인 칸 수.</summary>
    public int BrokenCount     => _broken;
    /// <summary>최외곽 링 번호 — 2페이즈 진입 붕괴 대상.</summary>
    public int OuterRing       => _outerRing;
    /// <summary>한 변의 칸 수.</summary>
    public int GridSize        => gridSize;
    /// <summary>칸 한 변(m).</summary>
    public float CellSize      => cellSize;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        BuildIndex();
    }

    private void OnEnable()
    {
        Active = this;
        FallRecoveryController.SetFallDepthOverride(this, fallDepth);
    }

    private void OnDisable()
    {
        if (Active == this) Active = null;
        FallRecoveryController.ClearFallDepthOverride(this);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 링 하나를 통째로 무너뜨린다. 코어 링은 무시. 실제로 무너뜨린 칸 수를 돌려준다.
    /// <paramref name="ignoreCap"/>는 연출 전용(리치 붕괴 컷신·최후의 원) — 플레이어를 코어에 둔 채로만 쓴다.
    /// </summary>
    public int CollapseRing(int ring, float warnSeconds = -1f, bool ignoreCap = false)
    {
        if (ring <= permanentRingMax) return 0;

        _buffer.Clear();
        foreach (var cell in _cells.Values)
            if (cell.Ring == ring && !cell.Doomed) _buffer.Add(cell);

        return Commit(_buffer, warnSeconds, ignoreCap);
    }

    /// <summary>
    /// 사분면 하나(코어·최외곽 링 제외)를 무너뜨린다.
    /// 0 = +x+z(북동), 1 = −x+z(북서), 2 = −x−z(남서), 3 = +x−z(남동).
    /// </summary>
    public int CollapseQuadrant(int quadrant, float warnSeconds = -1f)
    {
        float c = (gridSize - 1) * 0.5f;
        bool wantEast  = quadrant == 0 || quadrant == 3;
        bool wantNorth = quadrant == 0 || quadrant == 1;

        _buffer.Clear();
        foreach (var cell in _cells.Values)
        {
            if (cell.Doomed || cell.Ring <= permanentRingMax || cell.Ring >= _outerRing) continue;
            bool east  = cell.Index.x > c;
            bool north = cell.Index.y > c;
            if (east == wantEast && north == wantNorth) _buffer.Add(cell);
        }

        return Commit(_buffer, warnSeconds);
    }

    /// <summary>
    /// 지정한 칸들을 무너뜨린다. 코어·없는 칸·이미 확정된 칸은 건너뛴다.
    /// <paramref name="ignoreCap"/>는 연출 전용(2페이즈 무대 변화) — 예산을 쓰지 않은 것처럼 두지 않고 그대로 센다.
    /// </summary>
    public int CollapseCells(IReadOnlyList<Vector2Int> cells, float warnSeconds = -1f, bool ignoreCap = false)
    {
        _buffer.Clear();
        for (int i = 0; i < cells.Count; i++)
        {
            if (!_cells.TryGetValue(cells[i], out var cell)) continue;
            if (cell.Doomed || cell.Ring <= permanentRingMax) continue;
            _buffer.Add(cell);
        }

        return Commit(_buffer, warnSeconds, ignoreCap);
    }

    /// <summary>네 귀퉁이 각 <paramref name="size"/>×<paramref name="size"/>칸(2페이즈 무대 변화 — 넓이는 대부분 남는다).</summary>
    public void CornerCells(int size, List<Vector2Int> result)
    {
        result.Clear();
        int hi = gridSize - size;
        foreach (var cell in _cells.Values)
        {
            var  i  = cell.Index;
            bool xs = i.x < size || i.x >= hi;
            bool ys = i.y < size || i.y >= hi;
            if (xs && ys && !cell.Doomed) result.Add(i);
        }
    }

    /// <summary>
    /// 격자 좌표(칸 번호, 소수 가능)의 바닥 월드 위치 — 칸이 없어도(깎인 귀퉁이 · 무너진 칸) 격자 기하로 구한다.
    /// 원점을 아직 모르면 false.
    /// </summary>
    public bool TryGetGridPoint(float ix, float iy, out Vector3 world)
    {
        world = default;
        if (!_hasOrigin) return false;
        world = floorRoot.TransformPoint(_originLocal + new Vector3(ix * cellSize, 0f, iy * cellSize));
        return true;
    }

    /// <summary>칸 바닥 중심의 월드 위치. 없는 칸이면 false.</summary>
    public bool TryGetCellCenter(Vector2Int index, out Vector3 center)
    {
        center = default;
        if (!_hasOrigin || !_cells.TryGetValue(index, out var cell)) return false;
        center = CellWorldCenter(cell);
        return true;
    }

    /// <summary>
    /// 칸들을 부순다 — 붉은 흔들림(<paramref name="warnSeconds"/>) → 가라앉아 사라짐 → 구멍으로 남는다.
    /// <paramref name="downSeconds"/>가 0 이상이면 그 뒤 스스로 복구, 음수면 <see cref="RestoreBroken"/>까지 남는다.
    /// 한가운데(breakSafeRingMax 이하)·이미 부서졌거나 붕괴 확정된 칸은 건너뛰고, 누적 한도(breakLimit)를 넘는 칸은 버린다. 실제로 부순 칸 수.
    /// </summary>
    public int BreakCells(IReadOnlyList<Vector2Int> cells, float warnSeconds, float downSeconds = -1f)
    {
        int count = 0;
        var ct    = destroyCancellationToken;
        for (int i = 0; i < cells.Count && _broken < breakLimit; i++)
        {
            if (!_cells.TryGetValue(cells[i], out var cell)) continue;
            if (cell.Doomed || cell.Broken || cell.Ring <= breakSafeRingMax) continue;
            cell.Broken    = true;
            cell.RestoreAt = -1f;
            _broken++;
            BreakAsync(cell, Mathf.Max(0f, warnSeconds), downSeconds, ct).Forget();
            count++;
        }
        return count;
    }

    /// <summary>
    /// 부서진 칸을 모두 복구한다 — <paramref name="from"/>에서 가까운 칸부터 <paramref name="stagger"/>초 간격으로 떠오른다.
    /// 복구를 시작시킨 칸 수(이미 복구 요청된 칸·영구 붕괴가 확정된 칸은 제외).
    /// </summary>
    public int RestoreBroken(Vector3 from, float stagger)
    {
        _buffer.Clear();
        foreach (var cell in _cells.Values)
            if (cell.Broken && !cell.Doomed && cell.RestoreAt < 0f) _buffer.Add(cell);
        if (_buffer.Count == 0) return 0;

        Vector3 local = floorRoot.InverseTransformPoint(from) - _originLocal;
        float   fx    = local.x / cellSize;
        float   fz    = local.z / cellSize;
        _buffer.Sort((a, b) => Dist2(a).CompareTo(Dist2(b)));
        float now = Time.time;
        for (int i = 0; i < _buffer.Count; i++)
            _buffer[i].RestoreAt = now + i * Mathf.Max(0f, stagger);
        return _buffer.Count;

        float Dist2(Cell c)
        {
            float dx = c.Index.x - fx;
            float dz = c.Index.y - fz;
            return dx * dx + dz * dz;
        }
    }

    /// <summary>
    /// <paramref name="near"/>에서 가장 가까운, 지금 구멍인 칸(부서졌고 아직 복구가 시작되지 않은 칸)의 바닥 중심. 없으면 false.
    /// 테스트 도구가 플레이어를 구멍에 떨어뜨릴 때 쓴다.
    /// </summary>
    public bool TryGetBrokenCellCenter(Vector3 near, out Vector3 center)
    {
        center = default;
        if (!_hasOrigin) return false;
        float best = float.MaxValue;
        foreach (var cell in _cells.Values)
        {
            if (!cell.Broken || cell.Doomed || cell.RestoreAt >= 0f) continue;
            Vector3 c = CellWorldCenter(cell);
            float   d = (c - near).sqrMagnitude;
            if (d >= best) continue;
            best   = d;
            center = c;
        }
        return best < float.MaxValue;
    }

    /// <summary>수평 거리로 <paramref name="worldPos"/>에서 칸 중심까지 <paramref name="radius"/> 이내인 칸들을 담는다.</summary>
    public void CellsInRadius(Vector3 worldPos, float radius, List<Vector2Int> result)
    {
        result.Clear();
        if (!_hasOrigin) return;
        Vector3 local = floorRoot.InverseTransformPoint(worldPos) - _originLocal;
        float   r     = radius / cellSize;
        foreach (var cell in _cells.Values)
        {
            float dx = cell.Index.x - local.x / cellSize;
            float dz = cell.Index.y - local.z / cellSize;
            if (dx * dx + dz * dz <= r * r) result.Add(cell.Index);
        }
    }

    /// <summary>
    /// 무너지지 않는 흔들림 — <paramref name="worldPos"/> 수평 반경 <paramref name="radius"/> 안 최외곽 칸을
    /// <paramref name="seconds"/> 동안 떨게 한 뒤 제자리로 돌린다(해골이 난간을 넘어오는 예고 등). 흔들린 칸 수.
    /// </summary>
    public int Tremble(Vector3 worldPos, float radius, float seconds)
    {
        if (!_hasOrigin || seconds <= 0f) return 0;
        Vector3 local = floorRoot.InverseTransformPoint(worldPos) - _originLocal;
        float   r2    = (radius / cellSize) * (radius / cellSize);
        int     count = 0;
        var     ct    = destroyCancellationToken;
        foreach (var cell in _cells.Values)
        {
            if (cell.Doomed || cell.Broken || cell.Ring < _outerRing) continue;
            float dx = cell.Index.x - local.x / cellSize;
            float dz = cell.Index.y - local.z / cellSize;
            if (dx * dx + dz * dz > r2) continue;
            TrembleAsync(cell, seconds, ct).Forget();
            count++;
        }
        return count;
    }

    /// <summary>월드 좌표의 수평 위치가 속한 칸. 높이는 무시한다(부유하는 보스도 판정 가능).</summary>
    public bool TryGetCell(Vector3 worldPos, out Vector2Int cell)
    {
        cell = default;
        if (!_hasOrigin) return false;

        Vector3 local = floorRoot.InverseTransformPoint(worldPos) - _originLocal;
        var idx = new Vector2Int(Mathf.RoundToInt(local.x / cellSize), Mathf.RoundToInt(local.z / cellSize));
        if (!_cells.ContainsKey(idx)) return false;

        cell = idx;
        return true;
    }

    /// <summary>격자 중심의 월드 위치(바닥 높이).</summary>
    public bool TryGetWorldCenter(out Vector3 center)
    {
        center = default;
        if (!_hasOrigin) return false;

        float c = (gridSize - 1) * 0.5f;
        center = floorRoot.TransformPoint(_originLocal + new Vector3(c * cellSize, 0f, c * cellSize));
        return true;
    }

    /// <summary>중심에서 링 한 줄의 칸 중심까지 거리(m). 짝수 격자는 중심이 칸 경계라 반 칸 밀린다.</summary>
    public float RingCenterDistance(int ring) => (ring + (gridSize % 2 == 0 ? 0.5f : 0f)) * cellSize;

    // ── Private Methods ───────────────────────────────────────────
    private void BuildIndex()
    {
        _cells.Clear();
        _outerRing = 0;
        if (floorRoot == null)
        {
            Debug.LogError("[ArenaTileGrid] floorRoot 미할당 — 붕괴가 동작하지 않는다.", this);
            return;
        }

        float c = (gridSize - 1) * 0.5f;
        foreach (Transform child in floorRoot)
        {
            if (!TryParseIndex(child.name, out var idx)) continue;

            float dx = idx.x - c;
            float dz = idx.y - c;
            var cell = new Cell
            {
                Index         = idx,
                Tf            = child,
                Colliders     = child.GetComponentsInChildren<Collider>(true),
                Renderers     = child.GetComponentsInChildren<Renderer>(true),
                HomeLocal     = child.localPosition,
                Ring          = (int)Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz)),
                CenterDistSqr = dx * dx + dz * dz,
            };
            _cells[idx] = cell;
            _outerRing  = Mathf.Max(_outerRing, cell.Ring);
            if (cell.Colliders.Length > 0)
                cell.TopLocalY = floorRoot.InverseTransformPoint(cell.Colliders[0].bounds.max).y;

            // 원점은 콜라이더 중심으로 잡는다 — 타일 원본의 피벗이 모서리라 localPosition으로는 칸 중심을 알 수 없다.
            if (!_hasOrigin && cell.Colliders.Length > 0)
            {
                Vector3 center = floorRoot.InverseTransformPoint(cell.Colliders[0].bounds.center);
                _originLocal = new Vector3(center.x - idx.x * cellSize, 0f, center.z - idx.y * cellSize);
                _hasOrigin   = true;
            }
        }

        _cap = Mathf.FloorToInt(_cells.Count * collapseCapRatio);
        Debug.Log($"[ArenaTileGrid] 타일 {_cells.Count}장 · 최외곽 링 {_outerRing} · 붕괴 상한 {_cap}", this);
    }

    private int Commit(List<Cell> targets, float warnSeconds, bool ignoreCap = false)
    {
        int allowed = ignoreCap ? targets.Count : Mathf.Min(targets.Count, RemainingBudget);
        if (allowed <= 0) return 0;

        // 예산이 모자라면 코어에서 먼 칸부터 — 남는 바닥이 코어와 이어지게 한다.
        if (allowed < targets.Count)
            targets.Sort((a, b) => b.CenterDistSqr.CompareTo(a.CenterDistSqr));

        float warn = warnSeconds >= 0f ? warnSeconds : defaultWarnSeconds;
        var ct = destroyCancellationToken;
        for (int i = 0; i < allowed; i++)
        {
            var cell = targets[i];
            cell.Doomed = true;
            // 일시 파괴 중인 칸은 이미 발판이 없다 — 복구하지 않게 표시만 하고 파괴 쪽이 마무리한다.
            if (!cell.Broken) CollapseAsync(cell, warn, ct).Forget();
        }

        _spent += allowed;
        return allowed;
    }

    private async UniTaskVoid CollapseAsync(Cell cell, float warn, CancellationToken ct)
    {
        var tf = cell.Tf;
        try
        {
            // 흔들림 — 아직 발판이다. 붕괴 직전으로 갈수록 세지고 붉게 물든다.
            float t = 0f;
            while (t < warn)
            {
                t += Time.deltaTime;
                float k = t / warn;
                tf.localPosition = cell.HomeLocal + UnityEngine.Random.insideUnitSphere * (shakeAmplitude * k);
                TintCell(cell, Color.Lerp(Color.white, warnTint, Mathf.SmoothStep(0f, 1f, k)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 발판 제거와 동시에 낙하 — 콜라이더가 남아 있는 동안 떨어지면 플레이어가 함께 끌려 내려간다.
            for (int i = 0; i < cell.Colliders.Length; i++)
                if (cell.Colliders[i] != null) cell.Colliders[i].enabled = false;

            Quaternion startRot = tf.localRotation;
            Vector3    tiltAxis = new Vector3(UnityEngine.Random.Range(-1f, 1f), 0f, UnityEngine.Random.Range(-1f, 1f));
            if (tiltAxis.sqrMagnitude < 0.01f) tiltAxis = Vector3.right;
            tiltAxis.Normalize();

            t = 0f;
            while (t < fallDuration)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / fallDuration);
                tf.localPosition = cell.HomeLocal + Vector3.down * (fallDistance * k * k);   // 가속 낙하
                tf.localRotation = startRot * Quaternion.AngleAxis(fallTiltDegrees * k, tiltAxis);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            tf.gameObject.SetActive(false);
        }
        catch (OperationCanceledException)
        {
            // 방이 파괴됨 — 타일도 함께 사라지므로 정리할 것이 없다.
        }
    }

    private async UniTaskVoid BreakAsync(Cell cell, float warn, float down, CancellationToken ct)
    {
        var tf = cell.Tf;
        try
        {
            // 붉은 흔들림 — 아직 발판이다.
            float t = 0f;
            while (t < warn && !cell.Doomed)
            {
                t += Time.deltaTime;
                float k = t / Mathf.Max(0.01f, warn);
                tf.localPosition = cell.HomeLocal + UnityEngine.Random.insideUnitSphere * (shakeAmplitude * k);
                TintCell(cell, Color.Lerp(Color.white, warnTint, Mathf.SmoothStep(0f, 1f, k)));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }

            // 발판 제거와 동시에 가라앉는다.
            SetColliders(cell, false);
            t = 0f;
            while (t < breakSinkSeconds)
            {
                t += Time.deltaTime;
                float k = Mathf.Clamp01(t / breakSinkSeconds);
                tf.localPosition = cell.HomeLocal + Vector3.down * (breakSinkDistance * k * k);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            SetRenderers(cell, false);

            // 구멍으로 남는다 — 시간을 줬으면 그 뒤, 아니면 복구 패턴(RestoreBroken)이 부를 때까지.
            if (down >= 0f) cell.RestoreAt = Time.time + down;
            while (!cell.Doomed && (cell.RestoreAt < 0f || Time.time < cell.RestoreAt))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (cell.Doomed)
            {
                tf.gameObject.SetActive(false);   // 부서진 사이 영구 붕괴가 왔다 — 그대로 사라진다
                return;
            }

            // 구멍에 떨어지는 중인 사람이 있으면 기다린다 — 떠오르는 발판에 끼지 않게(낙사 복구가 데려간다).
            while (!cell.Doomed && ColumnOccupied(cell))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            if (cell.Doomed)
            {
                tf.gameObject.SetActive(false);
                return;
            }

            // 복구 — 청록 빛을 띠고 떠올라, 끝나는 순간 다시 밟을 수 있다.
            TileRestoring?.Invoke(CellWorldCenter(cell), restoreRiseSeconds);
            SetRenderers(cell, true);
            t = 0f;
            while (t < restoreRiseSeconds)
            {
                t += Time.deltaTime;
                float k    = Mathf.Clamp01(t / restoreRiseSeconds);
                float ease = 1f - (1f - k) * (1f - k);
                tf.localPosition = cell.HomeLocal + Vector3.down * (breakSinkDistance * (1f - ease));
                TintCell(cell, Color.Lerp(restoreTint, Color.white, k));
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            tf.localPosition = cell.HomeLocal;
            TintCell(cell, Color.white);
            // 떠오르는 사이 발판 없는 자리로 걸어 들어와 떨어지는 중이면, 빠져나갈 때까지 발판을 켜지 않는다.
            while (ColumnOccupied(cell))
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            SetColliders(cell, true);
        }
        catch (OperationCanceledException)
        {
            // 방이 파괴됨 — 타일도 함께 사라진다.
        }
        finally
        {
            cell.Broken    = false;
            cell.RestoreAt = -1f;
            _broken        = Mathf.Max(0, _broken - 1);
        }
    }

    /// <summary>칸 가운데 바닥의 월드 위치.</summary>
    private Vector3 CellWorldCenter(Cell cell)
        => floorRoot.TransformPoint(_originLocal + new Vector3(cell.Index.x * cellSize, 0f, cell.Index.y * cellSize));

    /// <summary>
    /// 칸 구멍 기둥(발판 윗면 0.2 m 아래 ~ 40 m 아래) 안에 플레이어가 있는가 — 떨어지는 중이거나 구멍 안에 서 있다.
    /// 옆 칸 위에 서 있는 발은 윗면 높이라 걸리지 않는다. 플레이어는 레이어가 Default라 물리 질의 대신 위치로 본다.
    /// </summary>
    private bool ColumnOccupied(Cell cell)
    {
        const float Depth  = 40f;
        const float Margin = 0.4f;   // 캡슐 반경만큼 — 구멍 가장자리에 걸친 몸도 기둥 안으로 본다
        if (_player == null)
        {
            var go = GameObject.FindWithTag("Player");
            if (go == null) return false;
            _player = go.transform;
        }

        Vector3 local = floorRoot.InverseTransformPoint(_player.position) - _originLocal;
        float   half  = cellSize * 0.5f + Margin;
        if (Mathf.Abs(local.x - cell.Index.x * cellSize) > half) return false;
        if (Mathf.Abs(local.z - cell.Index.y * cellSize) > half) return false;
        float top = cell.TopLocalY - (_originLocal.y);
        return local.y < top - 0.2f && local.y > top - Depth;
    }

    private static void SetColliders(Cell cell, bool on)
    {
        for (int i = 0; i < cell.Colliders.Length; i++)
            if (cell.Colliders[i] != null) cell.Colliders[i].enabled = on;
    }

    private static void SetRenderers(Cell cell, bool on)
    {
        if (cell.Renderers == null) return;
        for (int i = 0; i < cell.Renderers.Length; i++)
            if (cell.Renderers[i] != null) cell.Renderers[i].enabled = on;
    }

    private async UniTaskVoid TrembleAsync(Cell cell, float seconds, CancellationToken ct)
    {
        var tf = cell.Tf;
        try
        {
            float t = 0f;
            while (t < seconds && !cell.Doomed && !cell.Broken)
            {
                t += Time.deltaTime;
                tf.localPosition = cell.HomeLocal + UnityEngine.Random.insideUnitSphere * (shakeAmplitude * 0.6f);
                await UniTask.Yield(PlayerLoopTiming.Update, ct);
            }
            if (!cell.Doomed && !cell.Broken) tf.localPosition = cell.HomeLocal;   // 붕괴·파괴가 이어받았으면 건드리지 않는다
        }
        catch (OperationCanceledException) { }
    }

    private void TintCell(Cell cell, Color color)
    {
        if (cell.Renderers == null || cell.Renderers.Length == 0) return;
        _mpb ??= new MaterialPropertyBlock();
        for (int i = 0; i < cell.Renderers.Length; i++)
        {
            var r = cell.Renderers[i];
            if (r == null) continue;
            r.GetPropertyBlock(_mpb);
            _mpb.SetColor(BaseColorId, color);
            _mpb.SetColor(ColorId, color);
            r.SetPropertyBlock(_mpb);
        }
    }

    private static bool TryParseIndex(string name, out Vector2Int idx)
    {
        idx = default;
        if (!name.StartsWith(TilePrefix, StringComparison.Ordinal)) return false;

        int sep = name.IndexOf('_', TilePrefix.Length);
        if (sep < 0) return false;

        if (!int.TryParse(name.AsSpan(TilePrefix.Length, sep - TilePrefix.Length), out int i)) return false;
        if (!int.TryParse(name.AsSpan(sep + 1), out int j)) return false;

        idx = new Vector2Int(i, j);
        return true;
    }
}
