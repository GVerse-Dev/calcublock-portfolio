using System;
using UnityEngine;

/// <summary>
/// 화면 크기·세이프에어리어 변화를 감지해 구독자에게 알린다.
///
/// 안드로이드에서는 세션 중 화면 크기가 바뀌지 않아(세로 고정) 필요가 없었지만,
/// WebGL/앱인토스 웹뷰는 다르다. 창 리사이즈, 로딩 오버레이 제거, 키보드 노출,
/// 세이프에어리어 확정 시점에 뷰포트가 바뀐다. 레이아웃 계산이 Awake/Start에서
/// 한 번만 돌면 그 뒤로 어긋난 채 남는다 (2026-07-31 브라우저에서 재현:
/// 보드가 화면 밖으로 잘리고 트레이 블록이 슬롯에서 이탈).
///
/// 변화가 없으면 이벤트를 발생시키지 않으므로, 화면이 고정된 플랫폼에서는
/// 동작이 완전히 동일하다.
/// </summary>
public static class ScreenChangeWatcher
{
    /// <summary>화면 크기 또는 세이프에어리어가 실제로 바뀐 프레임에만 발생한다.</summary>
    public static event Action OnChanged;

    private static int  _lastWidth;
    private static int  _lastHeight;
    private static Rect _lastSafeArea;
    private static bool _running;

    /// <summary>
    /// 감시를 시작한다. 구독 전에 호출할 것. 여러 번 불러도 안전하다.
    /// </summary>
    public static void EnsureRunning()
    {
        if (_running) return;
        _running = true;

        Capture();

        var go = new GameObject("[ScreenChangeWatcher]")
        {
            hideFlags = HideFlags.HideAndDontSave,
        };
        UnityEngine.Object.DontDestroyOnLoad(go);
        go.AddComponent<Runner>();
    }

    private static void Capture()
    {
        _lastWidth    = Screen.width;
        _lastHeight   = Screen.height;

        // Screen.safeArea 가 아니라 SafeAreaSource 를 본다. 앱인토스에서는 인셋이
        // 토스에서 비동기로 오므로 Screen.safeArea 는 끝까지 화면 전체로 남는다.
        // 여기서 출처를 바꿔 두면 기존 구독자들이 코드 변경 없이 그 갱신을 함께 받는다.
        _lastSafeArea = SafeAreaSource.Current;
    }

    private static void Poll()
    {
        // 씬 전환·종료 중에는 0이 올 수 있다. 그 값으로 레이아웃을 다시 잡으면
        // 0으로 나누거나 화면이 무너진다.
        if (Screen.width <= 0 || Screen.height <= 0) return;

        if (Screen.width == _lastWidth &&
            Screen.height == _lastHeight &&
            SafeAreaSource.Current == _lastSafeArea)
            return;

        Capture();
        Notify();
    }

    /// <summary>
    /// 구독자 하나가 던져도 나머지가 실행되어야 한다. 레이아웃 갱신은 서로 독립적인데
    /// 멀티캐스트를 통째로 Invoke하면 예외 이후 구독자가 통째로 누락돼
    /// 화면 일부만 갱신된 상태로 남는다.
    /// </summary>
    private static void Notify()
    {
        var handlers = OnChanged;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((Action)handler).Invoke();
            }
            catch (Exception e)
            {
                Debug.LogWarning(
                    $"[ScreenChangeWatcher] 구독자 예외 ({handler.Method.Name}): {e.Message}");
            }
        }
    }

    private sealed class Runner : MonoBehaviour
    {
        private void Update() => Poll();
    }
}
