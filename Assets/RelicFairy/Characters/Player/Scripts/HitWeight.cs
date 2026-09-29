/// <summary>
/// 플레이어가 받는 공격의 연출 등급(약·중·강). 공격 쪽이 정하지 않으면 Auto —
/// 플레이어가 최종 피해/최대 HP로 판정한다(<see cref="PlayerController.TakeDamage"/>).
/// </summary>
public enum HitWeight { Auto, Light, Medium, Heavy }
