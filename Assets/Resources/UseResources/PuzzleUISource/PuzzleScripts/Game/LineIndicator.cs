using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class LineIndicator : MonoBehaviour
{
    // 기존의 square_data 배열
    public int[,] square_data = new int[8, 8]
    {
        {0, 1, 2, 3, 4, 5, 6, 7},
        {8, 9, 10, 11, 12, 13, 14, 15},
        {16, 17, 18, 19, 20, 21, 22, 23},
        {24, 25, 26, 27, 28, 29, 30, 31},
        {32, 33, 34, 35, 36, 37, 38, 39},
        {40, 41, 42, 43, 44, 45, 46, 47},
        {48, 49, 50, 51, 52, 53, 54, 55},
        {56, 57, 58, 59, 60, 61, 62, 63}
    };

    // 사과 모양을 정의한 배열 (1이면 사과, 0이면 일반 칸)
    private int[,] appleShape = new int[8, 8]
    {
        {0, 0, 0, 1, 1, 0, 0, 0},
        {0, 0, 1, 1, 1, 1, 0, 0},
        {0, 1, 1, 1, 1, 1, 1, 0},
        {0, 1, 1, 1, 1, 1, 1, 0},
        {0, 0, 1, 1, 1, 1, 0, 0},
        {0, 0, 0, 1, 1, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0},
        {0, 0, 0, 0, 0, 0, 0, 0}
    };

    // 사과 모양에 해당하는지 확인하는 함수
    public bool IsAppleShape(int row, int col)
    {
        return appleShape[row, col] == 1;
    }

    // 기존의 GetGridSquareIndex 함수는 그대로 두기
    public int GetGridSquareIndex(int square)
    {
        for (int row = 0; row < 8; row++)
        {
            for (int col = 0; col < 8; col++)
            {
                if (square_data[row, col] == square)
                {
                    return row;
                }
            }
        }

        return -1; // 찾지 못하면 -1 반환
    }
}
