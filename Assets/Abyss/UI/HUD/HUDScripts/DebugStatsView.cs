using UnityEngine;
using TMPro;

/// <summary>
/// PlayerRuntimeStats를 실시간으로 표시하는 디버그 UI.
/// @UIRoot의 Canvas_Overlay에 부착.
/// </summary>
public class DebugStatsView : MonoBehaviour
{
    [SerializeField] private TMP_Text statsText;

    private PlayerController _player;
    private PlayerRuntimeStats _stats;

    public void SetStatsText(TMP_Text text) => statsText = text;

    private void Update()
    {
        if (_stats == null)
        {
            TryBind();
            return;
        }

        if (statsText == null) return;

        var wm = _player?.WeaponManager;
        var wd = wm?.CurrentWeaponData;

        statsText.text =
$@"<b>=== Debug Stats ===</b>
<b>HP</b>: {_stats.Hp} / {_stats.MaxHp}
<b>Melee</b>: {_stats.MeleeAttack}  <b>Ranged</b>: {_stats.RangedAttack}
<b>Defense</b>: {_stats.Defense}  <b>Luck</b>: {_stats.Luck}
<b>AtkSpeed</b>: {_stats.AttackSpeedMultiplier:F2}x
<b>SkillCDR</b>: {_stats.SkillCooldownReduction:P0}
<b>ItemCDR</b>: {_stats.ActiveItemCooldownReduction:P0}
---
<b>Weapon</b>: {(wd != null ? wd.displayName : "None")}
<b>ATK</b>: {wd?.baseAttack ?? 0}  <b>DEF</b>: {wd?.baseDefense ?? 0}
<b>SPD</b>: {wd?.attackSpeed ?? 0}/s  <b>RNG</b>: {wd?.attackRange ?? 0}m
<b>Type</b>: {wd?.weaponType}
<b>Slot</b>: {wm?.CurrentSlotIndex ?? -1}";
    }

    private void TryBind()
    {
        if (_player != null) return;

        var pm = Managers.Player;
        if (pm?.PlayerTransform == null) return;

        _player = pm.PlayerTransform.GetComponent<PlayerController>();
        if (_player != null)
            _stats = _player.RuntimeStats;
    }
}
