// WeaponDisplay.cs
using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 무기 정보를 UI/오브젝트로 표시하는 컴포넌트
/// - testWeaponSO에 SO를 넣어 인스펙터에서 테스트 가능
/// - Addressables로 로드된 프리팹 인스턴스 핸들을 보관/해제
/// - 클릭 콜백(onClicked)으로 획득 동작 연결
/// </summary>
[RequireComponent(typeof(Button))]
public class WeaponDisplay : MonoBehaviour
{
    [Header("테스트용 (인스펙터에 SO 넣기)")]
    public WeaponSO testWeaponSO;

    [Header("UI 참조 (옵션)")]
    public Image iconImage;
    public Text nameText;        // UnityEngine.UI.Text 사용 (.text)
    public Text priceText;

    // 내부 상태
    private WeaponSO _so;
    private AsyncOperationHandle<Sprite>? _iconHandle;
    private AsyncOperationHandle<GameObject>? _instanceHandle; // InstantiateAsync 핸들 (선택적 보관)

    // 클릭 시 호출되는 콜백 (외부에서 할당)
    public Action<WeaponSO> onClicked;

    private void Start()
    {
        // 인스펙터에 SO 넣어놨다면 자동 초기화 (테스트용)
        if (testWeaponSO != null)
            Initialize(testWeaponSO);

        // 버튼 클릭 바인딩
        var btn = GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(() => onClicked?.Invoke(_so));
    }

    /// <summary>
    /// WeaponSO로 UI/디스플레이 초기화
    /// </summary>
    public void Initialize(WeaponSO so)
    {
        _so = so;
        if (nameText != null)
            nameText.text = so.displayName;

        // 가격/설명 등도 있으면 세팅
        // priceText?.text = so.cost.ToString();

        // 아이콘이 있으면 비동기 로드
        if (!string.IsNullOrEmpty(so.iconKey) && iconImage != null)
        {
            _ = LoadIconAsync(so.iconKey);
        }
        else if (iconImage != null)
        {
            iconImage.color = Color.clear;
            iconImage.sprite = null;
        }
    }

    /// <summary>
    /// InstantiateAsync로 생성된 핸들을 외부에서 주입하면 보관한다.
    /// Destroy 시 이 핸들을 Release 한다 (호출자와 계약에 따라 조절 가능).
    /// </summary>
    public void AttachInstantiateHandle(AsyncOperationHandle<GameObject> handle)
    {
        // 이전 핸들 해제(필요시)
        if (_instanceHandle.HasValue)
        {
            var prev = _instanceHandle.Value;
            if (prev.IsValid()) Addressables.Release(prev);
            _instanceHandle = null;
        }
        _instanceHandle = handle;
    }

    /// <summary>
    /// 아이콘 로드 및 세팅 (Addressables.LoadAssetAsync<Sprite>(key))
    /// </summary>
    private async UniTask LoadIconAsync(string iconKey)
    {
        // 이전 핸들 해제
        if (_iconHandle.HasValue)
        {
            var prev = _iconHandle.Value;
            if (prev.IsValid()) Addressables.Release(prev);
            _iconHandle = null;
        }

        try
        {
            var handle = Addressables.LoadAssetAsync<Sprite>(iconKey);
            _iconHandle = handle;
            await handle.Task;

            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
            {
                iconImage.sprite = handle.Result;
                iconImage.color = Color.white;
            }
            else
            {
                iconImage.sprite = null;
                iconImage.color = Color.clear;
                Debug.LogWarning($"WeaponDisplay: 아이콘 로드 실패 - {iconKey}");
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"WeaponDisplay: 아이콘 로드 중 예외 - {iconKey} / {e.Message}");
        }
    }

    private void OnDestroy()
    {
        // 아이콘 핸들 정리
        if (_iconHandle.HasValue)
        {
            var h = _iconHandle.Value;
            if (h.IsValid()) Addressables.Release(h);
            _iconHandle = null;
        }

        // 인스턴스 핸들 정리 (주의: 호출자와 소유권 계약에 따라 다름)
        if (_instanceHandle.HasValue)
        {
            var ih = _instanceHandle.Value;
            if (ih.IsValid()) Addressables.ReleaseInstance(ih);
            _instanceHandle = null;
        }
    }

    // 외부에서 현재 표시중인 SO 접근
    public WeaponSO GetWeaponSO() => _so;
}
