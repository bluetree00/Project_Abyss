using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BKGroundCircle 오브젝트 풀.
/// 경고 원은 타이머 만료 시 자동으로 풀에 반납된다.
/// </summary>
public class BKGroundCirclePool
{
    private readonly Transform                _container;
    private readonly Queue<BKGroundCircle>    _idle   = new();
    private readonly HashSet<BKGroundCircle>  _active = new();

    public BKGroundCirclePool(int initialSize, Transform container = null)
    {
        _container = container;
        for (int i = 0; i < initialSize; i++)
            Enqueue(CreateNew());
    }

    // ── 풀에서 꺼내기 ─────────────────────────────────────
    public BKGroundCircle Get(Vector3 pos, float radius, float duration)
    {
        var circle = _idle.Count > 0 ? _idle.Dequeue() : CreateNew();
        circle.ResetForPool(pos, radius, duration);
        _active.Add(circle);
        return circle;
    }

    // ── 풀에 반납 ─────────────────────────────────────────
    public void Return(BKGroundCircle circle)
    {
        if (circle == null || !_active.Remove(circle)) return;
        Enqueue(circle);
    }

    // ── 전체 강제 반납 ────────────────────────────────────
    public void RecycleAll()
    {
        foreach (var c in _active)
            if (c != null) Enqueue(c);
        _active.Clear();
    }

    // ── 풀 해제 ───────────────────────────────────────────
    public void Dispose()
    {
        _active.Clear();
        while (_idle.Count > 0)
        {
            var c = _idle.Dequeue();
            if (c != null) Object.Destroy(c.gameObject);
        }
    }

    // ─────────────────────────────────────────────────────
    private BKGroundCircle CreateNew()
    {
        var go = new GameObject("[DropWarning]");
        if (_container != null) go.transform.SetParent(_container, false);
        // AddComponent → Awake 즉시 실행 → LineRenderer + MeshRenderer 셋업
        var circle = go.AddComponent<BKGroundCircle>();
        circle.Pool = this;
        go.SetActive(false);
        return circle;
    }

    private void Enqueue(BKGroundCircle circle)
    {
        if (circle == null) return;
        circle.gameObject.SetActive(false);
        _idle.Enqueue(circle);
    }
}
