using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace RelicFairy.Monster
{
public class DKSoulPillar : MonoBehaviour, IDamageable
{
    private static readonly int BaseColorId     = Shader.PropertyToID("_BaseColor");
    private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

    private float                  _hp;
    private GameObject             _destroyVfxPrefab;
    private float                  _destroyVfxScale;
    private AudioClip              _destroySfx;
    private MonsterContext         _ctx;
    private Action                 _onDestroyed;
    private GameObject             _vfxInstance;
    private float                  _popupHeight;

    private Renderer[]            _visualRenderers;
    private int[]                 _originalLayers;
    private int                   _originalRootLayer;
    private MaterialPropertyBlock _propBlock;
    private Coroutine             _hitBlinkCoroutine;

    public bool  IsDestroyed { get; private set; }
    public float RemainingHp => Mathf.Max(0f, _hp);

    public void Initialize(float hp, GameObject destroyVfx, float destroyVfxScale,
                           AudioClip destroySfx,
                           MonsterContext ctx,
                           Action onDestroyed,
                           GameObject vfxInstance, GameObject visualPillarGo = null)
    {
        _hp               = hp;
        _destroyVfxPrefab = destroyVfx;
        _destroyVfxScale  = destroyVfxScale;
        _destroySfx       = destroySfx;
        _ctx              = ctx;
        _onDestroyed      = onDestroyed;
        _vfxInstance      = vfxInstance;
        IsDestroyed       = false;
        _propBlock        = new MaterialPropertyBlock();

        var box = GetComponent<BoxCollider>();
        _popupHeight = box != null ? box.size.y * 0.85f : 2.8f;

        // Base(받침대) + Column(기둥 몸통) 모든 렌더러 수집
        // 기둥은 두 개의 별도 GO(SM_Pillar_Base + SM_PillarMiddle)로 구성되므로
        // OverlapCapsule로 동일 XZ의 Column 파트를 찾아 함께 처리
        var rendList = new List<Renderer>(GetComponentsInChildren<Renderer>(true));
        var nearbyColliders = Physics.OverlapCapsule(
            transform.position + Vector3.up * 1f,
            transform.position + Vector3.up * 14f,
            0.8f, ~0, QueryTriggerInteraction.Ignore);
        foreach (var col in nearbyColliders)
        {
            if (col == null || col.gameObject == gameObject) continue;
            rendList.AddRange(col.GetComponentsInChildren<Renderer>(true));
        }

        _visualRenderers = rendList.ToArray();
        _originalLayers  = new int[_visualRenderers.Length];

        int monsterLayer    = LayerMask.NameToLayer("Monster");
        int monsterHitLayer = LayerMask.NameToLayer("MonsterHit");

        // BoxCollider GO(root)를 MonsterHit 레이어로 설정해야 무기 OverlapBox에 감지됨
        _originalRootLayer = gameObject.layer;
        gameObject.layer   = monsterHitLayer;

        for (int i = 0; i < _visualRenderers.Length; i++)
        {
            if (_visualRenderers[i] == null) continue;
            _originalLayers[i]                   = _visualRenderers[i].gameObject.layer;
            _visualRenderers[i].gameObject.layer = monsterLayer;
        }
    }

    public void TakeDamage(float amount, GameObject instigator,
                           float knockbackMultiplier = 1f, bool isCrit = false)
    {
        if (IsDestroyed) return;

        _hp -= amount;

        DamagePopupSpawner.Spawn(transform.position + Vector3.up * _popupHeight, amount, isCrit);

        if (_visualRenderers != null && _visualRenderers.Length > 0)
        {
            if (_hitBlinkCoroutine != null) StopCoroutine(_hitBlinkCoroutine);
            _hitBlinkCoroutine = StartCoroutine(HitBlinkRoutine());
        }

        if (_hp <= 0f) Burst();
    }

    public void ReleaseVfx()
    {
        if (_vfxInstance == null) return;
        _vfxInstance.transform.SetParent(null, false);
        BossEffectPool.Release(_vfxInstance);
        _vfxInstance = null;
    }

    private void OnDestroy()
    {
        RestoreRendererLayers();
        ClearRendererTint();
    }

    private IEnumerator HitBlinkRoutine()
    {
        SetRendererTint(Color.red, new Color(1f, 0f, 0f, 1f));
        yield return new WaitForSeconds(0.12f);
        ClearRendererTint();
        _hitBlinkCoroutine = null;
    }

    private void SetRendererTint(Color baseColor, Color emission)
    {
        foreach (var r in _visualRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_propBlock);
            _propBlock.SetColor(BaseColorId,     baseColor);
            _propBlock.SetColor(EmissionColorId, emission);
            r.SetPropertyBlock(_propBlock);
        }
    }

    private void ClearRendererTint()
    {
        if (_propBlock == null || _visualRenderers == null) return;
        foreach (var r in _visualRenderers)
        {
            if (r == null) continue;
            r.GetPropertyBlock(_propBlock);
            _propBlock.Clear();
            r.SetPropertyBlock(_propBlock);
        }
    }

    private void RestoreRendererLayers()
    {
        gameObject.layer = _originalRootLayer;

        if (_visualRenderers == null || _originalLayers == null) return;
        for (int i = 0; i < _visualRenderers.Length; i++)
        {
            if (_visualRenderers[i] == null) continue;
            _visualRenderers[i].gameObject.layer = _originalLayers[i];
        }
    }

    private void Burst()
    {
        IsDestroyed = true;

        if (_hitBlinkCoroutine != null)
        {
            StopCoroutine(_hitBlinkCoroutine);
            _hitBlinkCoroutine = null;
        }

        ClearRendererTint();
        RestoreRendererLayers();
        ReleaseVfx();

        if (_destroyVfxPrefab != null)
        {
            var vfx = BossEffectPool.SpawnOneShot(_destroyVfxPrefab, transform.position,
                Quaternion.identity, fallbackLifetime: 3f);
            if (vfx != null && _destroyVfxScale != 1f)
                vfx.transform.localScale = Vector3.one * _destroyVfxScale;
        }

        if (_destroySfx != null)
            Managers.Sound?.PlayEffectAt(_destroySfx, transform.position);

        _onDestroyed?.Invoke();
        Destroy(this); // GO는 씬 오브젝트이므로 컴포넌트만 제거
    }
}
}
