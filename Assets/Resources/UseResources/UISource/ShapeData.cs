using System.Collections;
using System.Collections.Generic;
using UnityEngine;

// ScriptableObject를 생성할 수 있는 메뉴 추가
[CreateAssetMenu]
[System.Serializable]
public class ShapeData : ScriptableObject
{
    // 행 데이터를 표현하는 클래스
    [System.Serializable]
    public class Row
    {
        public bool[] column; // 열 데이터 (각 칸이 true/false로 활성화 여부를 나타냄)
        private int _size;    // 열의 크기

        // 기본 생성자
        public Row() {}

        // 열의 크기를 설정하는 생성자
        public Row(int size)
        {
            CreateRow(size); // 행 데이터를 초기화
        }

        // 행 데이터 배열(column)을 생성하고 초기화
        public void CreateRow(int size)
        {
            _size = size;
            column = new bool[_size]; // 열의 크기만큼 bool 배열 생성
            ClearRow(); // 생성된 배열을 초기화
        }

        // 행 데이터를 초기화 (모든 값을 false로 설정)
        public void ClearRow()
        {
            for (int i = 0; i < _size; i++)
            {
                column[i] = false; // 각 칸을 비활성화(false) 상태로 초기화
            }
        }
    }

    public int columns = 0; // 보드의 열 개수
    public int rows = 0;    // 보드의 행 개수
    public Row[] board;     // 2D 보드를 행(Row) 단위로 저장

    // 보드의 모든 데이터를 초기화 (모든 칸을 false로 설정)
    public void Clear()
    {
        for (var i = 0; i < rows; i++)
        {
            board[i].ClearRow(); // 각 행(Row)을 초기화
        }
    }

    // 새로운 보드를 생성
    public void CreateNewBoard()
    {
        board = new Row[rows]; // 보드 배열을 행(Row) 개수만큼 생성

        for (var i = 0; i < rows; i++)
        {
            board[i] = new Row(columns); // 각 행(Row)을 생성하고 열(columns) 크기를 설정
        }
    }
}
