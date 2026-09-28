#if UNITY_EDITOR
using System.Globalization;
using System.IO;
using System.Text;
using Cinemachine;
using UnityEngine;

/// <summary>
/// [임시 진단 · 2026-09-16] 카메라 위아래 흔들림 원인 계측기. <b>원인 규명 후 삭제할 것.</b>
///
/// 에디터 플레이 모드에서만 스스로 붙어(빌드에는 포함되지 않음) 매 프레임 아래 값을 기록하고,
/// 플레이 모드를 끝낼 때 {프로젝트}/Logs/camera_jitter.csv 로 쓴다(Assets 밖·gitignore 대상).
///
///   · 몸    : 물리 위치 Y(rb.position) · 보간 위치 Y(transform) · 수직/수평 속도 · 접지 · 로코 상태
///   · 지면  : 발밑 레이 거리 · 구 캐스트 거리 — 플로팅 서보(DefaultGroundingAbility)가 읽는 방식 그대로 잰다
///   · 카메라: 앵커 Y · 최종 카메라 Y/피치 · FreeLook Y축 값 · 중간 리그 높이 · FOV
///
/// 판독: 몸 Y가 떨리면 물리/서보 쪽, 몸은 평평한데 카메라 Y/피치가 떨리면 카메라 쪽.
/// 레이와 구 거리가 서로 다르게 움직이면 구가 주변 가장자리를 읽고 있는 것이다.
/// </summary>
[DefaultExecutionOrder(10000)]   // PlayerController(0)·CinemachineBrain(0) 뒤 — 확정된 값을 읽는다
public sealed class CameraJitterProbe : MonoBehaviour
{
    private const int   MaxRows      = 7200;   // 60fps 기준 약 2분. 넘치면 기록만 멈춘다.
    private const float FindInterval = 0.5f;

    private struct Row
    {
        public int   Frame, Steps, Loco;
        public bool  Grounded;
        public float Time, Dt, TimeScale;
        public float RbY, TrY, Vy, Vh;
        public float RayDist, SphereDist;
        public float AnchorY, CamY, CamPitch, YAxis, MidHeight, Fov;
    }

    // 매 프레임 할당을 피하려고 고정 버퍼에 담고, 문자열 변환은 종료 시 한 번만 한다.
    private readonly Row[] _rows = new Row[MaxRows];
    private int _count;

    private PlayerController    _player;
    private CapsuleCollider     _capsule;
    private CinemachineFreeLook _vcam;
    private Camera              _cam;
    private float               _nextFind;

