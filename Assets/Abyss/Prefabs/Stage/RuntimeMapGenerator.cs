using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public sealed class RuntimeMapGeneratorSolo : MonoBehaviour
{
    [Header("Block Prefab")]
    [SerializeField] private GameObject blockPrefab;

    [Header("Root")]
    [SerializeField] private Transform blocksRoot;

    [Header("Grid Size (X,Z plane)")]
    [Min(1)][SerializeField] private int width = 10;
    [Min(1)][SerializeField] private int height = 10;
    [SerializeField] private Vector2Int originCell = Vector2Int.zero;
    [SerializeField] private float baseY = 0f;

    [Header("Cell Size")]
    [SerializeField] private bool autoCellSizeFromBoxCollider = true;
    [SerializeField] private Vector2 manualCellSizeXZ = Vector2.one;

    [Header("Scatter Spawn (3D)")]
    [SerializeField] private int seed = 1234;

    [Tooltip("흩뿌릴 중심이 그리드 중심인지 여부")]
    [SerializeField] private bool scatterAroundGridCenter = true;

    [Tooltip("흩뿌릴 범위(X, Y, Z). 각 축 방향으로 -range ~ +range")]
    [SerializeField] private Vector3 scatterRange = new Vector3(10f, 4f, 10f);

    [Tooltip("Y가 바닥 아래로 내려가지 않게 clamp (baseY + minY ~ baseY + maxY)")]
    [SerializeField] private Vector2 scatterYClamp = new Vector2(0f, 6f);

    [Header("Return Move (Rough)")]
    [SerializeField] private bool animateReturn = true;
    [Min(0.01f)][SerializeField] private float returnDuration = 1.2f;

    [Tooltip("이동 중 흔들림(노이즈) 강도. 0이면 흔들림 없음")]
    [SerializeField] private float travelJitter = 0.05f;

    [Tooltip("노이즈 변화 속도(낮을수록 느리게 흔들림)")]
    [SerializeField] private float jitterTimeScale = 1.0f;

    [Tooltip("러프한 이동 경로를 위한 커브 휘어짐(0이면 거의 직선)")]
    [SerializeField] private float curveBend = 1.0f;

    [Header("Options")]
    [SerializeField] private bool clearBeforeGenerate = true;
    [SerializeField] private bool logDebug = true;

    private Vector2 _cellSizeXZ;

    private sealed class BlockMemory
    {
        public Transform tr;
        public Vector2Int cell;
        public Vector3 targetWorld;
        public Vector3 scatterWorld;
    }

    private readonly List<BlockMemory> _blocks = new();

    private void Awake()
    {
        if (blocksRoot == null) blocksRoot = transform;
    }

    private void Start()
    {
        GenerateRememberScatterReturn();
    }

    [ContextMenu("Generate -> Remember -> Scatter(3D) -> Return")]
    public void GenerateRememberScatterReturn()
    {
        if (blockPrefab == null)
        {
            Debug.LogError("[RuntimeMapGeneratorSolo] blockPrefab is null");
            return;
        }

        if (blocksRoot == null) blocksRoot = transform;

        if (clearBeforeGenerate)
            Clear();

        // 1) 셀 크기 결정
        _cellSizeXZ = autoCellSizeFromBoxCollider
            ? GetCellSizeXZFromPrefabBoxCollider(blockPrefab)
            : manualCellSizeXZ;

        if (_cellSizeXZ.x <= 0f || _cellSizeXZ.y <= 0f)
        {
            Debug.LogError($"[RuntimeMapGeneratorSolo] invalid cell size: {_cellSizeXZ}");
            return;
        }

        // 랜덤 고정
        var prevRandom = Random.state;
        Random.InitState(seed);

        // 2) 그리드 정배치 + 타겟 기억
        _blocks.Clear();

        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                var cell = new Vector2Int(originCell.x + x, originCell.y + z);
                Vector3 target = CellToWorld(cell, baseY);

                var go = Instantiate(blockPrefab, blocksRoot);
                go.transform.position = target;
                go.transform.rotation = Quaternion.identity;
                go.name = $"Block_{x}_{z}";

                _blocks.Add(new BlockMemory
                {
                    tr = go.transform,
                    cell = cell,
                    targetWorld = target,
                    scatterWorld = target
                });
            }
        }

        // 3) 3D로 흩뿌리기
        Vector3 scatterCenter = scatterAroundGridCenter ? GetGridCenterWorld(baseY) : new Vector3(0f, baseY, 0f);

        for (int i = 0; i < _blocks.Count; i++)
        {
            Vector3 scattered = scatterCenter + RandomInBox(scatterRange);

            // Y clamp: baseY + [min,max]
            float yMin = baseY + scatterYClamp.x;
            float yMax = baseY + scatterYClamp.y;
            scattered.y = Mathf.Clamp(scattered.y, yMin, yMax);

            _blocks[i].scatterWorld = scattered;
            _blocks[i].tr.position = scattered;
        }

        // 4) 복귀(러프)
        if (animateReturn)
        {
            StopAllCoroutines();
            StartCoroutine(CoReturnAll());
        }
        else
        {
            for (int i = 0; i < _blocks.Count; i++)
                _blocks[i].tr.position = _blocks[i].targetWorld; // 정확 복귀
        }

        Random.state = prevRandom;

        if (logDebug)
            Debug.Log($"[RuntimeMapGeneratorSolo] grid={width}x{height}, cellSizeXZ={_cellSizeXZ}, scatterRange={scatterRange}, yClamp={scatterYClamp}");
    }

    private IEnumerator CoReturnAll()
    {
        float t = 0f;
        int n = _blocks.Count;

        var starts = new Vector3[n];
        var targets = new Vector3[n];
        var controlA = new Vector3[n];
        var controlB = new Vector3[n];

        for (int i = 0; i < n; i++)
        {
            starts[i] = _blocks[i].tr.position;
            targets[i] = _blocks[i].targetWorld; // ✅ 정확 복귀

            Vector3 mid = (starts[i] + targets[i]) * 0.5f;

            // 커브는 XZ 중심으로 주되, Y도 살짝 섞고 싶으면 y값도 랜덤으로 추가 가능
            Vector3 bend = RandomInsideRadiusXZ(curveBend);
            bend.y = Random.Range(-curveBend * 0.25f, curveBend * 0.25f);

            controlA[i] = mid + bend;
            controlB[i] = mid - bend * 0.6f;
        }

        while (t < returnDuration)
        {
            t += Time.deltaTime;
            float k = Mathf.Clamp01(t / returnDuration);
            float eased = k * k * (3f - 2f * k);

            float noiseT = Time.time * jitterTimeScale;

            for (int i = 0; i < n; i++)
            {
                Vector3 p = Bezier4(starts[i], controlA[i], controlB[i], targets[i], eased);

                if (travelJitter > 0f)
                {
                    float jx = (Mathf.PerlinNoise(i * 0.37f, noiseT) - 0.5f) * 2f;
                    float jy = (Mathf.PerlinNoise(i * 0.51f, noiseT) - 0.5f) * 2f;
                    float jz = (Mathf.PerlinNoise(i * 0.73f, noiseT) - 0.5f) * 2f;

                    p.x += jx * travelJitter;
                    p.y += jy * (travelJitter * 0.5f); // Y 흔들림은 약하게
                    p.z += jz * travelJitter;
                }

                _blocks[i].tr.position = p;
            }

            yield return null;
        }

        // 마지막 고정(정확 매칭)
        for (int i = 0; i < n; i++)
            _blocks[i].tr.position = targets[i];
    }

    // ---- helpers ----
    private Vector3 CellToWorld(Vector2Int cell, float y)
        => new Vector3(cell.x * _cellSizeXZ.x, y, cell.y * _cellSizeXZ.y);

    private Vector3 GetGridCenterWorld(float y)
    {
        float cx = (originCell.x + (width - 1) * 0.5f) * _cellSizeXZ.x;
        float cz = (originCell.y + (height - 1) * 0.5f) * _cellSizeXZ.y;
        return new Vector3(cx, y, cz);
    }

    private static Vector3 RandomInBox(Vector3 halfExtents)
    {
        // 각 축 -range ~ +range
        return new Vector3(
            Random.Range(-halfExtents.x, halfExtents.x),
            Random.Range(-halfExtents.y, halfExtents.y),
            Random.Range(-halfExtents.z, halfExtents.z)
        );
    }

    private static Vector3 RandomInsideRadiusXZ(float r)
    {
        Vector2 v = Random.insideUnitCircle * r;
        return new Vector3(v.x, 0f, v.y);
    }

    private static Vector3 Bezier4(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
    {
        float u = 1f - t;
        return
            (u * u * u) * p0 +
            (3f * u * u * t) * p1 +
            (3f * u * t * t) * p2 +
            (t * t * t) * p3;
    }

    [ContextMenu("Clear")]
    public void Clear()
    {
        if (blocksRoot == null) blocksRoot = transform;

        for (int i = blocksRoot.childCount - 1; i >= 0; i--)
            DestroySafe(blocksRoot.GetChild(i).gameObject);

        _blocks.Clear();
    }

    // ✅ 셀 크기 계산(안정): BoxCollider 기준
    private static Vector2 GetCellSizeXZFromPrefabBoxCollider(GameObject prefab)
    {
        var temp = Instantiate(prefab);
        try
        {
            var box = temp.GetComponentInChildren<BoxCollider>(true);
            if (box == null)
            {
                Debug.LogWarning("[RuntimeMapGeneratorSolo] No BoxCollider found. Fallback to (1,1).");
                return Vector2.one;
            }

            box.enabled = true;

            Vector3 s = box.transform.lossyScale;
            float sx = Mathf.Abs(box.size.x * s.x);
            float sz = Mathf.Abs(box.size.z * s.z);

            sx = Mathf.Max(0.0001f, sx);
            sz = Mathf.Max(0.0001f, sz);
            return new Vector2(sx, sz);
        }
        finally
        {
            DestroySafe(temp);
        }
    }

    private static void DestroySafe(Object obj)
    {
#if UNITY_EDITOR
        if (!Application.isPlaying) DestroyImmediate(obj);
        else Destroy(obj);
#else
        Destroy(obj);
#endif
    }
}
