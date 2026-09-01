using System.Collections.Generic;
using UnityEngine;
#if UNITY_WEBGL
using AppsInToss;
#endif

/// <summary>
/// 앱인토스 콘솔로 나가는 게임플레이 계측.
///
/// [Play 빌드에는 들어가지 않는다]
/// 모든 진입점에 <c>[Conditional("UNITY_WEBGL")]</c> 가 붙어 있다. 이 특성은 메서드 본문이
/// 아니라 **호출부를** 컴파일 단계에서 지운다. 따라서 Android 빌드에서는 아래 Note* 호출이
/// IL 에 아예 남지 않고, 게임 코드에 계측용 필드도 생기지 않는다(상태를 전부 이 클래스가 쥔다).
///
/// ⚠ Conditional 규약상 **인자 식도 평가되지 않는다.** 인자 자리에 부수효과가 있는 식을
///   절대 두지 말 것 — Android 에서 조용히 실행되지 않는다.
///
/// [왜 이름에 값을 굽는가]
/// 콘솔 MCP(event_log_search)는 로그별 **일자별 카운트**와 파라미터 **이름**만 돌려주고
/// 파라미터 **값의 분포는 주지 않는다.** 분포가 필요한 축(턴 수·튜토리얼 스텝)은 그래서
/// log_name 에 버킷으로 굽는다. 로그 이름은 개수 제한이 없고 이름마다 카운트가 나온다.
/// (AitAdProvider 가 ad_{stage}_{slot}_{os} 로 이름을 조합하는 것과 같은 이유다.)
///
/// [읽는 법]
/// - tut_step_NN     : 그 장에 도달한 수. 01 대비 감소분이 튜토리얼 이탈 곡선이다.
/// - tut_done        : 12장을 끝까지 보고 PLAY 를 누른 수.
/// - tut_close_NN    : 끝내지 않고 닫은 지점. 이탈이 어디서 몰리는지 직접 지목한다.
///
/// ⚠ 튜토리얼 지표의 단위는 **유저가 아니라 열람 1회**다. "?"·"How" 로 다시 열면 tut_step_01
///   이 또 올라간다. 곡선의 모양은 그대로지만 tut_done/tut_step_01 을 "유저 중 완주 비율"로
///   읽으면 틀린다 — "열람 중 완주 비율"이다. 유저 단위가 필요하면 콘솔이 자동으로 붙이는
///   anonymous_key 로 웹 콘솔에서 봐야 한다(MCP 로는 파라미터 값 분해가 안 나온다).
/// - first_clear_*   : 첫 소거까지 걸린 턴 수 분포.
/// - game_over_noclear : 한 번도 소거하지 못하고 끝난 판. 이 비율이 높으면 문제는
///                       온보딩 연출이 아니라 초반 난이도다.
/// </summary>
public static class Telemetry
{
    // ── 판 단위 상태 ─────────────────────────────────────────────────────────
    // 게임 클래스에 필드를 만들지 않으려고 여기에 둔다. Play 빌드에서는 Note* 호출부가
    // 전부 사라지므로 이 값들은 갱신되지 않고, 아무도 읽지 않는다.

    private static int  _placements;        // 이번 판의 배치 수
    private static bool _clearedThisGame;   // 이번 판에 한 번이라도 소거했는가

    /// <summary>
    /// 이 판을 **처음부터** 지켜봤는가. 이어하기로 복원된 판은 false 다.
    ///
    /// 복원된 판은 앱을 껐다 켠 것이라 이전 배치 수를 알 수 없다. 그대로 세면 첫 소거가
    /// 실제보다 훨씬 이른 턴에 일어난 것처럼 기록되어 분포가 통째로 왜곡된다.
    /// 측정할 수 없는 판은 세지 않는 편이 틀린 값을 넣는 것보다 낫다.
    /// </summary>
    private static bool _tracked;

    // ── 튜토리얼 상태 ────────────────────────────────────────────────────────
    // 한 번 연 동안 같은 장을 여러 번 세지 않는다(이전 버튼으로 되돌아갈 수 있다).
    // "도달"을 세야 이탈 곡선이 되지, "표시 횟수"를 세면 왕복이 섞여 곡선이 뭉개진다.

    private static readonly HashSet<int> _tutorialStepsSeen = new HashSet<int>();

    // ── 게임 ────────────────────────────────────────────────────────────────

    /// <summary>새 판 시작. 판 단위 상태를 되돌린다.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteGameStart()
    {
        _placements = 0;
        _clearedThisGame = false;
        _tracked = true;
        Send("game_start");
    }

