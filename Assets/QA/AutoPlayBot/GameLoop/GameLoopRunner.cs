#if IG_GAMELOOP_BUILD
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using IGMain;
using IGQA.AutoPlayBot.Metrics;
using IGQA.AutoPlayBot.Strategies;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace IGQA.AutoPlayBot
{
    /// <summary>
    /// Firebase Game Loop Test 부트스트랩.
    ///
    /// Android intent action "com.google.intent.action.TEST_LOOP" 감지 시 자동 실행된다.
    /// 일반 앱 실행(intent 없음)에서는 아무것도 하지 않고 즉시 리턴한다.
    ///
    /// 로컬 검증:
    ///   adb shell am start -a com.google.intent.action.TEST_LOOP \
    ///     -e scenario 1 -n com.GVerseDev.Calculationtetris/com.unity3d.player.UnityPlayerActivity
    /// </summary>
    public sealed class GameLoopRunner : MonoBehaviour
    {
        private const string TitleSceneName = "TitleScene";
        private const string IGSceneName = "IGScene";
        private const int DefaultMaxGames = 20;
        private const float DefaultMaxDurationS = 1500f;
        private const int Scenario2MaxGames = 100;
        private const float Scenario2MaxDurationS = 3600f;

        // AfterSceneLoad는 씬 로드마다 발화하므로 최초 1회만 처리
        private static bool _bootstrapped;

        // ── 진입점 ────────────────────────────────────────────────────────────

        // BeforeSceneLoad 대신 AfterSceneLoad 사용:
        // BeforeSceneLoad 시점에는 Android JNI 브리지가 준비되지 않아
        // AndroidJavaClass 호출이 실패할 수 있다.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void TryBootstrap()
        {
            if (_bootstrapped) return;
            _bootstrapped = true;

            Debug.Log("[GameLoopRunner] TryBootstrap called");

            if (!IsGameLoopIntent())
            {
                Debug.Log("[GameLoopRunner] Not a TEST_LOOP intent — normal execution.");
                return;
            }

            var go = new GameObject("[GameLoopRunner]");
            DontDestroyOnLoad(go);
            go.AddComponent<GameLoopRunner>();
            Debug.Log("[GameLoopRunner] Runner bootstrapped.");
        }

        // ── 라이프사이클 ──────────────────────────────────────────────────────

        private void Start() => StartCoroutine(RunSession());

        // ── 세션 코루틴 ───────────────────────────────────────────────────────

        private IEnumerator RunSession()
        {
            int scenario = ReadScenario();
            string logPath = ReadLogFilePath();

            Debug.Log($"[GameLoopRunner] RunSession start — scenario={scenario}  logPath={logPath ?? "(none)"}");

            // TitleScene이 이미 로드된 경우 재로드 생략
            if (SceneManager.GetActiveScene().name != TitleSceneName)
            {
                yield return SceneManager.LoadSceneAsync(TitleSceneName);
                for (int i = 0; i < 5; i++) yield return null;
            }

            yield return SceneManager.LoadSceneAsync(IGSceneName);
            for (int i = 0; i < 5; i++) yield return null;

            var gameController = FindAnyObjectByType<IGGameController>(FindObjectsInactive.Include);
            if (gameController == null)
            {
                Debug.LogError("[GameLoopRunner] IGGameController not found — aborting.");
                Application.Quit(1);
                yield break;
            }

            Debug.Log("[GameLoopRunner] IGGameController found. Starting bot.");

            var strategy = SelectStrategy(scenario);
            var bot = new AutoPlayBot(gameController, strategy, seed: scenario * 1009);
            var reports = new List<BotSessionReport>();

            int maxGames = scenario == 2 ? Scenario2MaxGames : DefaultMaxGames;
            float maxDuration = scenario == 2 ? Scenario2MaxDurationS : DefaultMaxDurationS;

            List<float> postGcMbPerGame = scenario == 2 ? new List<float>() : null;
            Func<IEnumerator> afterEachGame = postGcMbPerGame != null
                ? () => MeasurePostGc(postGcMbPerGame)
                : null;

            yield return bot.PlayRealtime(
                maxGames: maxGames,
                maxDurationSeconds: maxDuration,
                afterEachGame: afterEachGame,
                onGameComplete: report =>
                {
                    reports.Add(report);
                    Debug.Log($"[GameLoopRunner] Game {reports.Count}: {report.ToSummary()}");
                });

            string json = GameLoopResult.Serialize(reports, scenario, postGcMbPerGame);
            WriteResult(logPath, json, scenario);

            Debug.Log($"[GameLoopRunner] Done — {reports.Count} games completed. Quitting.");
            Application.Quit(0);
        }

        // ── Android intent 파싱 ───────────────────────────────────────────────

        private static bool IsGameLoopIntent()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass  player   = null;
            AndroidJavaObject activity = null;
            AndroidJavaObject intent   = null;
            try
            {
                player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                intent   = activity.Call<AndroidJavaObject>("getIntent");
                string action = intent.Call<string>("getAction");

                // 실제 수신된 action을 로그로 남겨 진단에 활용
                Debug.Log($"[GameLoopRunner] Received intent action: '{action}'");

                return action == "com.google.intent.action.TEST_LOOP";
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLoopRunner] Intent check failed: {e}");
                return false;
            }
            finally
            {
                intent?.Dispose();
                activity?.Dispose();
                player?.Dispose();
            }
#else
            return false;
#endif
        }

        private static int ReadScenario()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass  player   = null;
            AndroidJavaObject activity = null;
            AndroidJavaObject intent   = null;
            try
            {
                player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                intent   = activity.Call<AndroidJavaObject>("getIntent");

                // adb -e 는 문자열 extra, Firebase/-ei 는 정수 extra — 둘 다 처리
                string raw = intent.Call<string>("getStringExtra", "scenario");
                if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out int parsed))
                    return parsed;
                return intent.Call<int>("getIntExtra", "scenario", 1);
            }
            catch { return 1; }
            finally
            {
                intent?.Dispose();
                activity?.Dispose();
                player?.Dispose();
            }
