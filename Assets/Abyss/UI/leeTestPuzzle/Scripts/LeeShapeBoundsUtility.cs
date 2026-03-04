using UnityEngine;

/// <summary>
/// Small helper so we can compute shape bounds without modifying the SO file structure.
/// </summary>
public static class LeeShapeBoundsUtility
{
    public static RectInt GetBoundsInCells(LeeShapeAssetSO asset)
    {
        if (asset == null || asset.cellOffsets == null || asset.cellOffsets.Length == 0)
            return new RectInt(0, 0, 1, 1);

        int minX = asset.cellOffsets[0].x, maxX = asset.cellOffsets[0].x;
        int minY = asset.cellOffsets[0].y, maxY = asset.cellOffsets[0].y;

        foreach (var p in asset.cellOffsets)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minY) minY = p.y;
            if (p.y > maxY) maxY = p.y;
        }

        return new RectInt(minX, minY, (maxX - minX + 1), (maxY - minY + 1));
    }
}
