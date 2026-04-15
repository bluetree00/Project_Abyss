using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// 공격 테스트용 허수아비.
/// 데미지를 받으면 HP가 줄어들고, 일정 시간 후 자동 회복.
/// 절대 죽지 않음 (IKillable.IsDead = false).
/// 머리 위 체력바 + 데미지 표시.
/// </summary>
public class TrainingDummy : MonoBehaviour, IDamageable, IKillable
{
    [Header("Stats")]
    [SerializeField] private float maxHp = 500f;
    [SerializeField] private float regenDelay = 3f;
    [SerializeField] private float regenPerSecond = 100f;

    [Header("UI")]
    [SerializeField] private Slider hpBar;
    [SerializeField] private TMPro.TMP_Text hpText;
    [SerializeField] private TMPro.TMP_Text damageText;

    [Header("Hit Animation")]
    [SerializeField] private Animator animator;
    private static readonly int HitHash = Animator.StringToHash("Hit");

    [Header("Hit Color Flash")]
    [SerializeField] private Renderer[] renderers;
    private MaterialPropertyBlock _mpb;
    private static readonly int ColorID = Shader.PropertyToID("_BaseColor");

    private float _currentHp;
    private float _lastHitTime;
    private float _totalDamageShown;
    private Color _originalColor = Color.white;

    public bool IsDead => false;

    private void Awake()
    {
        _currentHp = maxHp;
        _mpb = new MaterialPropertyBlock();

        if (animator == null)
            animator = GetComponentInChildren<Animator>();
        if (renderers == null || renderers.Length == 0)
            renderers = GetComponentsInChildren<Renderer>();

        UpdateUI();
    }

    private void Update()
    {
        if (Time.time - _lastHitTime > regenDelay && _currentHp < maxHp)
        {
            _currentHp = Mathf.Min(maxHp, _currentHp + regenPerSecond * Time.deltaTime);
            _totalDamageShown = 0f;
            UpdateUI();
        }

        // 체력바를 카메라 쪽으로 회전
        if (hpBar != null)
        {
            var cam = Camera.main;
            if (cam != null)
                hpBar.transform.parent.rotation = cam.transform.rotation;
        }
    }

    public void TakeDamage(float amount, GameObject instigator,
                           float knockbackMultiplier = 1f,
                           ElementType element = ElementType.None,
                           float elementAmount = 0f)
    {
        if (amount <= 0f) return;

        _currentHp = Mathf.Max(0f, _currentHp - amount);
        _lastHitTime = Time.time;
        _totalDamageShown += amount;

        Debug.Log($"[Dummy] Hit! damage={amount:F0}, HP={_currentHp:F0}/{maxHp}");

        // 히트 애니메이션
        if (animator != null)
            animator.SetTrigger(HitHash);

        // 색상 플래시
        StopAllCoroutines();
        StartCoroutine(HitColorFlash());

        UpdateUI();

        // HP 0이면 즉시 풀회복
        if (_currentHp <= 0f)
        {
            _currentHp = maxHp;
            _totalDamageShown = 0f;
            Debug.Log("[Dummy] Reset! Full HP restored.");
            UpdateUI();
        }
    }

    private void UpdateUI()
    {
        if (hpBar != null)
        {
            hpBar.maxValue = maxHp;
            hpBar.value = _currentHp;
        }

        if (hpText != null)
            hpText.text = $"{_currentHp:F0} / {maxHp:F0}";

        if (damageText != null)
        {
            if (_totalDamageShown > 0f)
                damageText.text = $"DMG: {_totalDamageShown:F0}";
            else
                damageText.text = "";
        }
    }

    private System.Collections.IEnumerator HitColorFlash()
    {
        SetColor(Color.red);
        yield return new WaitForSeconds(0.08f);
        SetColor(_originalColor);
    }

    private void SetColor(Color color)
    {
        if (renderers == null || _mpb == null) return;
        _mpb.SetColor(ColorID, color);
        foreach (var r in renderers)
            if (r != null) r.SetPropertyBlock(_mpb);
    }
}