    /// <summary>
    /// 이어하기로 판이 복원됐다. 이 판은 측정 대상에서 뺀다.
    /// <see cref="NoteGameStart"/> 뒤에 불린다 — 복원 성공 여부는 시도해 봐야 알 수 있어서다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteGameRestored()
    {
        _tracked = false;
    }

    /// <summary>
    /// 블록이 보드에 실제로 놓였다. 턴 경계.
    /// 부활(ReviveGame)은 보드만 비우고 같은 판이 이어지므로 여기서 리셋하지 않는다 —
    /// 부활을 새 판으로 세면 첫 소거 턴 수가 실제보다 짧게 잡힌다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NotePlacement()
    {
        _placements++;
    }

    /// <summary>
    /// 이번 배치로 소거가 일어났다. 판의 **첫 소거에서만** 한 번 보낸다.
    /// 몇 번째 턴이었는지가 이 계측의 전부라, 두 번째 소거부터는 값이 없다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteClear()
    {
        if (_clearedThisGame) return;
        _clearedThisGame = true;

        if (!_tracked) return;
        Send("first_clear_" + TurnBucket(_placements));
    }

    /// <summary>게임오버 확정.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteGameOver()
    {
        if (!_tracked) return;
        Send(_clearedThisGame ? "game_over_clear" : "game_over_noclear");
    }

    /// <summary>
    /// 게임오버 화면에서 "다시 하기"를 눌렀다 (일시정지 재시작은 세지 않는다).
    /// retry / (game_over_clear + game_over_noclear) 가 다시하기율 — 세션 안에서
    /// 재미가 있었는지를 보여주는 가장 직접적인 신호다. 낮으면 코어 재미 문제,
    /// 높은데 D1이 낮으면 재방문 훅 문제로 갈래가 갈린다.
    /// 복원된 판(_tracked=false)의 게임오버는 분모에 없으므로 여기서도 세지 않는다.
    /// </summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteRetry()
    {
        if (!_tracked) return;
        Send("retry");
    }

    /// <summary>
    /// 첫 소거까지의 턴 수를 버킷으로 접는다.
    /// 경계는 "몇 수 만에 감을 잡는가"를 가르려는 것이라, 초반을 촘촘하게 둔다.
    /// </summary>
    private static string TurnBucket(int placements)
    {
        if (placements <= 3)  return "t01_03";
        if (placements <= 7)  return "t04_07";
        if (placements <= 15) return "t08_15";
        return "t16up";
    }

    // ── 튜토리얼 ─────────────────────────────────────────────────────────────

    /// <summary>튜토리얼을 처음부터 열었다.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteTutorialOpen()
    {
        _tutorialStepsSeen.Clear();
    }

    /// <summary>스텝이 화면에 표시됐다. step 은 0-base.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteTutorialStep(int step)
    {
        if (!_tutorialStepsSeen.Add(step)) return;   // 왕복은 세지 않는다

        Send("tut_step_" + TwoDigit(step + 1));
    }

    /// <summary>끝까지 보고 PLAY 를 눌렀다.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteTutorialDone()
    {
        Send("tut_done");
    }

    /// <summary>끝내지 않고 닫았다. step 은 닫은 시점의 0-base 스텝.</summary>
    [System.Diagnostics.Conditional("UNITY_WEBGL")]
    public static void NoteTutorialClosed(int step)
    {
        Send("tut_close_" + TwoDigit(step + 1));
    }

    /// <summary>로그 이름이 사전순으로 정렬되도록 두 자리로 맞춘다(step_9 가 step_10 뒤로 가는 것을 막는다).</summary>
    private static string TwoDigit(int n) => n < 10 ? "0" + n : n.ToString();

    // ── 전송 ─────────────────────────────────────────────────────────────────

#if UNITY_WEBGL
    /// <summary>
    /// 전송은 기다리지 않는다. 계측이 게임 흐름을 늦추거나 막으면 본말전도다.
    /// (AitAdProvider.SendTrack 과 같은 규약 — async void 지만 본문 전체가 try 안이라
    ///  예외가 밖으로 새지 않는다. 게임오버 경로에서 부르므로 이게 특히 중요하다.)
    /// </summary>
    private static async void Send(string logName)
    {
        try
        {
            await AIT.AnalyticsImpression(new Dictionary<string, object>
            {
                ["log_name"] = logName,
            });
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[Telemetry] {logName} 전송 실패: {e.Message}");
        }
    }
#else
    // Play(Android) 빌드에도 Note* 본문은 컴파일된다 — 사라지는 것은 호출부다.
    // 그러니 이 자리에 AIT 를 부르지 않는 빈 구현을 둬야 컴파일이 성립한다.
    private static void Send(string logName) { }
#endif
}
