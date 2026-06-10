using UnityEngine;

namespace RelicFairy.Monster
{
    internal static class DragonPatternFloorUtils
    {
        private const float SampleHeight = 50f;
        private const float SampleDepth  = 100f;

        /// <summary>
        /// xzPos 위치 바로 위에서 Raycast를 내려 실제 바닥 Y를 반환한다.
        /// 충돌이 없으면 fallbackY를 반환한다.
        /// </summary>
        internal static float GetFloorY(Vector3 xzPos, float fallbackY)
        {
            var origin = new Vector3(xzPos.x, fallbackY + SampleHeight, xzPos.z);
            return Physics.Raycast(origin, Vector3.down, out RaycastHit hit, SampleDepth)
                ? hit.point.y
                : fallbackY;
        }
    }
}