#else
            return 1;
#endif
        }

        private static string ReadLogFilePath()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            AndroidJavaClass  player   = null;
            AndroidJavaObject activity = null;
            AndroidJavaObject intent   = null;
            AndroidJavaObject data     = null;
            try
            {
                player   = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity = player.GetStatic<AndroidJavaObject>("currentActivity");
                intent   = activity.Call<AndroidJavaObject>("getIntent");
                data     = intent.Call<AndroidJavaObject>("getData");
                // getPath()는 content:// URI에서 경로 구성요소만 추출 — content:// scheme을 포함한 전체 URI 보존
                return data?.Call<string>("toString");
            }
            catch { return null; }
            finally
            {
                data?.Dispose();
                intent?.Dispose();
                activity?.Dispose();
                player?.Dispose();
            }
#else
            return null;
#endif
        }

        // ── GC 강제 + post-GC 메모리 측정 ────────────────────────────────────

        private IEnumerator MeasurePostGc(List<float> postGcMbList)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            yield return null; // 엔진이 정리할 한 프레임 양보
            float mb = GC.GetTotalMemory(false) / 1048576f;
            postGcMbList.Add(mb);
            Debug.Log($"[GameLoopRunner] Game {postGcMbList.Count} postGC={mb:F2}Mb");
        }

        // ── 결과 기록 ─────────────────────────────────────────────────────────

        private static void WriteResult(string path, string json, int scenario)
        {
            if (string.IsNullOrEmpty(path))
                path = Path.Combine(Application.persistentDataPath, $"gameloop_result_s{scenario}.json");

            Debug.Log($"[GameLoopRunner] Writing result to: {path}");

#if UNITY_ANDROID && !UNITY_EDITOR
            if (path.StartsWith("content://"))
            {
                WriteViaContentResolver(path, json);
                return;
            }
#endif
            try
            {
                // Firebase가 파일 경로를 줄 경우 부모 디렉토리가 없을 수 있음
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(path, json);
                Debug.Log($"[GameLoopRunner] Result written → {path}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLoopRunner] Write failed: {e.Message}");
            }
        }

#if UNITY_ANDROID && !UNITY_EDITOR
        // content:// URI는 ContentResolver의 OutputStream으로 써야 한다
        private static void WriteViaContentResolver(string uriString, string json)
        {
            AndroidJavaClass  player          = null;
            AndroidJavaObject activity        = null;
            AndroidJavaObject contentResolver = null;
            AndroidJavaObject uriClass        = null;
            AndroidJavaObject uri             = null;
            AndroidJavaObject outputStream    = null;
            AndroidJavaObject writer          = null;
            try
            {
                player          = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                activity        = player.GetStatic<AndroidJavaObject>("currentActivity");
                contentResolver = activity.Call<AndroidJavaObject>("getContentResolver");
                uriClass        = new AndroidJavaClass("android.net.Uri");
                uri             = uriClass.CallStatic<AndroidJavaObject>("parse", uriString);
                outputStream    = contentResolver.Call<AndroidJavaObject>("openOutputStream", uri);
                writer          = new AndroidJavaObject("java.io.OutputStreamWriter", outputStream);

                writer.Call("write", json);
                writer.Call("flush");
                Debug.Log($"[GameLoopRunner] Result written via ContentResolver → {uriString}");
            }
            catch (Exception e)
            {
                Debug.LogError($"[GameLoopRunner] ContentResolver write failed: {e.Message}");
            }
            finally
            {
                try { writer?.Call("close"); } catch { /* ignore close error */ }
                writer?.Dispose();
                outputStream?.Dispose();
                uri?.Dispose();
                uriClass?.Dispose();
                contentResolver?.Dispose();
                activity?.Dispose();
                player?.Dispose();
            }
        }
#endif

        // ── 전략 선택 ─────────────────────────────────────────────────────────

        private static IPlacementStrategy SelectStrategy(int scenario) =>
            scenario switch
            {
                _ => new RandomPlacementStrategy(),
            };
    }
}
#endif
