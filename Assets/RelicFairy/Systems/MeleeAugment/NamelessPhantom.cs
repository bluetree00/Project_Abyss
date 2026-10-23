using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 등 뒤 무형검 환영(⑧-기획 A · 10-18 · 기획/RelicFairy_무형검증강_진화이식_설계_20261018.md §3-2).
/// 진화한 무기에 무형검 증강이 남아 있으면(<see cref="MeleeAugmentCatalog.HasPhantom"/>) 등 뒤 어깨 위에 반쯤 비친 무형검이 떠 있고,
/// 그 증강이 나갈 때(<see cref="Swing"/>) 앞으로 나와 한 번 가르고 등 뒤로 돌아간다.
/// 입자는 없다(이펙트 절제) — 베는 그림은 증강 쪽 검기 · 바닥 띠가 맡는다.
/// 모델 = 무형검 메시(<c>T0_Nameless_Weapon</c>) · 재질 = 회피 잔상과 같은 유령 재질(<see cref="CharacterData.dodgeGhostMaterial"/>).
/// </summary>
[DisallowMultipleComponent]
public sealed class NamelessPhantom : MonoBehaviour
{
    // ── Constants ─────────────────────────────────────────────────
    private const string ModelKey = "T0_Nameless_Weapon";
    private const float IdleAlpha = 0.22f, SwingAlpha = 0.75f;
    private const float SwingSeconds = 0.3f, SwingOut = 0.4f;   // 앞으로 나가는 몫(비율) — 나머지는 돌아오는 몫
    private const float FollowSharpness = 12f;                  // 플레이어를 살짝 늦게 따라간다
    private const float BobAmp = 0.04f, BobSpeed = 2f;
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly Vector3 IdleLocal = new(0.32f, 1.3f, -0.45f);                   // 오른 어깨 위 · 등 뒤
    private static readonly Vector3 IdleBladeLocal = new Vector3(0.15f, 1f, -0.3f).normalized;   // 칼끝 = 위 · 살짝 뒤로

    // ── Private ───────────────────────────────────────────────────
    private PlayerController _player;
    private Transform _holder;          // 환영 뿌리(월드) — 위(+Y) = 칼끝 방향
    private MeshRenderer _renderer;
    private MaterialPropertyBlock _mpb;
    private Color _baseColor = new(0.4f, 0.7f, 1f, 0.6f);
    private float _alpha = -1f;
    private bool _visible;
    private bool _building;
    private bool _snap = true;
    private float _swingAt = -1f;
    private Vector3 _swingDir = Vector3.forward;
    private CancellationTokenSource _cts;

    // ── Lifecycle ─────────────────────────────────────────────────
    private void Awake()
    {
        TryGetComponent(out _player);
        _mpb = new MaterialPropertyBlock();
    }

