using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

/// <summary>
/// 몹 발밑 가짜 그림자(블롭). MonsterBase가 스폰 시 런타임으로 부착한다(프리팹 수정 없음).
/// 자식 쿼드를 바닥에 눕혀 절차적 그림자 셰이더(RelicFairy/MonsterGroundShadow)로 렌더 —
/// 등급(Common/Rare/Elite/Boss)에 따라 진하기/외곽 링을 MaterialPropertyBlock으로 차등.
///
/// ※ 쿼드는 Monster 레이어가 아닌 Default 레이어 유지 — 외곽선/실루엣 Render Objects 피처가
///   그림자까지 표기하지 않도록.
/// </summary>
[DisallowMultipleComponent]
public sealed class MonsterGroundShadow : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const string MaterialKey  = "Mat_MonsterGroundShadow";
    private const string ShaderName   = "RelicFairy/MonsterGroundShadow";
    private static readonly int ColorId     = Shader.PropertyToID("_Color");
    private static readonly int RingColorId = Shader.PropertyToID("_RingColor");

    // 등급별 기본값 — 시각 튜닝은 머티리얼/여기 상수로. "은은하게"가 기본.
    private const float CommonAlpha = 0.40f;
    private const float EliteAlpha  = 0.50f;
    private const float BossAlpha   = 0.60f;

    // ── Static (공유 머티리얼 1장) ─────────────────────────────────
    private static Material s_sharedMat;
    private static bool     s_loading;

    // ── Private ───────────────────────────────────────────────────
    private MeshRenderer          _renderer;
    private MaterialPropertyBlock _mpb;
    private MonsterGrade          _grade;
    private bool                  _built;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>발밑 그림자 생성/구성. footprintRadius=발자국 반경(m). 인스턴스당 1회면 충분(풀 재사용 시 유지).</summary>
    public void Configure(MonsterGrade grade, float footprintRadius)
    {
        _grade = grade;
        if (!_built) BuildQuad(footprintRadius);
        ApplyAsync(this.GetCancellationTokenOnDestroy()).Forget();
    }

    // ── Private Methods ───────────────────────────────────────────
    private void BuildQuad(float footprintRadius)
    {
        _built = true;

        var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
        quad.name = "~GroundShadow";
        // 쿼드 기본 콜라이더 제거 — 물리/타격에 끼어들지 않게.
        var col = quad.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var t = quad.transform;
        t.SetParent(transform, worldPositionStays: false);
        t.localPosition = new Vector3(0f, 0.02f, 0f);   // 바닥 살짝 위 — z-fighting 방지
        t.localRotation = Quaternion.Euler(90f, 0f, 0f); // XZ 평면에 눕힘
        float d = Mathf.Max(0.1f, footprintRadius * 2f);
        t.localScale = new Vector3(d, d, 1f);

        _renderer = quad.GetComponent<MeshRenderer>();
        _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        _renderer.receiveShadows    = false;
        _renderer.enabled           = false; // 머티리얼 적용 전 기본 Lit 머티리얼 깜빡임 방지
        _mpb = new MaterialPropertyBlock();
    }

    private async UniTaskVoid ApplyAsync(CancellationToken ct)
    {
        var mat = await EnsureMaterialAsync(ct);
        if (mat == null || _renderer == null) return;

        _renderer.sharedMaterial = mat;

        // 등급별 진하기/링 — 공유 머티리얼 + MPB로 인스턴스별 차등(머티리얼 복제 없음).
        float alpha = _grade switch
        {
            MonsterGrade.Boss  => BossAlpha,
            MonsterGrade.Elite => EliteAlpha,
            _                  => CommonAlpha,
        };

        _renderer.GetPropertyBlock(_mpb);
        _mpb.SetColor(ColorId, new Color(0f, 0f, 0f, alpha));

        // 정예/보스만 외곽 링 — 색약 안전(형태로 등급 구분). 일반 몹은 링 끔(알파 0).
        Color ring = _grade switch
        {
            MonsterGrade.Boss  => new Color(1f, 0.3f, 0.2f, 0.55f),
            MonsterGrade.Elite => new Color(1f, 0.85f, 0.3f, 0.5f),
            _                  => new Color(1f, 1f, 1f, 0f),
        };
        _mpb.SetColor(RingColorId, ring);
        _renderer.SetPropertyBlock(_mpb);
        _renderer.enabled = true; // 우리 머티리얼 적용 완료 — 표시
    }

    private static async UniTask<Material> EnsureMaterialAsync(CancellationToken ct)
    {
        if (s_sharedMat != null) return s_sharedMat;

        while (s_loading) await UniTask.Yield(ct);
        if (s_sharedMat != null) return s_sharedMat;

        s_loading = true;
        try
        {
            // 1순위: Addressable 머티리얼(사용자 튜닝 대상). 없으면 셰이더 직접 폴백.
            var loaded = await Managers.AddressableManager.TryLoadAssetAsync<Material>(MaterialKey);
            if (loaded != null) { s_sharedMat = loaded; return s_sharedMat; }

            var shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning($"[MonsterGroundShadow] 셰이더 '{ShaderName}'를 찾을 수 없습니다.");
                return null;
            }
            s_sharedMat = new Material(shader) { name = "MonsterGroundShadow (Runtime)" };
            return s_sharedMat;
        }
        finally { s_loading = false; }
    }
}