    // 직전 렌더 프레임 이후의 물리 스텝 집계(FixedUpdate가 채우고 LateUpdate가 비운다)
    private int   _steps;
    private float _rayDist = -1f, _sphereDist = -1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        var go = new GameObject("[CameraJitterProbe]") { hideFlags = HideFlags.HideInHierarchy };
        DontDestroyOnLoad(go);
        go.AddComponent<CameraJitterProbe>();
        Debug.Log("[CameraJitterProbe] 계측 시작 — 플레이를 끝내면 Logs/camera_jitter.csv 로 저장된다");
    }

    private void FixedUpdate()
    {
        if (!Resolve()) return;
        _steps++;
        MeasureGround();
    }

    private void LateUpdate()
    {
        if (!Resolve() || _count >= MaxRows) { _steps = 0; return; }

        var rb = _player.Rigid;
        Vector3 v = rb != null ? rb.linearVelocity : Vector3.zero;
        Transform anchor = _vcam != null ? _vcam.Follow : null;
        Transform camTf  = _cam != null ? _cam.transform : null;

        ref Row r = ref _rows[_count++];
        r.Frame      = Time.frameCount;
        r.Time       = Time.unscaledTime;
        r.Dt         = Time.unscaledDeltaTime;
        r.TimeScale  = Time.timeScale;
        r.Steps      = _steps;
        r.RbY        = rb != null ? rb.position.y : 0f;
        r.TrY        = _player.transform.position.y;
        r.Vy         = v.y;
        r.Vh         = Mathf.Sqrt(v.x * v.x + v.z * v.z);
        r.Grounded   = _player.IsGrounded();
        r.Loco       = _player.LocoSM != null ? (int)_player.LocoSM.CurrentId : -1;
        r.RayDist    = _rayDist;
        r.SphereDist = _sphereDist;
        r.AnchorY    = anchor != null ? anchor.position.y : 0f;
        r.CamY       = camTf != null ? camTf.position.y : 0f;
        r.CamPitch   = camTf != null ? camTf.eulerAngles.x : 0f;
        r.YAxis      = _vcam != null ? _vcam.m_YAxis.Value : 0f;
        r.MidHeight  = _vcam != null && _vcam.m_Orbits != null && _vcam.m_Orbits.Length > 1 ? _vcam.m_Orbits[1].m_Height : 0f;
        r.Fov        = _vcam != null ? _vcam.m_Lens.FieldOfView : 0f;

        _steps = 0;
    }

    private void OnDestroy() => Flush();

    // 플레이어는 PlayerManager 등록값으로 찾는다(분신은 등록되지 않는다). 찾기 전까지만 주기적으로 확인.
    private bool Resolve()
    {
        if (_player != null) return true;
        if (Time.unscaledTime < _nextFind) return false;
        _nextFind = Time.unscaledTime + FindInterval;

        var tf = Managers.Player?.PlayerTransform;
        if (tf == null || !tf.TryGetComponent(out _player)) return false;

        _player.TryGetComponent(out _capsule);
        _vcam = _player.CinemachineCamera;
        _cam  = Camera.main;
        return true;
    }

    // DefaultGroundingAbility.UpdateFloatGroundCheck 와 같은 원점·길이·반경으로 잰다.
    private void MeasureGround()
    {
        _rayDist = _sphereDist = -1f;
        var cd = _player.CharacterData;
        if (cd == null) return;

        float ride = cd.floatRideHeight;
        Vector3 origin = _player.transform.position + Vector3.up * ride;
        float len = ride + Mathf.Max(cd.floatProbeExtra, cd.floatStepDownDistance);

        float radius = 0.2f;
        if (_capsule != null)
        {
            Vector3 s = _player.transform.lossyScale;
            radius = Mathf.Max(0.05f, _capsule.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z)) * 0.9f);
        }

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hr, len, cd.groundLayer, QueryTriggerInteraction.Ignore))
            _rayDist = hr.distance;
        if (Physics.SphereCast(origin + Vector3.up * radius, radius, Vector3.down, out RaycastHit hs, len,
                               cd.groundLayer, QueryTriggerInteraction.Ignore))
            _sphereDist = hs.distance;
    }

    private void Flush()
    {
        if (_count == 0) return;

        string dir  = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "Logs"));
        string path = Path.Combine(dir, "camera_jitter.csv");
        Directory.CreateDirectory(dir);

        CultureInfo inv = CultureInfo.InvariantCulture;
        var sb = new StringBuilder(_count * 180);
        sb.AppendLine("frame,t,dt,timeScale,steps,rbY,trY,vy,vh,grounded,loco,rayDist,sphereDist,anchorY,camY,camPitch,yAxis,midH,fov");
        for (int i = 0; i < _count; i++)
        {
            Row r = _rows[i];
            sb.Append(r.Frame).Append(',')
              .Append(r.Time.ToString("F4", inv)).Append(',')
              .Append(r.Dt.ToString("F4", inv)).Append(',')
              .Append(r.TimeScale.ToString("F2", inv)).Append(',')
              .Append(r.Steps).Append(',')
              .Append(r.RbY.ToString("F4", inv)).Append(',')
              .Append(r.TrY.ToString("F4", inv)).Append(',')
              .Append(r.Vy.ToString("F3", inv)).Append(',')
              .Append(r.Vh.ToString("F3", inv)).Append(',')
              .Append(r.Grounded ? 1 : 0).Append(',')
              .Append(r.Loco).Append(',')
              .Append(r.RayDist.ToString("F4", inv)).Append(',')
              .Append(r.SphereDist.ToString("F4", inv)).Append(',')
              .Append(r.AnchorY.ToString("F4", inv)).Append(',')
              .Append(r.CamY.ToString("F4", inv)).Append(',')
              .Append(r.CamPitch.ToString("F3", inv)).Append(',')
              .Append(r.YAxis.ToString("F3", inv)).Append(',')
              .Append(r.MidHeight.ToString("F3", inv)).Append(',')
              .Append(r.Fov.ToString("F2", inv))
              .AppendLine();
        }

        File.WriteAllText(path, sb.ToString());
        Debug.Log($"[CameraJitterProbe] {_count}행 저장 → {path}");
    }
}
#endif