    private void OnEnable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged += OnWeaponChanged;
        _cts = new CancellationTokenSource();
    }

    private void LateUpdate()
    {
        if (_holder == null || _player == null) return;
        if (!_visible)
        {
            if (_holder.gameObject.activeSelf) _holder.gameObject.SetActive(false);
            return;
        }
        if (!_holder.gameObject.activeSelf) { _holder.gameObject.SetActive(true); _snap = true; }

        var pt = _player.transform;
        Vector3 idlePos = pt.TransformPoint(IdleLocal) + Vector3.up * (Mathf.Sin(Time.time * BobSpeed) * BobAmp);
        Vector3 idleUp  = pt.TransformDirection(IdleBladeLocal);
        Vector3 pos = idlePos, up = idleUp, face = pt.forward;
        float alpha = IdleAlpha;
        bool swinging = false;

        if (_swingAt >= 0f)
        {
            float t = (Time.time - _swingAt) / SwingSeconds;
            if (t >= 1f) _swingAt = -1f;
            else
            {
                swinging = true;
                float w = t < SwingOut ? t / SwingOut : 1f - (t - SwingOut) / (1f - SwingOut);
                w = w * w * (3f - 2f * w);
                // 앞으로 나와 칼끝이 앞을 가로지른다(-70° → +70°) — 「살짝」 범위의 한 번 가르기
                float a = Mathf.Lerp(-70f, 70f, Mathf.Clamp01(t / SwingOut));
                Vector3 slashUp = Quaternion.AngleAxis(a, Vector3.up) * Vector3.Slerp(_swingDir, Vector3.down, 0.2f);
                pos   = Vector3.Lerp(idlePos, pt.position + _swingDir * 0.9f + Vector3.up * 1.1f, w);
                up    = Vector3.Slerp(idleUp, slashUp, w).normalized;
                face  = Vector3.Slerp(pt.forward, Vector3.up, w);
                alpha = Mathf.Lerp(IdleAlpha, SwingAlpha, w);
            }
        }

        if (Mathf.Abs(Vector3.Dot(face.normalized, up)) > 0.95f) face = Vector3.Cross(up, pt.right);
        Quaternion rot = Quaternion.LookRotation(face, up);
        float k = _snap || swinging ? 1f : 1f - Mathf.Exp(-FollowSharpness * Time.deltaTime);
        _holder.SetPositionAndRotation(Vector3.Lerp(_holder.position, pos, k), Quaternion.Slerp(_holder.rotation, rot, k));
        _snap = false;
        SetAlpha(alpha);
    }

    private void OnDisable()
    {
        if (_player != null && _player.WeaponManager != null) _player.WeaponManager.OnWeaponChanged -= OnWeaponChanged;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _building = false;
        if (_holder != null) _holder.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (_holder != null) Destroy(_holder.gameObject);
    }

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>플레이어에 환영을 붙이고(없으면) 지금 무기로 보이기를 다시 정한다 — 환영 몫 증강이 켜질 때 부른다.</summary>
    public static void Ensure(PlayerController p)
    {
        if (p == null) return;
        if (!p.TryGetComponent<NamelessPhantom>(out var ph)) ph = p.gameObject.AddComponent<NamelessPhantom>();
        ph.Refresh();
    }

    /// <summary>환영이 <paramref name="dir"/> 쪽으로 한 번 가른다(증강이 나갈 때). 환영이 없으면 아무 일도 없다.</summary>
    public static void Swing(PlayerController p, Vector3 dir)
    {
        if (p == null || !p.TryGetComponent<NamelessPhantom>(out var ph)) return;
        dir.y = 0f;
        ph._swingDir = dir.sqrMagnitude > 0.0001f ? dir.normalized : p.transform.forward;
        ph._swingAt = Time.time;
    }

    // ── Private Methods ───────────────────────────────────────────
    private void Refresh()
    {
        _visible = _player != null && _player.WeaponManager != null && MeleeAugmentCatalog.HasPhantom(_player.WeaponManager.CurrentWeaponData);
        if (_visible && _holder == null && !_building && _cts != null) BuildAsync(_cts.Token).Forget();
    }

    private async UniTaskVoid BuildAsync(CancellationToken ct)
    {
        _building = true;
        try
        {
            var prefab = await Managers.AddressableManager.LoadAssetAsync<GameObject>(ModelKey);
            ct.ThrowIfCancellationRequested();
            var mat = _player != null && _player.CharacterData != null ? _player.CharacterData.dodgeGhostMaterial : null;
            var src = prefab != null ? prefab.GetComponentInChildren<MeshFilter>(true) : null;
            if (src == null || src.sharedMesh == null || mat == null || _holder != null) return;

            // 메시만 떼어 낸다(무기 프리팹의 판정 · 트레일 스크립트는 붙이지 않는다). 손잡이 → 칼끝을 뿌리의 위(+Y)로 돌려 놓는다.
            Transform root = prefab.transform;
            Matrix4x4 rel = root.worldToLocalMatrix * src.transform.localToWorldMatrix;
            Vector3 grip = FindLocal(root, "Root", Vector3.zero);
            Vector3 tip  = FindLocal(root, "Tip", grip + (Vector3)rel.MultiplyVector(Vector3.up));
            Vector3 axis = tip - grip;
            Quaternion align = axis.sqrMagnitude > 0.0001f ? Quaternion.FromToRotation(axis.normalized, Vector3.up) : Quaternion.identity;

            _holder = new GameObject("NamelessPhantom").transform;
            var blade = new GameObject("Blade");
            blade.transform.SetParent(_holder, false);
            blade.transform.localPosition = align * ((Vector3)rel.GetColumn(3) - grip);
            blade.transform.localRotation = align * rel.rotation;
            blade.transform.localScale    = rel.lossyScale;
            blade.AddComponent<MeshFilter>().sharedMesh = src.sharedMesh;
            _renderer = blade.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial    = mat;
            _renderer.shadowCastingMode = ShadowCastingMode.Off;
            _renderer.receiveShadows    = false;
            if (mat.HasProperty(BaseColorId)) _baseColor = mat.GetColor(BaseColorId);
            _alpha = -1f;
            _snap  = true;
            _holder.gameObject.SetActive(_visible);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Debug.LogWarning($"[NamelessPhantom] 환영 모델 실패: {ex.Message}"); }
        finally { _building = false; }
    }

    private static Vector3 FindLocal(Transform root, string name, Vector3 fallback)
    {
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return root.InverseTransformPoint(t.position);
        return fallback;
    }

    private void SetAlpha(float a)
    {
        if (_renderer == null || Mathf.Approximately(a, _alpha)) return;
        _alpha = a;
        var c = _baseColor; c.a = a;
        _mpb.SetColor(BaseColorId, c);
        _renderer.SetPropertyBlock(_mpb);
    }

    // ── Event Handlers ────────────────────────────────────────────
    private void OnWeaponChanged(WeaponData w, GameObject instance) => Refresh();
}
