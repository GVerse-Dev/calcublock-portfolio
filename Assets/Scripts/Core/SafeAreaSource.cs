using UnityEngine;

/// <summary>
/// 세이프 에어리어의 단일 출처.
///
/// 안드로이드에서는 <c>Screen.safeArea</c>가 곧 정답이라 이 클래스는 그것을 그대로 돌려준다.
/// WebGL(앱인토스)에서는 다르다:
/// - 브라우저는 노치·펀치홀 인셋을 <c>Screen.safeArea</c>로 알려주지 않는다(화면 전체가 나온다).
/// - 그리고 정작 피해야 할 것은 기기 노치가 아니라 **토스가 그리는 우상단 X 버튼**이다.
///   그건 토스만 아는 좌표라 AIT API로 받아야 한다.
///
/// 그래서 인셋 출처를 한 겹 분리해 둔다. 소비처(<see cref="SafeAreaHandler"/>,
/// <see cref="SafeAreaCameraAligner"/>, <c>SafeAreaEdgeOffset</c>)는 이 값만 읽으면 된다.
/// 덮어쓴 값이 없으면 기존과 완전히 동일하게 동작한다.
/// </summary>
public static class SafeAreaSource
{
    private static Rect _override;
    private static bool _hasOverride;

    /// <summary>현재 세이프 에어리어(픽셀, 좌하단 원점). 덮어쓴 값이 없으면 <c>Screen.safeArea</c>.</summary>
    public static Rect Current => _hasOverride ? _override : Screen.safeArea;

    /// <summary>
    /// 플랫폼이 준 값으로 덮어쓴다.
    ///
    /// 별도의 알림은 없다. <see cref="ScreenChangeWatcher"/>가 이 값을 감시하므로
    /// 다음 프레임에 기존 구독자들이 그대로 갱신을 받는다 — 통지 경로를 둘로 두지 않는다.
    /// </summary>
    public static void SetOverride(Rect rect)
    {
        _override = rect;
        _hasOverride = true;
    }

    public static void ClearOverride()
    {
        _hasOverride = false;
    }
}
