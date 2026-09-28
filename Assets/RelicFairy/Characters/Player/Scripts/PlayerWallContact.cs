using UnityEngine;

/// <summary>
/// 벽 파고듦 진동 차단 — 접촉 중인 벽 법선을 모아 이동 목표에서 '벽으로 파고드는 성분'을 제거한다.
/// PlayerController가 소유하고, OnCollisionStay가 <see cref="Collect"/>로 접촉을 넘긴다.
///
/// 벽에 몸을 붙인 채 이동 입력을 유지하면, 솔버는 매 물리 스텝 벽 방향 속도를 0으로 만드는데
/// Move()는 다음 프레임에 가속(moveAccel)으로 그 속도를 <b>다시</b> 만들어낸다. 그 사이 캡슐이
/// 1cm 남짓 파고들고 솔버가 되밀어내며 물리 주기(50Hz)로 진동한다 — 카메라가 이걸 그대로 비춘다.
/// 카메라 댐핑은 진폭만 깎을 뿐 진동원이 살아 있어 0으로 만들지 못한다.
///
/// 해법은 <b>못 가는 방향으로 애초에 명령하지 않는 것</b>이다. 접촉면 법선으로 이동 목표를 투영하면
/// 벽을 파고들 속도가 0이라 침투도, 되밀림도, 진동도 생기지 않는다.
/// </summary>
public sealed class PlayerWallContact
{
    // ── Constants ─────────────────────────────────────────────────
    /// <summary>이 값보다 법선 y가 크면 걸어 오를 수 있는 바닥·완경사 → 벽으로 치지 않는다(약 60° 기준).</summary>
    private const float WallNormalMaxY = 0.5f;
    /// <summary>
    /// 캡슐 바닥에서 이 높이 이하의 접촉은 벽으로 치지 않는다 — <b>계단 앞면이기 때문</b>.
    /// 계단 앞면도 법선 y가 0이라 그냥 두면 벽으로 잡혀 전진 속도가 0이 되고,
    /// 전진 속도로 발동하는 <see cref="DefaultMoveAbility.StepClimb"/>가 죽어 계단을 못 오른다.
    /// StepClimb의 StepMaxHeight(0.35m)에 여유를 더한 값이라 둘이 같이 움직여야 한다.
    /// </summary>
    private const float StepContactIgnoreHeight = 0.40f;
    private const int   MaxWallNormals = 8;

    // ── Static ────────────────────────────────────────────────────
    // 접촉점 조회 버퍼 — GetContacts(배열)는 힙 할당이 없다(Update 경로 할당 금지 규칙 준수).
    private static readonly ContactPoint[] ContactBuffer = new ContactPoint[16];

    // ── Private ───────────────────────────────────────────────────
    private readonly Vector3[] _wallNormals = new Vector3[MaxWallNormals];
    private int   _wallNormalCount;
    private float _wallNormalStamp = float.NegativeInfinity;

    // ── Public Methods ────────────────────────────────────────────
    /// <summary>
    /// 수평 이동 목표에서 접촉 중인 벽으로 <b>파고드는 성분만</b> 제거한다.
    /// 벽에서 멀어지는 성분과 벽을 따라 미끄러지는 성분은 그대로 두므로 주행감은 변하지 않는다.
    /// 접촉 정보가 최근 물리 스텝 것이 아니면(벽에서 떨어짐) 원본을 그대로 돌려준다.
    /// </summary>
    public Vector2 Clip(Vector2 target)
    {
        if (_wallNormalCount == 0) return target;
        // 충돌 콜백은 물리 스텝 뒤에 오므로 한 스텝 분은 유효하다. 그보다 낡으면 접촉이 끊긴 것.
        if (Time.fixedTime - _wallNormalStamp > Time.fixedDeltaTime * 1.5f) return target;

        for (int i = 0; i < _wallNormalCount; i++)
        {
            Vector2 n = new Vector2(_wallNormals[i].x, _wallNormals[i].z);
            float mag = n.magnitude;
            if (mag < 0.0001f) continue;
            n /= mag;

            float into = Vector2.Dot(target, n);
            if (into < 0f) target -= n * into;
        }
        return target;
    }

    /// <summary>OnCollisionStay에서 호출 — 이번 물리 스텝의 벽 접촉 법선을 모은다.</summary>
    /// <param name="bottomY">캡슐 바닥 높이(월드). 계단 앞면과 벽을 가르는 기준.</param>
    public void Collect(Collision collision, float bottomY)
    {
        // 물리 스텝이 바뀌었으면 이전 스텝 접촉을 버린다(같은 스텝의 여러 충돌은 누적).
        if (_wallNormalStamp != Time.fixedTime)
        {
            _wallNormalStamp = Time.fixedTime;
            _wallNormalCount = 0;
        }

        int count = collision.GetContacts(ContactBuffer);
        for (int i = 0; i < count && _wallNormalCount < MaxWallNormals; i++)
        {
            ref readonly ContactPoint c = ref ContactBuffer[i];
            if (c.normal.y > WallNormalMaxY) continue;                      // 바닥·완경사는 이동 가능한 면
            if (c.point.y - bottomY <= StepContactIgnoreHeight) continue;   // 계단 앞면 — StepClimb에 맡긴다
            _wallNormals[_wallNormalCount++] = c.normal;
        }
    }
}
