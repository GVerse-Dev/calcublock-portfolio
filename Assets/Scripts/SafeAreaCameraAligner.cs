using UnityEngine;

/// <summary>
/// 월드 공간 콘텐츠(보드/블록 타일)를 세이프 에어리어 중심에 정렬한다.
///
/// UI는 SafeAreaHandler가 캔버스 앵커로 세이프 에어리어를 따르지만,
/// 카메라가 렌더링하는 월드 오브젝트는 물리 화면 전체 기준으로 그려져
/// 펀치홀 인셋만큼 UI와 어긋난다. 카메라를 인셋의 절반만큼 반대로 옮겨
/// 월드 콘텐츠의 화면상 중심을 세이프 에어리어 중심과 일치시킨다.
///
/// 입력(ScreenToWorldPoint)은 같은 카메라를 쓰므로 별도 보정이 필요 없다.
/// </summary>
[RequireComponent(typeof(Camera))]
public class SafeAreaCameraAligner : MonoBehaviour
{
    private Camera _camera;
    private Vector3 _basePosition;

    private void Awake()
    {
        _camera = GetComponent<Camera>();
        _basePosition = transform.position;
    }

    /// <summary>
    /// SetupCamera처럼 카메라 위치를 직접 세팅하는 코드 뒤에 호출해
    /// 그 위치를 새 기준점으로 삼는다.
    /// </summary>
    public void RebaseToCurrentPosition()
    {
        _basePosition = transform.position;
        Apply();
    }

    // 세이프 에어리어·orthographicSize가 초기화 순서에 따라 나중에 정해질 수 있어
    // 매 프레임 재계산한다. (연산량은 무시할 수준)
    private void LateUpdate() => Apply();

    private void Apply()
    {
        if (!_camera.orthographic || Screen.height == 0) return;

        Rect safe = SafeAreaSource.Current;
        float worldPerPixel = (_camera.orthographicSize * 2f) / Screen.height;

        // 세이프 에어리어 중심이 화면 중심에서 벗어난 양 (픽셀, 좌하단 원점)
        float offsetX = (safe.center.x - Screen.width * 0.5f) * worldPerPixel;
        float offsetY = (safe.center.y - Screen.height * 0.5f) * worldPerPixel;

        // 콘텐츠를 세이프 에어리어 중심 쪽으로 보내려면 카메라는 반대로 이동
        transform.position = _basePosition - new Vector3(offsetX, offsetY, 0f);
    }
}
