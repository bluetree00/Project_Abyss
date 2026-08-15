using UnityEngine;

/// <summary>
/// 심연의 정수 조각 드랍. <see cref="GoldCoinPickup"/>과 같은 규약(파라볼라 → 자석 → 획득)이되
/// 획득 시 <see cref="GameRunSession.AddEssence"/>로 <b>영구 재화</b>를 적립한다.
///
/// <para><b>왜 드랍으로 만들었나</b> — 정수는 원래 처치/클리어 시 수치만 조용히 올랐다.
/// 그러면 "런을 다시 하는 이유"인 재화가 화면에 한 번도 안 보인다. 골드는 주우러 가는 물건인데
/// 정수는 존재조차 모르는 상태였다. 주우러 가는 행위가 있어야 재화가 인식된다.</para>
///
/// <para><b>아트 교체 지점</b> — <see cref="VisualPrefab"/>에 프리팹을 넣으면 그것으로 스폰하고,
/// 비어 있으면 런타임 프리미티브로 떨어진다(골드와 동일한 임시 비주얼 규약).
/// 정수 아트가 나오면 코드 수정 없이 프리팹만 물리면 된다.</para>
/// </summary>
public class EssenceShardPickup : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float ArcDuration   = 0.5f;
    private const float ArcHeight     = 1.6f;
    private const float PickupDelay   = 0.4f;
    private const float MagnetRange   = 3.5f;    // 골드보다 살짝 넓다 — 영구 재화를 놓치면 손실이 크다
    private const float MagnetSpeed   = 13f;
    private const float PickupRadius  = 0.6f;
    private const float ScatterRadius = 1.3f;
    private const float ShardSize     = 0.3f;
    private const float LifeTime      = 30f;
    private const float SpinSpeed     = 300f;
    private const float BobAmplitude  = 0.12f;
    private const float BobSpeed      = 3f;

    /// <summary>한 번에 흩뿌릴 조각 수 상한. 보스 120정수가 조각 120개가 되면 안 된다.</summary>
    private const int MaxShards = 8;

    // ── Static ───────────────────────────────────────────────
    private static readonly Color ShardColor = new(0.39f, 0.85f, 0.75f);

    /// <summary>정수 조각 비주얼 프리팹. 비어 있으면 런타임 프리미티브를 쓴다.</summary>
    public static GameObject VisualPrefab { get; set; }

    // ── Private Fields ───────────────────────────────────────
    private int       _value;
    private float     _spawnTime;
    private float     _arcTimer;
    private Vector3   _arcStart;
    private Vector3   _arcEnd;
    private Vector3   _restPos;
    private bool      _arcing;
    private bool      _collected;
    private Transform _playerTf;

    // ── Lifecycle ────────────────────────────────────────────
    private void Update()
    {
        if (_arcing)
        {
            _arcTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_arcTimer / ArcDuration);
            Vector3 pos = Vector3.Lerp(_arcStart, _arcEnd, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * ArcHeight;
            transform.position = pos;
            transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.World);

            if (t >= 1f) { _arcing = false; _restPos = _arcEnd; }
            return;
        }

        if (!_collected && Time.time - _spawnTime >= PickupDelay)
        {
            Transform player = ResolvePlayer();
            if (player != null)
            {
                Vector3 delta = player.position - transform.position;
                float dist = delta.magnitude;

                if (dist <= PickupRadius) { Collect(); return; }

                if (dist <= MagnetRange)
                {
                    transform.position += delta.normalized * MagnetSpeed * Time.deltaTime;
                    transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.World);

                    // 끌려간 자리를 새 기준으로 삼는다. 안 하면 자석 범위를 벗어나는 순간
                    // 착지 지점으로 순간이동해 돌아간다.
                    _restPos = transform.position;
                }
                else
                {
                    // 착지 후엔 제자리에서 천천히 돌며 위아래로 뜬다 — 골드와 구분되는 "귀한 것" 표현.
                    transform.Rotate(Vector3.up, SpinSpeed * 0.35f * Time.deltaTime, Space.World);
                    transform.position = _restPos + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobAmplitude);
                }
            }
        }

        if (Time.time - _spawnTime >= LifeTime)
            Destroy(gameObject);
    }

    // ── Public Methods ───────────────────────────────────────

    /// <summary>
    /// origin 주변에 총 <paramref name="totalAmount"/>만큼의 정수를 조각으로 나눠 흩뿌린다.
    /// 조각 수는 <see cref="MaxShards"/>로 묶고, <b>나머지는 첫 조각에 얹어</b> 총량이 정확히 보존된다.
    /// </summary>
    public static void SpawnDrops(Vector3 origin, int totalAmount)
    {
        if (totalAmount <= 0) return;

        int shards    = Mathf.Clamp(Mathf.CeilToInt(totalAmount / 15f), 1, MaxShards);
        int perShard  = totalAmount / shards;
        int remainder = totalAmount - perShard * shards;

        Vector3 startPos = origin + Vector3.up * 0.6f;

        for (int i = 0; i < shards; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r     = Random.Range(0.35f, ScatterRadius);
            Vector3 landing = origin + new Vector3(Mathf.Cos(angle) * r, 0.25f, Mathf.Sin(angle) * r);

            var go = CreateShardVisual();
            go.transform.position = startPos;
            RoomScopedDrop.Mark(go);   // 방 전환 시 정리 대상(부모가 없어 방 파괴로는 안 지워짐)

            var shard = go.AddComponent<EssenceShardPickup>();
            shard.Initialize(startPos, landing, perShard + (i == 0 ? remainder : 0));
        }
    }

    // ── Private Methods ──────────────────────────────────────
    private void Initialize(Vector3 arcStart, Vector3 arcEnd, int value)
    {
        _arcStart  = arcStart;
        _arcEnd    = arcEnd;
        _restPos   = arcEnd;
        _value     = value;
        _spawnTime = Time.time;
        _arcTimer  = 0f;
        _arcing    = true;
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        // 깊이 보상 배율은 AddEssence 안에서 걸린다 — 여기서 또 곱하면 이중 적용이 된다.
        GameRunBootstrapper.Instance?.Run?.AddEssence(_value);
        Destroy(gameObject);
    }

    private Transform ResolvePlayer()
    {
        if (_playerTf != null) return _playerTf;

        var mgr = Managers.Player;
        _playerTf = mgr != null ? mgr.PlayerTransform : null;
        return _playerTf;
    }

    private static GameObject CreateShardVisual()
    {
        if (VisualPrefab != null)
        {
            var art = Instantiate(VisualPrefab);
            art.name = "EssenceShard";
            return art;
        }

        // 골드와 같은 임시 비주얼 규약. 형태(정육면체·회전)와 색(청록)으로 골드와 구분한다.
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = "EssenceShard";
        go.transform.localScale = Vector3.one * ShardSize;
        go.transform.rotation   = Quaternion.Euler(45f, 0f, 45f);

        // 자체 트리거 로직으로 픽업을 판정하므로 Primitive 기본 콜라이더는 제거
        var col = go.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var rend = go.GetComponent<Renderer>();
        // 빌드에서 프리미티브 기본 머티리얼이 핑크로 스트립되는 문제 → URP/Lit 명시 할당.
        RuntimePrimitiveMaterial.Apply(rend, ShardColor);
        if (rend != null)
        {
            var mat = rend.material;   // instance — 공유 머티리얼 오염 방지
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.95f);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0.2f);

            // 자체발광 — 어두운 심연 바닥에서도 눈에 띄어야 주우러 간다.
            if (mat.HasProperty("_EmissionColor"))
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", ShardColor * 2.2f);
            }
        }

        return go;
    }
}
