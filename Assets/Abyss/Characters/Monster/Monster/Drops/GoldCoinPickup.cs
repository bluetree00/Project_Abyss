using UnityEngine;

/// <summary>
/// 몬스터 사망 시 드롭되는 골드 코인.
///  · 스폰 직후 짧은 파라볼라 비행(arc) 후 착지
///  · 플레이어가 MagnetRange 진입 시 자석처럼 빨려오다가 PickupRadius 안에서 획득
///  · 획득 시 GameRunSession.AddGold 로 coinValue 지급
/// 런타임 Primitive(Sphere) 로 시각을 생성하므로 별도 프리팹 등록 불필요.
/// 추후 아트 프리팹이 생기면 SpawnDrops 를 프리팹 로드 기반으로 교체.
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
    private const float CoinSize      = 0.35f;
    private const float LifeTime      = 30f;
    private const float SpinSpeed     = 720f;

    // ── Static ───────────────────────────────────────────────
    private static readonly Color CoinColor = new Color(1f, 0.82f, 0.1f);

    // ── Private Fields ───────────────────────────────────────
    private int       _value;
    private float     _spawnTime;
    private float     _arcTimer;
    private Vector3   _arcStart;
    private Vector3   _arcEnd;
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
            Destroy(gameObject);
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

            var coin = go.AddComponent<GoldCoinPickup>();
            coin.Initialize(startPos, landing, valuePerCoin);
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
    }

    private void Collect()
    {
        if (_collected) return;
        _collected = true;

        GameRunBootstrapper.Instance?.Run?.AddGold(_value);
        Destroy(gameObject);
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
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = "GoldCoin";
        go.transform.localScale = Vector3.one * CoinSize;

        // 자체 트리거 로직으로 픽업을 판정하므로 Primitive 기본 콜라이더는 제거
        var col = go.GetComponent<Collider>();
        if (col != null) Object.Destroy(col);

        var rend = go.GetComponent<Renderer>();
        if (rend != null)
        {
            var mat = rend.material; // instance — 공유 머티리얼 오염 방지
            if (mat.HasProperty("_Color"))      mat.color = CoinColor;
            if (mat.HasProperty("_BaseColor"))  mat.SetColor("_BaseColor", CoinColor);
            if (mat.HasProperty("_Smoothness")) mat.SetFloat("_Smoothness", 0.9f);
            if (mat.HasProperty("_Metallic"))   mat.SetFloat("_Metallic", 0.9f);
        }

        return go;
    }
}
