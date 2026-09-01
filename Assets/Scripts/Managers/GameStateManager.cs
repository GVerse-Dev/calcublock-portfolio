using UnityEngine;

public enum GameState
{
    MainMenu,
    Playing,
    Paused,
    GameOver
}

public class GameStateManager : ManagerBase<GameStateManager>
{
    public GameState CurrentState { get; private set; }
    public event System.Action<GameState> OnGameStateChanged;
    public event System.Action OnRestartRequested;
    public event System.Action OnReviveRequested;

    /// <summary>
    /// 이번 판에서 부활이 가능한지. IGGameController가 게임오버 확정 시 설정한다.
    /// GameOverView는 이 값을 읽어 버튼 표시 여부를 결정한다.
    /// </summary>
    public bool CanRevive { get; private set; }

    /// <summary>IGGameController 전용. 부활 가능 여부를 설정한다.</summary>
    public void SetReviveAvailable(bool available) => CanRevive = available;

    public void SetGameState(GameState newState)
    {
        if (CurrentState == newState) return;

        CurrentState = newState;
        Time.timeScale = newState == GameState.Paused ? 0f : 1f;
        NotifyGameStateChanged(CurrentState);
    }

    /// <summary>
    /// 상태 변경을 구독자에게 알린다. **구독자별로 예외를 격리한다.**
    ///
    /// 그냥 Invoke 하면 멀티캐스트 델리게이트라 한 구독자가 던지는 순간
    /// (1) 뒤에 등록된 구독자가 아예 호출되지 않고
    /// (2) 예외가 호출자(IGGameController.CheckGameOver)까지 거슬러 올라가 그 뒤 코드가 취소된다.
    ///
    /// 2026-07-30에 실제로 터졌다: 파괴된 HUDView 구독이 남아 있어 StartCoroutine 이
    /// ArgumentNullException 을 던졌고, 그 결과 **살아 있는 HUDView 가 호출되지 않아 게임오버
    /// 패널이 뜨지 않았으며**, CheckGameOver 의 점수 저장·연출·광고까지 전부 실행되지 않았다.
    /// 게임오버 상태인데 패널이 없는 = 복구 불가 상태가 됐다.
    ///
    /// 상태 전이는 게임 흐름의 근간이므로 **어떤 구독자도 전체를 망가뜨릴 수 없어야 한다.**
    /// 범인을 찾을 수 있도록 메서드 이름과 대상까지 로그에 남긴다.
    /// </summary>
    private void NotifyGameStateChanged(GameState state)
    {
        var handlers = OnGameStateChanged;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((System.Action<GameState>)handler).Invoke(state);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[GameState] 구독자 예외 ({handler.Method.DeclaringType?.Name}.{handler.Method.Name}): " +
                    $"{e.GetType().Name}: {e.Message}");
            }
        }
    }

    /// <summary>Playing ↔ Paused 토글. 두 상태 외에는 무시한다.</summary>
    public void TogglePause()
    {
        if (CurrentState == GameState.Playing)
            SetGameState(GameState.Paused);
        else if (CurrentState == GameState.Paused)
            SetGameState(GameState.Playing);
    }

    /// <summary>재시작 요청. GameOverView → IGGameController.RestartGame() 간 직접 참조 없이 연결.</summary>
    public void RequestRestart() => NotifyRequest(OnRestartRequested, nameof(OnRestartRequested));

    /// <summary>
    /// 부활 요청. GameOverView → IGGameController.ReviveGame() 간 직접 참조 없이 연결.
    ///
    /// **여기서 예외가 새면 사용자는 광고를 보고도 부활하지 못한다**(수익 직결).
    /// 그래서 다른 요청 이벤트와 함께 구독자별로 격리한다.
    /// </summary>
    public void RequestRevive() => NotifyRequest(OnReviveRequested, nameof(OnReviveRequested));

    /// <summary>
    /// 인자 없는 요청 이벤트를 구독자별로 끊어 발화한다.
    ///
    /// NotifyGameStateChanged 와 같은 이유다 — 게임 흐름을 여는 이벤트에서 한 구독자의
    /// 예외가 뒤 구독자와 호출자를 함께 죽이면 복구 경로가 없는 상태가 만들어진다.
    /// (2026-07-30 게임오버 소프트락 사고 참고)
    /// </summary>
    private static void NotifyRequest(System.Action handlers, string eventName)
    {
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((System.Action)handler).Invoke();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning(
                    $"[GameState] {eventName} 구독자 예외 " +
                    $"({handler.Method.DeclaringType?.Name}.{handler.Method.Name}): {e.GetType().Name}: {e.Message}");
            }
        }
    }

    public event System.Action OnMainMenuRequested;
    public event System.Action OnForfeitRequested;

    /// <summary>메인 메뉴로 이동 요청. timeScale 복원 후 OnMainMenuRequested 발화 → 씬 전환은 구독자가 처리.</summary>
    public void RequestGoToMainMenu()
    {
        SetGameState(GameState.MainMenu);
        NotifyRequest(OnMainMenuRequested, nameof(OnMainMenuRequested));
    }

    /// <summary>
    /// 플레이 도중 홈으로 나가기 요청 — 판을 포기하는 것으로 간주한다.
    /// IGGameController가 점수 확정·세션 정리 후 RequestGoToMainMenu를 호출한다.
    /// </summary>
    public void RequestForfeit() => NotifyRequest(OnForfeitRequested, nameof(OnForfeitRequested));


    public override void InitializeManager() { }
    public override void ClearManager() { }
    public override void FinalizeManager() { }
}
