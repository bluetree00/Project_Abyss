using System;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

/// <summary>
/// 아이템 정보를 UI로 표시하는 컴포넌트.
/// WeaponDisplay와 동일 패턴.
/// </summary>
[RequireComponent(typeof(Button))]
public class ItemDisplay : MonoBehaviour
{
    [Header("테스트용 (인스펙터에 SO 넣기)")]
    [SerializeField] private ItemSO testItemSO;

    [Header("UI 참조 (옵션)")]
    [SerializeField] private Image iconImage;
    [SerializeField] private Text nameText;
    [SerializeField] private Text rarityText;

    private ItemSO _so;
    private RuntimeItemData _runtimeData;
    private AsyncOperationHandle<Sprite>? _iconHandle;

    public event Action<RuntimeItemData> OnClicked;

    private void Start()
    {
        if (testItemSO != null)
            Initialize(testItemSO);

        var btn = GetComponent<Button>();
        if (btn != null)
            btn.onClick.AddListener(() => OnClicked?.Invoke(_runtimeData));
    }

    /// <summary>SO로 초기화.</summary>
    public void Initialize(ItemSO so)
    {
        _so = so;
        _runtimeData = RuntimeItemData.FromSO(so);
        ApplyUI(so.displayName, so.rarity.ToString(), so.icon, so.iconKey);
    }

    /// <summary>런타임 데이터로 초기화.</summary>
    public void Initialize(RuntimeItemData data)
    {
        _so = null;
        _runtimeData = data;
        ApplyUI(data.displayName, data.rarity.ToString(), data.icon, data.iconKey);
    }

    private void ApplyUI(string displayName, string rarity, Sprite icon, string iconKey)
    {
        if (nameText != null)
            nameText.text = displayName;

        if (rarityText != null)
            rarityText.text = rarity;

        if (icon != null && iconImage != null)
        {
            iconImage.sprite = icon;
            iconImage.color = Color.white;
        }
        else if (!string.IsNullOrEmpty(iconKey) && iconImage != null)
        {
            _ = LoadIconAsync(iconKey);
        }
        else if (iconImage != null)
        {
            iconImage.sprite = null;
            iconImage.color = Color.clear;
        }
    }

    private async UniTask LoadIconAsync(string iconKey)
    {
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
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ItemDisplay] 아이콘 로드 예외: {iconKey} / {e.Message}");
        }
    }

    private void OnDestroy()
    {
        if (_iconHandle.HasValue)
        {
            var h = _iconHandle.Value;
            if (h.IsValid()) Addressables.Release(h);
            _iconHandle = null;
        }
    }

    public ItemSO GetItemSO() => _so;
    public RuntimeItemData GetRuntimeData() => _runtimeData;
}
