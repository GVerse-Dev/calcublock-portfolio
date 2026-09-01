#if UNITY_WEBGL
using System;
using AppsInToss;
using UnityEngine;

/// <summary>
/// 토스가 알려주는 세이프 에어리어 인셋을 <see cref="SafeAreaSource"/>에 넣어 준다.
///
/// 브라우저의 <c>Screen.safeArea</c>는 화면 전체라 노치도, **토스가 그리는 우상단 X 버튼**도
/// 반영되지 않는다. X 버튼은 인셋 상단 기준으로 그려지므로, 인셋을 제대로 받아야
/// HUD가 그 아래에서 시작한다.
///
/// 단위 변환에 주의한다. AIT가 주는 인셋은 **CSS 픽셀**이고 Unity의 <c>Screen.width/height</c>는
/// 캔버스의 실제 픽셀이다. 둘의 비가 devicePixelRatio이며 SDK가 그 값을 동기로 제공한다.
/// 변환을 빼먹으면 고해상도 기기에서 인셋이 절반 이하로 들어가 X 버튼과 겹친다.
/// </summary>
public static class AitSafeAreaProvider
{
    private const int TIMEOUT_MS = 10_000;

    private static Action _unsubscribe;
    private static bool _initialized;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        Fetch();
        Subscribe();

        // 화면 크기가 바뀌면 픽셀 환산값도 달라진다. 인셋(CSS px)은 그대로여도
        // Unity 픽셀 기준 Rect는 다시 만들어야 한다.
        ScreenChangeWatcher.EnsureRunning();
        ScreenChangeWatcher.OnChanged += Recompute;
    }

    private static SafeAreaInsets _lastInsets;

    /// <summary>화면 크기가 바뀌었을 때 같은 인셋을 새 픽셀 기준으로 다시 환산한다.</summary>
    private static void Recompute()
    {
        if (_lastInsets != null) Apply(_lastInsets);
    }

    private static async void Fetch()
    {
        try
        {
            var insets = await AIT.SafeAreaInsetsGet(TIMEOUT_MS);
            Apply(insets);
        }
        catch (Exception e)
        {
            // 실패하면 Screen.safeArea 로 남는다 — 화면 전체라 잘리지는 않고,
            // X 버튼과 겹칠 여지만 남는다. 게임을 막을 이유는 없다.
            Debug.LogWarning($"[SafeArea] 인셋 조회 실패 — 기본값을 씁니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    private static async void Subscribe()
    {
        try
        {
            _unsubscribe = await AIT.SafeAreaInsetsSubscribe(
                new SafeAreaInsetsSubscribe__0 { OnEvent = Apply },
                TIMEOUT_MS);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SafeArea] 인셋 구독 실패 — 초기값만 사용합니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    private static void Apply(SafeAreaInsets insets)
    {
        if (insets == null) return;

        if (!string.IsNullOrEmpty(insets.error))
        {
            Debug.LogWarning($"[SafeArea] 인셋 오류: {insets.error}");
            return;
        }

        if (Screen.width <= 0 || Screen.height <= 0) return;   // 씬 전환·종료 프레임

        _lastInsets = insets;

        // CSS 픽셀 → Unity 픽셀
        double dpr = AIT.GetDevicePixelRatio();
        if (!(dpr > 0)) dpr = 1.0;   // NaN·0 방어

        float left   = (float)(insets.Left   * dpr);
        float right  = (float)(insets.Right  * dpr);
        float top    = (float)(insets.Top    * dpr);
        float bottom = (float)(insets.Bottom * dpr);

        // 값이 이상하면(합이 화면을 넘음) 통째로 버린다. 폭·높이가 음수인 Rect를
        // 넘기면 소비처의 앵커 계산이 화면 밖으로 나간다.
        if (left + right >= Screen.width || top + bottom >= Screen.height)
        {
            Debug.LogWarning($"[SafeArea] 인셋이 화면보다 큽니다 — 무시합니다 " +
                             $"(l{left} r{right} t{top} b{bottom} / {Screen.width}x{Screen.height})");
            return;
        }

        // Unity의 세이프 에어리어는 좌하단 원점이다. 위쪽 인셋은 height에서 뺀다.
        var rect = new Rect(
            Mathf.Max(0f, left),
            Mathf.Max(0f, bottom),
            Screen.width  - Mathf.Max(0f, left) - Mathf.Max(0f, right),
            Screen.height - Mathf.Max(0f, top)  - Mathf.Max(0f, bottom));

        SafeAreaSource.SetOverride(rect);
    }
}
#endif
