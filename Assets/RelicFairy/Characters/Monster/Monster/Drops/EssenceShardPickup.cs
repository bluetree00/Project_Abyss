using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// 심연의 정수 조각 드랍. <see cref="GoldCoinPickup"/>과 같은 규약(파라볼라 → 자석 → 획득)이되
/// 획득 시 <see cref="GameRunSession.AddEssence"/>로 <b>영구 재화</b>를 적립한다.
///
/// <para><b>왜 드랍으로 만들었나</b> — 정수는 원래 처치/클리어 시 수치만 조용히 올랐다.
/// 그러면 "런을 다시 하는 이유"인 재화가 화면에 한 번도 안 보인다. 골드는 주우러 가는 물건인데
/// 정수는 존재조차 모르는 상태였다. 주우러 가는 행위가 있어야 재화가 인식된다.</para>
///
/// <para><b>모양</b> — 재화 오브젝트 목록(<see cref="CurrencyVisualSetSO"/>의 정수 칸, 10-12)에서 꺼낸다.
/// 목록이 아직 없거나 칸이 비면 예전 임시 비주얼(청록 정육면체)로 떨어진다. 나타나기 · 사라지기는 디졸브(<see cref="CurrencyVisuals"/>).</para>
/// </summary>
public class EssenceShardPickup : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float ArcDuration   = 0.5f;
    private const float ArcHeight     = 1.6f;
    private const float PickupDelay   = 0.4f;
    private const float MagnetRange   = 5f;      // 골드보다 살짝 넓다 — 영구 재화를 놓치면 손실이 크다 · 10-29 「기본 획득 거리를 좀 더」 3.5 → 5
    private const float MagnetSpeed   = 13f;
    private const float PickupRadius  = 0.6f;
    private const float ScatterRadius = 1.3f;
    private const float ShardSize     = 0.3f;    // 임시 정육면체 한 변
    private const float RestHeight    = 0.32f;   // 내려앉은 자리(가운데) 높이 — 캡슐(0.36 m)이 출렁여도 바닥에 묻히지 않게
    private const float RestTilt      = 12f;     // 캡슐을 살짝 기울여 둔다 — 곧게 서 있으면 도는 것이 안 보인다
    private const float LifeTime      = 30f;
    private const float SpinSpeed     = 300f;
    private const float BobAmplitude  = 0.12f;
    private const float BobSpeed      = 3f;
    private const float AppearSeconds = 0.35f;   // 날아가는 동안(0.5초) 끝난다. 줍기 대기(0.4초)보다 짧게
    private const float CollectVanishSeconds = 0.2f;
    private const float ExpireVanishSeconds  = 0.6f;

    /// <summary>한 번에 흩뿌릴 조각 수 상한. 보스 120정수가 조각 120개가 되면 안 된다.</summary>
    private const int MaxShards = 8;

    // ── Static ───────────────────────────────────────────────
    private static readonly Color ShardColor = new(0.39f, 0.85f, 0.75f);
    private static readonly Color ShardEdge  = new(1.6f, 0.9f, 3.0f, 1f);   // 디졸브 테두리(HDR) — 기억의 보랏빛

    /// <summary>아직 줍지 않은 조각. 씬을 떠나기 직전 <see cref="CollectAll"/>이 쓴다.</summary>
    private static readonly System.Collections.Generic.List<EssenceShardPickup> s_live = new();

    // ── Private Fields ───────────────────────────────────────
    private int       _value;
    private float     _spawnTime;
    private float     _arcTimer;
    private Vector3   _arcStart;
    private Vector3   _arcEnd;
    private Vector3   _restPos;
    private bool      _arcing;
    private bool      _collected;
    private bool      _vanishing;
    private Transform _playerTf;
    private GameObject _outline;   // 「~Outline」 — 내려앉으면 켠다(10-22)

    // ── Lifecycle ────────────────────────────────────────────
    private void OnEnable()  => s_live.Add(this);
    private void OnDisable() => s_live.Remove(this);

    private void Update()
    {
        if (_vanishing)
        {
            // 주운 조각은 녹는 동안 몸으로 따라 들어간다 — 제자리에서 녹으면 「안 주워졌다」로 읽힌다
            if (_collected) FollowPlayer();
            return;
        }

        if (_arcing)
        {
            _arcTimer += Time.deltaTime;
            float t = Mathf.Clamp01(_arcTimer / ArcDuration);
            Vector3 pos = Vector3.Lerp(_arcStart, _arcEnd, t);
            pos.y += Mathf.Sin(t * Mathf.PI) * ArcHeight;
            transform.position = pos;
            transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.World);

            if (t >= 1f)
            {
                _arcing  = false;
                _restPos = _arcEnd;
                if (_outline != null) _outline.SetActive(true);
            }
            return;
        }

        if (!_collected && Time.time - _spawnTime >= PickupDelay)
        {
            Transform player = ResolvePlayer();
            if (player != null)
            {
                Vector3 delta = player.position - transform.position;
                float dist = delta.magnitude;

                if (dist <= PickupRadius) { Collect(false); return; }

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
        {
            _vanishing = true;
            if (_outline != null) _outline.SetActive(false);   // 테두리는 디졸브를 안 탄다 — 먼저 끈다
            CurrencyVisuals.Vanish(gameObject, CurrencyVisualKind.Essence, ExpireVanishSeconds);
        }
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
            Vector3 landing = origin + new Vector3(Mathf.Cos(angle) * r, RestHeight, Mathf.Sin(angle) * r);

            var go = CreateShardVisual();
            go.transform.position = startPos;
            RoomScopedDrop.Mark(go);   // 방 전환 시 정리 대상(부모가 없어 방 파괴로는 안 지워짐)

            var shard = go.AddComponent<EssenceShardPickup>();
            shard.Initialize(startPos, landing, perShard + (i == 0 ? remainder : 0));
            shard._outline = CurrencyVisuals.AttachOutline(go, CurrencyVisualKind.Essence);
            CurrencyVisuals.Appear(go, CurrencyVisualKind.Essence, AppearSeconds, ShardEdge);
        }
    }

    /// <summary><see cref="GoldCoinPickup.PushAwayFrom"/>과 같다 — 정예 · 보스 정수 조각도 보상 구체 위에 겹쳤다(10-22).</summary>
    public static void PushAwayFrom(Vector3 center, float radius)
    {
        for (int i = 0; i < s_live.Count; i++)
        {
            var s = s_live[i];
            if (s == null || s._collected || s._vanishing) continue;

            Vector3 at   = s._arcing ? s._arcEnd : s._restPos;
            Vector3 flat = at - center;
            flat.y = 0f;
            float d = flat.magnitude;
            if (d >= radius) continue;

            Vector3 dir = d > 0.01f ? flat / d : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            Vector3 landing = at + dir * (radius - d + Random.Range(0.1f, 0.5f));
            if (NavMesh.SamplePosition(landing - Vector3.up * RestHeight, out var hit, 1f, NavMesh.AllAreas)
                && Mathf.Abs(hit.position.y + RestHeight - at.y) < 0.5f)
                landing = hit.position + Vector3.up * RestHeight;

            s._arcStart = s.transform.position;
            s._arcEnd   = landing;
            s._arcTimer = 0f;
            s._arcing   = true;
        }
    }

    /// <summary>
    /// 떨어져 있는 조각을 전부 즉시 적립한다. 챕터 이동·런 클리어처럼 <b>씬을 떠나기 직전</b>에 부른다 —
    /// 보스 정수는 보스를 쓰러뜨린 자리에 떨어지는데, 드래프트·선택창이 곧바로 시간을 멈추면
    /// 자석이 끌어오기 전에 씬이 바뀌어 영구 재화가 사라진다. 사망 때는 부르지 않는다(못 주운 건 잃는다).
    /// </summary>
    public static void CollectAll()
    {
        int count = 0, total = 0;
        for (int i = s_live.Count - 1; i >= 0; i--)
        {
            if (i >= s_live.Count) continue;   // Collect → Destroy → OnDisable로 목록이 줄 수 있다
            var shard = s_live[i];
            if (shard == null || shard._collected || shard._vanishing) continue;   // 수명이 다해 녹는 중인 조각은 이미 잃은 것
            total += shard._value;
            shard.Collect(true);   // 씬을 떠나는 순간 — 녹는 연출 없이 바로 거둔다
            count++;
        }
        if (count > 0) Debug.Log($"[Essence] 떠나기 전 정수 조각 {count}개 회수 (+{total})");
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

    private void Collect(bool instant)
    {
        if (_collected) return;
        _collected = true;
        _vanishing = true;
        if (_outline != null) _outline.SetActive(false);

        // 깊이 보상 배율은 AddEssence 안에서 걸린다 — 여기서 또 곱하면 이중 적용이 된다.
        GameRunBootstrapper.Instance?.Run?.AddEssence(_value);
        Managers.Sound?.PlayEvent(SoundEvent.EssencePickup);
        if (instant) Destroy(gameObject);
        else CurrencyVisuals.Vanish(gameObject, CurrencyVisualKind.Essence, CollectVanishSeconds);
    }

    private void FollowPlayer()
    {
        Transform player = ResolvePlayer();
        if (player == null) return;
        transform.position = Vector3.MoveTowards(transform.position, player.position, MagnetSpeed * Time.deltaTime);
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
        // 기억을 담은 캡슐(10-12) — 목록이 없거나 칸이 비면 임시 정육면체
        var art = CurrencyVisuals.TryCreate(CurrencyVisualKind.Essence);
        if (art != null)
        {
            art.name = "EssenceShard";
            art.transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), RestTilt);
            return art;
        }
        return CreatePlaceholder();
    }

    /// <summary>예전 임시 비주얼(청록 정육면체). 목록이 없을 때의 폴백 · 비교 도구의 「지금」 사진.</summary>
    private static GameObject CreatePlaceholder()
    {
        // 형태(정육면체·회전)와 색(청록)으로 골드와 구분한다.
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
