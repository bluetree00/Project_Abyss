using UnityEngine;

namespace Abyss.Monster
{
/// <summary>
/// Dragon breath pillar hazard that damages the player over time and
/// opens a player-shaped cutout when the pillar blocks the camera.
/// </summary>
public sealed class BossColumnHazard : MonoBehaviour
{
    private const float CylinderHeight = 5f;
    private const string CutoutShaderName = "Abyss/Boss/ColumnPlayerCutout";
    private const float PlayerHeight = 1.8f;
    private const float CutoutRadiusPadding = 0.45f;
    private const float CutoutSoftness = 0.2f;
    private const float SegmentRadiusPadding = 0.2f;
    private const float OcclusionHeightPadding = 0.35f;

    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private static readonly int PlayerWorldPosId = Shader.PropertyToID("_PlayerWorldPos");
    private static readonly int PlayerHeightId = Shader.PropertyToID("_PlayerHeight");
    private static readonly int CutoutRadiusId = Shader.PropertyToID("_CutoutRadius");
    private static readonly int CutoutSoftnessId = Shader.PropertyToID("_CutoutSoftness");
    private static readonly int CutoutEnabledId = Shader.PropertyToID("_CutoutEnabled");
    private static readonly int BaseOpacityId = Shader.PropertyToID("_BaseOpacity");

    private float _radius;
    private float _tickInterval;
    private float _duration;
    private int _damage;
    private DragonBossBlackboard.DragonElement _element;

    private float _tickTimer;
    private float _elapsed;

    private GameObject _cylinderGo;
    private Material _cylinderMat;
    private Camera _mainCamera;
    private Transform _playerTransform;

    public static void Spawn(
        Vector3 position,
        float radius,
        float duration,
        float tickInterval,
        int damage,
        DragonBossBlackboard.DragonElement element)
    {
        var go = new GameObject("[BossColumnHazard]");
        go.transform.position = position;

        var hazard = go.AddComponent<BossColumnHazard>();
        hazard._radius = radius;
        hazard._tickInterval = tickInterval;
        hazard._duration = duration;
        hazard._damage = damage;
        hazard._element = element;
        hazard._tickTimer = tickInterval;
        hazard._elapsed = 0f;
        hazard.CreateCylinder(position, radius, element);
    }

    private void CreateCylinder(Vector3 position, float radius, DragonBossBlackboard.DragonElement element)
    {
        _cylinderGo = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        _cylinderGo.name = "[ColumnCylinder]";
        Object.Destroy(_cylinderGo.GetComponent<Collider>());

        _cylinderGo.transform.position = position + Vector3.up * (CylinderHeight * 0.5f);
        _cylinderGo.transform.localScale = new Vector3(radius * 2f, CylinderHeight * 0.5f, radius * 2f);

        Color color = DragonBossVisualHelper.GetElementColor(element);
        color.a = 0.55f;

        var renderer = _cylinderGo.GetComponent<Renderer>();
        var cutoutShader = Shader.Find(CutoutShaderName);
        _cylinderMat = cutoutShader != null
            ? new Material(cutoutShader)
            : new Material(renderer.sharedMaterial);

        if (_cylinderMat.HasProperty(BaseColorId)) _cylinderMat.SetColor(BaseColorId, color);
        if (_cylinderMat.HasProperty(ColorId)) _cylinderMat.SetColor(ColorId, color);
        if (_cylinderMat.HasProperty(BaseOpacityId)) _cylinderMat.SetFloat(BaseOpacityId, color.a);
        if (_cylinderMat.HasProperty(PlayerHeightId)) _cylinderMat.SetFloat(PlayerHeightId, PlayerHeight);
        if (_cylinderMat.HasProperty(CutoutRadiusId)) _cylinderMat.SetFloat(CutoutRadiusId, radius + CutoutRadiusPadding);
        if (_cylinderMat.HasProperty(CutoutSoftnessId)) _cylinderMat.SetFloat(CutoutSoftnessId, CutoutSoftness);
        if (_cylinderMat.HasProperty(CutoutEnabledId)) _cylinderMat.SetFloat(CutoutEnabledId, 0f);
        if (_cylinderMat.HasProperty("_Surface")) _cylinderMat.SetFloat("_Surface", 1f);
        if (_cylinderMat.HasProperty("_Blend")) _cylinderMat.SetFloat("_Blend", 0f);
        _cylinderMat.renderQueue = 3000;

        renderer.material = _cylinderMat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        _cylinderGo.transform.SetParent(transform, true);
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        _tickTimer += Time.deltaTime;

        UpdatePlayerCutout();

        if (_tickTimer >= _tickInterval)
        {
            _tickTimer -= _tickInterval;
            ApplyDamage();
        }

        if (_elapsed >= _duration)
            Destroy(gameObject);
    }

