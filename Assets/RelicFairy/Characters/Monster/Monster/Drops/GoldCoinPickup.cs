using UnityEngine;

/// <summary>
/// 몬스터 사망 시 드롭되는 골드 코인.
///  · 스폰 직후 짧은 파라볼라 비행(arc) — 금화가 두 바퀴 뒤집히다 바닥에 누운 채 내려앉는다
///  · 플레이어가 MagnetRange 진입 시 자석처럼 빨려오다가 PickupRadius 안에서 획득
///  · 획득 시 GameRunSession.AddGold 로 coinValue 지급
///  · 나타나기 · 사라지기(획득 · 수명 끝)는 디졸브(<see cref="CurrencyVisuals"/>)
/// 모양은 재화 오브젝트 목록(<see cref="CurrencyVisualSetSO"/>의 골드 칸 = 왕관 금화, 10-12)에서 꺼낸다.
/// 목록이 아직 없거나 칸이 비면 예전 임시 비주얼(Primitive Sphere)로 떨어진다.
/// </summary>
public class GoldCoinPickup : MonoBehaviour
{
    // ── Constants ────────────────────────────────────────────
    private const float ArcDuration   = 0.45f;
    private const float ArcHeight     = 1.4f;
    private const float PickupDelay   = 0.4f;   // 스폰 직후 즉시 픽업 방지
    private const float MagnetRange   = 3f;
    private const float MagnetSpeed   = 12f;
    private const float PickupRadius  = 0.6f;
    private const float ScatterRadius = 1.3f;
    private const float CoinSize      = 0.35f;  // 임시 구체 지름
    private const float LifeTime      = 30f;
    private const float SpinSpeed     = 720f;
    private const float FlipTurns     = 2f;     // 날아가는 동안 뒤집히는 바퀴 수 — 정수라야 누운 채 내려앉는다
    private const float AppearSeconds = 0.3f;   // 나타나기 디졸브 — 날아가는 동안(0.45초) 끝난다. 줍기 대기(0.4초)보다 짧게
    private const float CollectVanishSeconds = 0.18f;
    private const float ExpireVanishSeconds  = 0.6f;

    // ── Static ───────────────────────────────────────────────
    private static readonly Color CoinColor = new Color(1f, 0.82f, 0.1f);
    private static readonly Color CoinEdge  = new Color(2.2f, 1.5f, 0.35f, 1f);   // 디졸브 테두리(HDR) — 금빛

    // ── Private Fields ───────────────────────────────────────
    private int       _value;
    private float     _spawnTime;
    private float     _arcTimer;
    private float     _yaw;
    private Vector3   _arcStart;
    private Vector3   _arcEnd;
    private bool      _arcing;
    private bool      _collected;
    private bool      _vanishing;
    private Transform _playerTf;

    // ── Lifecycle ────────────────────────────────────────────
    private void Update()
    {
        if (_vanishing)
        {
            // 주운 금화는 녹는 동안 몸으로 따라 들어간다 — 제자리에서 녹으면 「안 주워졌다」로 읽힌다
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
            transform.rotation = Quaternion.Euler(t * 360f * FlipTurns, _yaw, 0f);

            if (t >= 1f) _arcing = false;
            return;
        }

        if (!_collected && Time.time - _spawnTime >= PickupDelay)
        {
            Transform player = ResolvePlayer();
            if (player != null)
            {
                Vector3 delta = player.position - transform.position;
                float dist = delta.magnitude;

                if (dist <= PickupRadius)
                {
                    Collect();
                    return;
                }

                if (dist <= MagnetRange)
                {
                    transform.position += delta.normalized * MagnetSpeed * Time.deltaTime;
                    transform.Rotate(Vector3.up, SpinSpeed * Time.deltaTime, Space.World);
                }
            }
        }

        if (Time.time - _spawnTime >= LifeTime)
        {
            _vanishing = true;
            CurrencyVisuals.Vanish(gameObject, CurrencyVisualKind.Gold, ExpireVanishSeconds);
        }
    }

    // ── Public Methods ───────────────────────────────────────
    /// <summary>origin 주변에 골드 코인 count 개를 흩뿌려 스폰한다.</summary>
    public static void SpawnDrops(Vector3 origin, int count, int valuePerCoin)
    {
        if (count <= 0 || valuePerCoin <= 0) return;

        Vector3 startPos = origin + Vector3.up * 0.6f;

        for (int i = 0; i < count; i++)
        {
            float angle = Random.Range(0f, Mathf.PI * 2f);
            float r     = Random.Range(0.35f, ScatterRadius);
            Vector3 landing = origin + new Vector3(Mathf.Cos(angle) * r, 0.05f, Mathf.Sin(angle) * r);

            var go = CreateCoinVisual();
            go.transform.position = startPos;
            RoomScopedDrop.Mark(go);   // 방 전환 시 정리 대상(부모가 없어 방 파괴로는 안 지워짐)

            var coin = go.AddComponent<GoldCoinPickup>();
            coin.Initialize(startPos, landing, valuePerCoin);
            CurrencyVisuals.Appear(go, CurrencyVisualKind.Gold, AppearSeconds, CoinEdge);
        }
    }

    // ── Private Methods ──────────────────────────────────────
    private void Initialize(Vector3 arcStart, Vector3 arcEnd, int value)
    {
        _arcStart  = arcStart;
        _arcEnd    = arcEnd;
        _value     = value;
        _spawnTime = Time.time;
        _arcTimer  = 0f;
        _arcing    = true;
        _yaw       = Random.Range(0f, 360f);
        transform.rotation = Quaternion.Euler(0f, _yaw, 0f);
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;
        _vanishing = true;

        GameRunBootstrapper.Instance?.Run?.AddGold(_value);
        CurrencyVisuals.Vanish(gameObject, CurrencyVisualKind.Gold, CollectVanishSeconds);
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

    private static GameObject CreateCoinVisual()
    {
        // 왕관 금화(10-12) — 목록이 없거나 칸이 비면 임시 구체
        var art = CurrencyVisuals.TryCreate(CurrencyVisualKind.Gold);
        if (art != null)
        {
            art.name = "GoldCoin";
            return art;
        }
        return CreatePlaceholder();
    }

    /// <summary>예전 임시 비주얼(금색 구체). 목록이 없을 때의 폴백 · 비교 도구의 「지금」 사진.</summary>
    private static GameObject CreatePlaceholder()
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GoldCoin";
        go.transform.localScale = Vector3.one * CoinSize;

        // 자체 트리거 로직으로 픽업을 판정하므로 Primitive 기본 콜라이더는 제거
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var rend = go.GetComponent<Renderer>();
        // 빌드에서 프리미티브 기본 머티리얼이 핑크로 스트립되는 문제 → URP/Lit 명시 할당.
        RuntimePrimitiveMaterial.Apply(rend, CoinColor);
        if (rend != null)
        {
            var mat = rend.material; // instance — 공유 머티리얼 오염 방지
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0.9f);
        }

        return go;
    }
}
