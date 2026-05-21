using UnityEngine;

[CreateAssetMenu(menuName = "PuzzleGrid/Grid/Grid Pattern SO")]
public class GridPatternSO : ScriptableObject
{
    [Min(1)] public int rows = 8;
    [Min(1)] public int columns = 8;

    [Tooltip("Each string is a row. Use '1' for placeable, '0' for blocked. Length should equal columns.")]
    public string[] rows01;

    public bool IsPlaceable(int r, int c)
    {
        if (rows01 == null || r < 0 || r >= rows01.Length) return false;
        var line = rows01[r];
        if (string.IsNullOrEmpty(line) || c < 0 || c >= line.Length) return false;
        return line[c] == '1';
    }
}