    private void OnDestroy()
    {
        if (_cylinderMat != null)
        {
            Object.Destroy(_cylinderMat);
            _cylinderMat = null;
        }
    }

    private void UpdatePlayerCutout()
    {
        if (_cylinderMat == null)
            return;

        if (_playerTransform == null)
            _playerTransform = Managers.Player?.PlayerTransform;

        if (_mainCamera == null)
            _mainCamera = Camera.main;

        if (_playerTransform == null || _mainCamera == null)
        {
            if (_cylinderMat.HasProperty(CutoutEnabledId))
                _cylinderMat.SetFloat(CutoutEnabledId, 0f);
            return;
        }

        Vector3 playerFootPos = _playerTransform.position;
        playerFootPos.y = transform.position.y;

        if (_cylinderMat.HasProperty(PlayerWorldPosId))
            _cylinderMat.SetVector(PlayerWorldPosId, playerFootPos);

        if (_cylinderMat.HasProperty(CutoutEnabledId))
            _cylinderMat.SetFloat(
                CutoutEnabledId,
                IsBetweenCameraAndPlayer(_mainCamera.transform.position, _playerTransform.position) ? 1f : 0f);
    }

    private bool IsBetweenCameraAndPlayer(Vector3 cameraPos, Vector3 playerPos)
    {
        Vector3 targetPos = playerPos + Vector3.up * (PlayerHeight * 0.5f);
        Vector3 segment = targetPos - cameraPos;
        float segmentLengthSq = segment.sqrMagnitude;
        if (segmentLengthSq <= 0.0001f)
            return false;

        Vector3 columnCenter = transform.position + Vector3.up * (CylinderHeight * 0.5f);
        float t = Mathf.Clamp01(Vector3.Dot(columnCenter - cameraPos, segment) / segmentLengthSq);
        Vector3 closestPoint = cameraPos + segment * t;

        Vector2 closestXZ = new Vector2(closestPoint.x, closestPoint.z);
        Vector2 columnXZ = new Vector2(transform.position.x, transform.position.z);

        bool intersectsRadius = Vector2.Distance(closestXZ, columnXZ) <= (_radius + SegmentRadiusPadding);
        bool intersectsHeight = closestPoint.y >= (transform.position.y - OcclusionHeightPadding)
            && closestPoint.y <= (transform.position.y + CylinderHeight + OcclusionHeightPadding);

        return intersectsRadius && intersectsHeight;
    }

    private void ApplyDamage()
    {
        var hits = Physics.OverlapSphere(transform.position, _radius);
        foreach (var col in hits)
        {
            var player = col.GetComponent<PlayerController>()
                ?? col.GetComponentInParent<PlayerController>();
            if (player == null)
                continue;

            player.TakeDamage(_damage);

            switch (_element)
            {
                case DragonBossBlackboard.DragonElement.Ice:
                    player.ApplyKnockback(Vector3.zero, 2f);
                    break;
                case DragonBossBlackboard.DragonElement.Thunder:
                    player.ApplyThunderGroggy(2f);
                    break;
                default:
                    player.ApplySlow(0.4f, 2f);
                    break;
            }

            break;
        }
    }
}
}
