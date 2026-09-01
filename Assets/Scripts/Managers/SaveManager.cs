using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using System.IO;

[Serializable]
public class SaveData
{
    public long BestScore;
    public int TotalGamesPlayed;
    public int TotalLinesCleared;
    public float TotalPlayTime;
    public string LastPlayDate;
}

/// <summary>
/// 세이브/세션 파일 입출력.
///
/// ── gameData.json 무결성 정책 (마이그레이션 포함) ──────────────────────────────
///
/// 두 파일 모두 <c>Application.persistentDataPath</c> = 외부 앱전용 저장소에 있어
/// 릴리스 빌드에서도 루팅 없이 adb push로 교체할 수 있다(실기기 확인). 그래서
/// sessionData.json처럼 gameData.json도 <c>{payload, sig}</c> HMAC 봉투로 감싼다.
/// 키는 PlayerPrefs = 내부 저장소에 있어 adb만 가진 공격자는 읽지 못한다(SessionIntegrity 참고).
///
/// 세션과 결정적으로 다른 점: **세션은 못 믿으면 버리면 되지만 gameData는 버리면
/// 유저의 최고 점수가 사라진다.** 업데이트로 이 코드를 처음 받는 유저는 전원
/// 무서명 평문 파일을 갖고 있으므로, 평문을 한 번은 받아들여야 한다.
///
/// 그런데 평문을 무제한 받아들이면 서명이 무의미해진다 — 공격자가 "레거시처럼 보이는"
/// 평문을 push하면 그냥 통과한다. 그래서 PlayerPrefs에 <c>GameData.SignedOnce</c>
/// 플래그를 두고 **서명본을 한 번이라도 성공적으로 쓴 뒤에는 평문을 마이그레이션
/// 대상이 아니라 다운그레이드 공격으로 취급해 폐기한다.** 이 방어가 성립하는 근거는
/// 비대칭성이다: 공격자는 세이브 파일을 마음대로 바꿀 수 있지만 내부 저장소에 있는
/// 플래그는 지울 수 없다. 즉 "아직 서명한 적 없음" 상태를 위조할 수 없다.
///
/// 플래그를 세우는 시점은 <b>서명 파일 쓰기가 성공한 직후</b>여야 한다. 쓰기가 실패했는데
/// 플래그만 서면, 디스크에 남아 있는 정상 유저의 평문 파일이 다음 실행에서
/// 다운그레이드로 오판되어 폐기된다. 그래서 WriteAtomic이 성공 여부를 반환한다.
/// </summary>
public class SaveManager : SingletonClass<SaveManager>
{
    private const string SAVE_FILE_NAME = "gameData.json";
    private const string SESSION_FILE_NAME = "sessionData.json";
    private const string SETTINGS_KEY = "GameSettings";

    /// <summary>
    /// "이 설치는 서명 체제 하에 있다"는 표시. PlayerPrefs = 내부 저장소이므로
    /// adb 공격자가 지울 수 없다는 점이 다운그레이드 방어의 전제다.
    /// </summary>
    private const string SIGNED_ONCE_KEY = "GameData.SignedOnce";

    /// <summary>
    /// 서명 이력이 있는데 세이브를 못 쓰게 됐을 때(변조·다운그레이드 시도·손상) 새 데이터에
    /// 넣어 줄 누적 플레이 수.
    ///
    /// 0으로 시작하면 폐기 자체가 공격 목표를 달성시켜 버린다: AdGatePolicy의 신규 유저
    /// 보호가 TotalGamesPlayed를 보므로, 파일을 망가뜨려 폐기를 유도하면 전면 광고가
    /// 면제된다. 플래그가 서 있다는 것은 이 설치가 이미 저장을 해 봤다는 뜻 = 신규 유저가
    /// 아니라는 뜻이므로 보호 구간을 주지 않는다.
    ///
    /// AdGatePolicy.GracePeriodGames(현재 3)는 private이라 참조할 수 없어, 어떤 튜닝값이든
    /// 확실히 넘도록 넉넉히 잡는다. 이 필드는 UI에 표시되지 않아 통계 왜곡의 부작용이 없다.
    /// </summary>
    /// 값이 크면 워터마크(GamesPlayedMark)에 그 값이 **영구 각인**되어, 데이터를 잃은
    /// 정상 유저가 사실상 신규 상태인데도 광고 보호를 영구히 못 받는다.
    /// 보호 구간(AdGatePolicy.GracePeriodGames = 3)을 벗어나기만 하면 목적은 달성되므로
    /// 딱 그만큼만 넘긴다. 튜닝값이 바뀌면 여기도 함께 올릴 것.
    private const int RECOVERY_GAMES_PLAYED = 4;

    /// <summary>
    /// 지금까지 관측된 TotalGamesPlayed의 최댓값(단조 증가). PlayerPrefs = 내부 저장소.
    ///
    /// 서명만으로는 **되감기(replay)**를 막을 수 없다. 서명은 "이 앱이 썼는가"만 보증하고
    /// 신선도는 보증하지 않으므로, 누적 플레이 수가 낮았던 시절의 서명본을 adb pull 로
    /// 떠 두었다가 나중에 push 하면 서명이 유효한 채로 통과한다. 그러면 AdGatePolicy 의
    /// 신규 유저 보호 조건(TotalGamesPlayed <= GracePeriodGames)이 다시 참이 되어
    /// 전면 광고가 면제되고, 3판마다 되밀기만 하면 영구히 반복된다 —
    /// 서명을 붙인 목적 자체가 무력화된다.
    ///
    /// 그래서 이 값만 워터마크로 따로 남긴다. 공격자는 외부 저장소의 세이브 파일은
    /// 바꿀 수 있어도 내부 저장소의 이 값은 낮출 수 없다. 되감힌 파일을 읽어도
    /// 카운터가 워터마크까지 올라가므로 보호 구간으로 돌아가지 못한다.
    /// (BestScore 되감기는 막지 않는다 — 서버 제출 경로가 없어 실피해가 없고,
    ///  최고 점수는 사용자가 스스로 낮출 이유가 없어 워터마크가 곧 값 자체다.)
    /// </summary>
    private const string GAMES_PLAYED_MARK_KEY = "GameData.GamesPlayedMark";

    /// <summary>
    /// 마지막으로 성공적으로 쓴 세션 파일의 일련번호. 세션 되감기 방어용.
    /// 근거는 GameSessionData.serial 주석에 있다.
    ///
    /// **세션을 지울 때도 올린다.** 파일만 지우면 이미 떠 둔 복사본의 번호가 그대로 유효해서,
    /// 게임오버로 세션이 정리된 뒤 복사본을 되밀면 그 판을 되살릴 수 있다
    /// (부활권까지 함께 되살아난다). 번호를 올려 바깥에 있는 복사본을 한꺼번에 무효화한다.
    /// </summary>
    private const string SESSION_SERIAL_KEY = "Session.Serial";

    /// <summary>
    /// 정상 플레이로는 도달할 수 없는 상한. 변조된 저장 파일의 값을 잘라내는 데 쓴다.
    /// 실제 최고 점수는 수백만 단위이므로 1조는 정상 기록을 건드리지 않는다.
    /// </summary>
    public const long MAX_SCORE = 1_000_000_000_000L;

    /// <summary>
    /// 콤보 상한. 점수 배수는 콤보 10에서 이미 최대(2.0배)라 그 이상은 표시용 숫자일 뿐이다.
    /// 넉넉히 잡아 정상 플레이를 자르지 않으면서 int 극단값만 걸러낸다.
    /// </summary>
    public const int MAX_COMBO = 9999;


    public SaveData CurrentSaveData { get; private set; }

    protected override void Awake()
    {
        base.Awake();
        LoadGame();
    }

    /// <summary>
    /// 세이브를 서명 봉투로 저장한다. **절대 예외를 던지지 않는다.**
    ///
    /// 서명이 들어오면서 이 경로에 HMACSHA256이 생겼다 — 매니지드 스트리핑 등으로 실패할
    /// 여지가 있는데 호출자 위치가 위험하다: IncrementGamesPlayed는
    /// IGGameController.CheckGameOver에서 SetGameState(GameOver)·전면 광고 노출 **직전**에,
    /// ForfeitGame에서는 RequestGoToMainMenu() **직전**에 불린다. 여기서 예외가 나가면
    /// 게임오버 화면이 뜨지 않거나 메인으로 못 나가는 진행 불가 상태가 된다.
    /// 저장 실패가 게임 흐름을 막아서는 안 된다.
    /// </summary>
    public void SaveGame()
    {
        string path = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);

        try
        {
            string payload = JsonUtility.ToJson(CurrentSaveData);
            var envelope = new SignedEnvelope
            {
                payload = payload,
                sig     = SessionIntegrity.Sign(payload),
            };

            // 플래그는 **쓰기 성공을 확인한 뒤에만** 세운다. 순서가 뒤바뀌면 쓰기에 실패한
            // 기기에서 다음 실행에 정상 유저의 평문 세이브가 다운그레이드로 오판되어 폐기된다.
            if (WriteAtomic(path, JsonUtility.ToJson(envelope)))
            {
                MarkSignedOnce();

#if UNITY_WEBGL
                // 로컬 쓰기가 성공한 값만 복제한다. 복제는 실패해도 게임에 영향이 없다.
                // 서명 봉투가 아니라 payload를 넘기는 이유는 AitSaveMirror 주석 참고.
                AitSaveMirror.Push(payload);
#endif
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 세이브 저장 실패 — 진행은 계속합니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>서명본을 성공적으로 쓴 적이 있는가.</summary>
    private static bool HasSignedOnce() => PlayerPrefs.GetInt(SIGNED_ONCE_KEY, 0) == 1;

    /// <summary>
    /// 되감기 방어. 읽어 들인 누적 플레이 수를 워터마크 아래로 내려가지 못하게 하고,
    /// 값이 더 크면 워터마크를 갱신한다. (GAMES_PLAYED_MARK_KEY 주석 참고)
    ///
    /// 로드 직후와 증가 시점 양쪽에서 불러야 한다 — 로드에서만 하면 이번 세션에
    /// 늘어난 판수가 기록되지 않아 다음 실행의 방어선이 낡는다.
    /// </summary>
    private static void EnforceGamesPlayedFloor(SaveData data)
    {
        if (data == null) return;

        int mark = PlayerPrefs.GetInt(GAMES_PLAYED_MARK_KEY, 0);

        if (data.TotalGamesPlayed < mark)
        {
            Debug.LogWarning(
                $"[SaveManager] 누적 플레이 수가 기록보다 낮습니다 ({data.TotalGamesPlayed} < {mark}) — " +
                "되감기로 보고 기록값으로 올립니다.");
            data.TotalGamesPlayed = mark;
        }
        else if (data.TotalGamesPlayed > mark)
        {
            // PlayerPrefs 쓰기 실패는 예외를 던진다. 호출처가 게임오버 전이 직전
            // (IncrementGamesPlayed)이라 여기서 예외가 새면 진행 불가 상태가 된다.
            // 워터마크를 못 올리는 것은 치팅 방어의 약화일 뿐이다.
            try
            {
                PlayerPrefs.SetInt(GAMES_PLAYED_MARK_KEY, data.TotalGamesPlayed);
                PlayerPrefs.Save();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 누적 판수 기록 갱신 실패 — 진행은 계속합니다. ({e.GetType().Name})");
            }
        }
    }

    /// <summary>
    /// 서명 이력을 기록한다. 이미 서 있으면 아무것도 하지 않는다 —
    /// SaveGame은 점수 갱신마다 불리므로 매번 PlayerPrefs.Save()로 디스크를 때리지 않는다.
    /// </summary>
    private static void MarkSignedOnce()
    {
        if (HasSignedOnce()) return;

        // 이 호출은 SaveGame 안에 있어 이미 try 로 덮여 있지만, SaveGame 이 예외를
        // 삼키면 그 뒤 코드(마이그레이션 재저장 등)가 조용히 건너뛰어진다.
        // 여기서 막아 두면 실패해도 흐름이 유지된다 — 플래그가 늦게 서는 것은
        // 평문 수용 창이 한 번 더 열리는 것뿐이고, 그건 정상 유저에게 무해하다.
        try
        {
            PlayerPrefs.SetInt(SIGNED_ONCE_KEY, 1);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 서명 이력 기록 실패 — 진행은 계속합니다. ({e.GetType().Name})");
        }
    }

    /// <summary>
    /// 세이브를 읽는다. 파일이 없거나 손상되었어도 절대 예외를 던지지 않는다.
    ///
    /// 이 메서드가 예외를 던지면 Awake가 중단되어 CurrentSaveData가 null로 남고,
    /// IGScoreController.InitializeController → GetBestScore()에서 NRE가 나면서
    /// 게임 씬 초기화 전체가 멈춘다. 그 상태는 앱 데이터 삭제 외에는 복구 불가다.
    /// 손상 파일은 .bak으로 격리해 원인 분석 여지를 남기고 새 데이터로 시작한다.
    ///
    /// 분기는 ReadSaveFile에 있다. 로드는 앱 시작 시 1회라 비용은 문제가 되지 않는다.
    /// </summary>
    public void LoadGame()
    {
        string path = Path.Combine(Application.persistentDataPath, SAVE_FILE_NAME);
        bool needsResave = false;

        try
        {
            if (File.Exists(path))
                CurrentSaveData = ReadSaveFile(path, out needsResave);
        }
        catch (Exception e)
        {
            // 애초에 JSON이 아닌 파일. 서명 검증까지 가지도 못한 손상이므로 격리해 둔다.
            Debug.LogWarning($"[SaveManager] 세이브 로드 실패 — 새 데이터로 시작합니다. ({e.GetType().Name}: {e.Message})");
            QuarantineCorruptFile(path);
            CurrentSaveData = null;
            needsResave = false;
        }

        if (CurrentSaveData == null)
        {
            CurrentSaveData = new SaveData();

            // 서명 이력이 있는데도 여기까지 왔다 = 쓸 수 없는 세이브를 폐기했다는 뜻이다.
            // 그 경우 이 설치는 신규 유저가 아니므로 광고 보호 구간을 주지 않는다.
            // (RECOVERY_GAMES_PLAYED 주석 참고 — 폐기 유도 자체가 공격이 되는 것을 막는다.)
            if (HasSignedOnce())
                CurrentSaveData.TotalGamesPlayed = RECOVERY_GAMES_PLAYED;
        }

        SanitizeSaveData(CurrentSaveData);

        // 클램프 뒤에 워터마크를 적용한다 — 음수를 0으로 자른 값이 다시 기록값까지 올라가야
        // 되감기와 음수 조작이 같은 방어선에 걸린다.
        EnforceGamesPlayedFloor(CurrentSaveData);

        // 지금 메모리에 있는 데이터가 유효한 서명본으로 뒷받침되지 않으면 **즉시** 확정한다.
        //  - 레거시 평문 수용: 마이그레이션을 1회로 끝낸다. 성공하면 SignedOnce 플래그가 서서
        //    다음 실행부터 평문이 거부된다.
        //  - 최고 점수 구제: 안 쓰면 다음 저장 트리거(게임오버·일시정지)까지 디스크에 없다.
        //    그 전에 앱이 닫히면 살려낸 기록이 그대로 사라진다.
        // 클램프·워터마크까지 끝난 값을 쓰는 것이 의도다 — 변조된 값이 서명만 얻어 살아남지 않는다.
        if (needsResave)
        {
            Debug.Log("[SaveManager] 서명본으로 다시 씁니다 (레거시 마이그레이션 또는 최고 점수 구제).");
            SaveGame();
        }

#if UNITY_WEBGL
        // 복제본 확인은 비동기다. 로딩을 붙잡지 않고, 늦게 도착하면 그때 병합한다.
        // 로컬이 멀쩡한 경우가 대부분이고 그때는 병합에서 아무것도 바뀌지 않는다.
        AitSaveMirror.Restore(MergeFromMirror);
#endif
    }

#if UNITY_WEBGL
    /// <summary>
    /// 복제본 값을 현재 데이터에 병합한다. 바뀐 것이 있으면 true.
    ///
    /// **항목별로 큰 값이 이긴다.** 어느 쪽이 최신인지 판단하지 않는 것이 의도다 —
    /// 시계에 의존하면 기기 시간 조작이나 시차에 그대로 휘둘린다. 최고 점수와 누적 통계는
    /// 단조 증가하는 값이라 큰 쪽을 택하면 되돌아가는 일이 없다.
    /// <c>LastPlayDate</c>는 순서를 신뢰할 수 없어 건드리지 않는다(로컬 값 유지).
    ///
    /// 부수 효과가 없는 순수 함수다 — 저장·통지 없이 값만 합친다. 그래서 싱글턴을
    /// 만들지 않고 검증할 수 있다.
    /// </summary>
    public static bool MergeRecords(SaveData current, SaveData remote)
    {
        if (current == null || remote == null) return false;

        bool changed = false;
        if (remote.BestScore         > current.BestScore)         { current.BestScore         = remote.BestScore;         changed = true; }
        if (remote.TotalGamesPlayed  > current.TotalGamesPlayed)  { current.TotalGamesPlayed  = remote.TotalGamesPlayed;  changed = true; }
        if (remote.TotalLinesCleared > current.TotalLinesCleared) { current.TotalLinesCleared = remote.TotalLinesCleared; changed = true; }
        if (remote.TotalPlayTime     > current.TotalPlayTime)     { current.TotalPlayTime     = remote.TotalPlayTime;     changed = true; }

        return changed;
    }

    /// <summary>
    /// 복제본이 로컬보다 앞선 값을 갖고 있으면 병합됐다고 알린다.
    /// 타이틀 화면이 이미 그려진 뒤에 도착할 수 있어 UI가 스스로 갱신해야 한다.
    /// </summary>
    public static event Action OnRestoredFromMirror;

    /// <summary>
    /// 복제본을 읽어 병합하고, 바뀐 것이 있으면 로컬에 확정한 뒤 알린다.
    ///
    /// 복제본이라고 더 믿지는 않는다. 파일에서 읽은 값과 똑같이 클램프하고
    /// 되감기 워터마크를 태운 뒤, 현재 키로 서명해 로컬에 확정한다.
    /// 병합 규칙 자체는 <see cref="MergeRecords"/>에 있다.
    /// </summary>
    private void MergeFromMirror(string payloadJson)
    {
        if (CurrentSaveData == null) return;

        SaveData remote;
        try
        {
            remote = JsonUtility.FromJson<SaveData>(payloadJson);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 복제본 해석 실패 — 무시합니다. ({e.GetType().Name}: {e.Message})");
            return;
        }

        if (remote == null) return;
        SanitizeSaveData(remote);

        if (!MergeRecords(CurrentSaveData, remote)) return;

        Debug.Log($"[SaveManager] 복제본에서 기록을 복원했습니다 (최고 점수 {CurrentSaveData.BestScore}).");

        EnforceGamesPlayedFloor(CurrentSaveData);
        SaveGame();

        // 구독자 예외가 나머지 구독자를 막지 않게 한다 (게임오버 소프트락 이력이 있는 패턴).
        var handlers = OnRestoredFromMirror;
        if (handlers == null) return;

        foreach (var handler in handlers.GetInvocationList())
        {
            try { ((Action)handler).Invoke(); }
            catch (Exception e)
            {
                Debug.LogWarning($"[SaveManager] 복원 통지 구독자 예외 ({handler.Method.Name}): {e.Message}");
            }
        }
    }
#endif

    /// <summary>
    /// 세이브 파일 하나를 해석한다.
    ///
    /// 반환 null = 쓸 수 없는 파일(호출자가 새 데이터로 시작). JSON 자체가 깨진 경우는
    /// 예외를 던져 호출자의 .bak 격리 경로로 보낸다.
    /// <paramref name="needsResave"/> = true 는 지금 메모리에 있는 데이터가 유효한 서명본으로
    /// 뒷받침되지 않는다는 뜻이다(레거시 평문 수용 또는 최고 점수 구제).
    /// 호출자가 즉시 서명본으로 다시 써서 확정해야 한다 — 안 그러면 앱을 닫는 순간 사라진다.
    /// </summary>
    private static SaveData ReadSaveFile(string path, out bool needsResave)
    {
        needsResave = false;

        string raw = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(raw))
        {
            // 0바이트/공백 파일 — 쓰기가 중간에 끊긴 잔재다. 격리해 봐야 분석할 내용이 없다.
            Debug.LogWarning("[SaveManager] 세이브 파일이 비어 있습니다 — 새 데이터로 시작합니다.");
            TryDelete(path);
            return null;
        }

        // 레거시 평문을 이 타입으로 읽으면 JsonUtility가 모르는 필드를 무시해
        // payload/sig가 비어 있는 객체가 나온다. 그것이 "봉투가 아니다"의 판정 근거다.
        var envelope = JsonUtility.FromJson<SignedEnvelope>(raw);

        bool looksSigned = envelope != null &&
                           !string.IsNullOrEmpty(envelope.payload) &&
                           !string.IsNullOrEmpty(envelope.sig);

        if (looksSigned)
        {
            if (!SessionIntegrity.Verify(envelope.payload, envelope.sig))
            {
                // 변조됐거나, 다른 설치에서 왔거나, **키를 잃었다.** 셋을 구별할 수 없다.
                //
                // 예전에는 파일을 삭제했다. 그런데 이 분기가 지키려는 것(광고 회피)은
                // GamesPlayedMark 워터마크가 이미 독립적으로 막고 있고, 삭제로 잃는 것은
                // 유저의 최고 점수 전부다 — **방어 이득 0, 피해 최대.**
                // BestScore 되감기는 이 파일 상단 주석대로 애초에 막지 않는 항목이다
                // (서버 제출 경로가 없어 실피해가 없다).
                // 그래서 최고 점수만 클램프해 살리고 나머지는 버린다. 파일은 지우지 않고
                // 격리해 원인 분석 여지를 남긴다.
                Debug.LogWarning("[SaveManager] 세이브 서명 검증 실패 — 최고 점수만 살리고 격리합니다.");

                var salvaged = SalvageBestScoreOnly(envelope.payload);
                QuarantineCorruptFile(path);

                // 구제했으면 즉시 서명본으로 확정해야 한다 — 안 그러면 다음 저장 트리거까지
                // 디스크에 없고, 그 전에 앱이 닫히면 살려낸 기록이 사라진다.
                needsResave = salvaged != null;
                return salvaged;   // null 이면 호출자가 새 데이터로 시작한다
            }

            // 유효한 서명은 이 앱이 썼다는 증거다. 플래그가 어떤 이유로 빠져 있었다면
            // 여기서 복구해 둔다(평문 수용 창을 열어 두지 않는다).
            MarkSignedOnce();

            return JsonUtility.FromJson<SaveData>(envelope.payload);
        }

        // ── 봉투가 아니다 = 서명 도입 이전의 평문 세이브 ──
        if (HasSignedOnce())
        {
            // 서명본을 쓴 적이 있는데 평문이 나타났다. 우회 시도일 수 있지만
            // **정상 경로도 있다**: 이 기기에 구버전(서명 이전)이 다시 설치되어 평문을 쓴 경우다.
            // 내부 테스트 이탈이나 개발자 롤백에서 실제로 일어나고, 그때 삭제하면
            // 신버전을 한 번이라도 실행한 **전 유저의 최고 점수가 날아간다.**
            //
            // 여기서도 방어 이득은 워터마크가 이미 확보하고 있으므로 최고 점수만 살린다.
            Debug.LogWarning("[SaveManager] 서명 이력이 있는데 평문 세이브가 나타났습니다 — 최고 점수만 살립니다.");

            var salvaged = SalvageBestScoreOnly(raw);
            QuarantineCorruptFile(path);

            needsResave = salvaged != null;   // 위와 같은 이유로 즉시 확정
            return salvaged;
        }

        // 업데이트 유저의 첫 실행 경로. JsonUtility는 "null" 같은 입력에 대해
        // 예외 없이 null을 반환하기도 한다.
        var legacy = JsonUtility.FromJson<SaveData>(raw);
        if (legacy == null) return null;

        // **필드명이 하나도 맞지 않는 JSON도 예외 없이 전 필드 0으로 파싱된다.**
        // (예: 1.0.2 이하의 `HighScore` 키 — BestScore 로 개칭되기 전 이름)
        // 그걸 마이그레이션으로 받아들이면 0을 서명해 원본 위에 덮어써 복구 불가가 된다.
        // 읽을 수 있었다는 증거가 없으면 **파일을 건드리지 않고** 새 데이터로 시작한다.
        if (LooksAllDefault(legacy))
        {
            Debug.LogWarning("[SaveManager] 평문 세이브를 해석하지 못했습니다(전 필드 기본값) — " +
                             "덮어쓰지 않고 그대로 둡니다.");
            return null;
        }

        needsResave = true;
        return legacy;
    }

    /// <summary>
    /// 쓸 수 없는 세이브에서 **최고 점수만** 건져 온다. 나머지 통계는 신뢰할 수 없어 버린다.
    /// 파싱 자체가 불가능하면 null.
    ///
    /// 이 구제가 정당한 이유: 서명·다운그레이드 판정이 지키려는 대상은 광고 회피이고
    /// 그것은 GamesPlayedMark 워터마크(내부 저장소, 단조 증가)가 독립적으로 막는다.
    /// BestScore 는 서버 제출 경로가 없어 위조돼도 실피해가 없다.
    /// 즉 **최고 점수를 버려서 얻는 방어는 없고, 잃는 것은 유저의 기록 전부다.**
    /// </summary>
    private static SaveData SalvageBestScoreOnly(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;

        try
        {
            var parsed = JsonUtility.FromJson<SaveData>(json);
            if (parsed == null || LooksAllDefault(parsed)) return null;

            return new SaveData
            {
                BestScore = Math.Clamp(parsed.BestScore, 0L, MAX_SCORE),
                // 통계는 구제하지 않는다. TotalGamesPlayed 는 광고 판정 입력이므로
                // 신뢰할 수 없는 값을 그대로 쓰면 안 되고, 워터마크가 하한을 채워 준다.
            };
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 최고 점수 구제 실패 — 새 데이터로 시작합니다. ({e.GetType().Name})");
            return null;
        }
    }

    /// <summary>전 필드가 기본값인가 = "파싱은 됐지만 실제로 읽어낸 것이 없다"의 판정.</summary>
    private static bool LooksAllDefault(SaveData data) =>
        data != null &&
        data.BestScore == 0 &&
        data.TotalGamesPlayed == 0 &&
        data.TotalLinesCleared == 0 &&
        data.TotalPlayTime == 0f &&
        string.IsNullOrEmpty(data.LastPlayDate);

    /// <summary>
    /// 파일에서 읽은 값의 범위를 강제한다.
    ///
    /// 서명이 붙은 뒤에도 유지한다. 서명은 "이 앱이 쓴 파일인가"만 보증하므로,
    /// 서명 이전에 쓰인 레거시 평문(마이그레이션 입력)은 여전히 손으로 고친 값일 수 있다.
    /// 특히 TotalGamesPlayed에 음수를 넣으면 AdGatePolicy의 신규 유저 보호 조건이
    /// 영구히 참이 되어 전면 광고가 완전히 사라진다. 읽은 직후 한 번만 잘라내면 막힌다.
    /// </summary>
    private static void SanitizeSaveData(SaveData data)
    {
        data.BestScore = Math.Clamp(data.BestScore, 0L, MAX_SCORE);
        if (data.TotalGamesPlayed < 0)  data.TotalGamesPlayed = 0;
        if (data.TotalLinesCleared < 0) data.TotalLinesCleared = 0;
        // NaN은 어떤 비교에도 false이므로 양수 조건으로 판정한다.
        if (!(data.TotalPlayTime >= 0f)) data.TotalPlayTime = 0f;
    }

    /// <summary>
    /// 세션 파일에서 읽은 값의 범위를 강제한다. SanitizeSaveData와 같은 취지다.
    ///
    /// 이 방어가 gameData.json에만 있고 sessionData.json에는 빠져 있었다.
    /// currentScore에 long.MinValue를 넣으면 ScoreView → ScoreFormatterUtility 경로에서
    /// 무한 재귀가 나 프로세스가 즉사하고, 세션 파일은 게임오버 때만 지워지므로
    /// 앱을 켤 때마다 같은 지점에서 죽는 상태가 됐다. 세이브 위치가 외부 앱전용
    /// 저장소라 루팅 없이 adb push로 교체할 수 있어 실제로 도달 가능한 경로다.
    /// </summary>
    private static void SanitizeSessionData(IGMain.GameSessionData data)
    {
        data.currentScore = Math.Clamp(data.currentScore, 0L, MAX_SCORE);
        data.comboCount   = Math.Clamp(data.comboCount, 0, MAX_COMBO);
    }

    /// <summary>
    /// 임시 파일에 먼저 쓴 뒤 교체한다.
    ///
    /// File.WriteAllText는 기존 파일을 먼저 비우고 쓰므로, 저장공간 부족(ENOSPC)이나
    /// 그 사이의 강제 종료로 0바이트/반쪽 파일이 남을 수 있다. 손상된 세이브는
    /// 앱을 못 켜게 만들었던 원인이므로 쓰기 자체를 원자적으로 바꾼다.
    ///
    /// 반환값은 "파일이 실제로 이 내용으로 갱신됐는가"다. SignedOnce 플래그를 세울지
    /// 판단하는 근거이므로, 폴백까지 모두 실패한 경우에만 false여야 한다.
    /// </summary>
    private static bool WriteAtomic(string path, string json)
    {
        string tmp = path + ".tmp";

        // ── 1단계: 임시 파일 쓰기 ──
        //
        // 여기서 실패하면 **폴백하지 않는다.** 폴백의 File.WriteAllText(path)는
        // FileMode.Create 라 대상을 먼저 0바이트로 잘라낸다. tmp 쓰기가 실패하는 상황
        // (저장공간 부족·I/O 오류)에서는 그 두 번째 쓰기도 실패할 가능성이 높고,
        // 그러면 **멀쩡했던 기존 세이브가 0바이트로 파괴된다.**
        // 원자적 쓰기를 도입한 이유를 폴백이 정확히 무효화하는 구조였다.
        // 마이그레이션 경로에서 이게 터지면 유일한 사본인 레거시 평문이 날아간다.
        try
        {
            File.WriteAllText(tmp, json);
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveManager] 임시 파일 쓰기 실패 — 기존 세이브를 그대로 보존합니다. " +
                           $"({e.GetType().Name}: {e.Message})");
            TryDelete(tmp);
            return false;
        }

        // ── 2단계: 교체 ──
        //
        // tmp 에 온전한 내용이 있는 것이 확인된 뒤이므로, 교체가 실패했을 때
        // 직접 쓰기로 폴백해도 최악의 경우가 "tmp 에 사본이 남아 있는 상태"다.
        try
        {
            if (File.Exists(path))
                File.Replace(tmp, path, null);   // 지원 플랫폼에서 원자적 교체
            else
                File.Move(tmp, path);

            return true;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 원자적 교체 실패, 직접 쓰기로 폴백합니다. ({e.GetType().Name}: {e.Message})");
            try
            {
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception fallbackError)
            {
                // 저장에 실패해도 게임 진행은 막지 않는다.
                Debug.LogError($"[SaveManager] 저장 실패: {fallbackError.Message}");
                return false;
            }
            finally
            {
                TryDelete(tmp);
            }
        }
    }

    private static void QuarantineCorruptFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return;

            string backup = path + ".bak";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(path, backup);
        }
        catch (Exception e)
        {
            // 격리에 실패하면 손상 파일을 지워서라도 다음 실행을 살린다.
            Debug.LogWarning($"[SaveManager] 손상 파일 격리 실패: {e.Message}");
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 파일 삭제 실패 ({path}): {e.Message}");
        }
    }

    public long GetBestScore()
    {
        return CurrentSaveData.BestScore;
    }

    public void UpdateBestScore(long score)
    {
        if (score > CurrentSaveData.BestScore)
        {
            CurrentSaveData.BestScore = score;
            SaveGame();
        }
    }

    public void ResetBestScore()
    {
        CurrentSaveData.BestScore = 0;
        SaveGame();
    }

    /// <summary>
    /// 판수를 1 올리고 확정한다. 게임오버·홈 나가기 직전에 불리므로 **예외를 던지지 않는다.**
    /// (EnforceGamesPlayedFloor·SaveGame 내부에서 각각 삼킨다 — 여기서 새면 진행 불가가 된다)
    /// </summary>
    public void IncrementGamesPlayed()
    {
        if (CurrentSaveData == null) return;

        CurrentSaveData.TotalGamesPlayed++;
        EnforceGamesPlayedFloor(CurrentSaveData);
        SaveGame();
    }

    /// <summary>
    /// 누적 클리어 수를 더한다. **디스크에 즉시 쓰지 않는다.**
    ///
    /// 예전에는 라인 클리어마다 SaveGame 을 불렀다. 서명이 붙은 지금은 그때마다
    /// JSON 직렬화 2회 + HMAC + 임시 파일 쓰기 + 교체가 **클리어 애니메이션이 시작되는
    /// 프레임에 동기로** 일어난다. 세이브 경로는 외부 에뮬레이트 저장소(FUSE 경유)라
    /// 내부보다 느리고 편차가 크며, 클리어는 한 판에 수십~수백 번 발생한다.
    ///
    /// 이 값은 소비처가 없는 통계이므로(광고 판정은 TotalGamesPlayed 만 본다)
    /// 게임오버·홈·일시정지 시점의 저장에 얹어 보내면 충분하다.
    /// 최악의 경우 강제 종료로 진행 중 판의 클리어 수만 유실된다.
    /// </summary>
    public void AddLinesCleared(int lines)
    {
        CurrentSaveData.TotalLinesCleared += lines;
    }

    public void UpdatePlayTime(float time)
    {
        CurrentSaveData.TotalPlayTime += time;
        CurrentSaveData.LastPlayDate = System.DateTime.Now.ToString();
        SaveGame();
    }

    /// <summary>
    /// 서명 봉투. 본문(payload)과 그 서명(sig)을 함께 담는다.
    /// 세션과 세이브가 같은 포맷을 공유한다 — 위협 모델이 동일하고, 봉투 JSON은
    /// 필드 이름으로 직렬화되므로 두 파일이 같은 타입을 써도 형식이 갈리지 않는다.
    ///
    /// 본문에 서명 필드를 넣지 않고 봉투로 감싸는 이유: 같은 객체 안에 두면
    /// "서명을 뺀 나머지"를 다시 직렬화해야 검증이 되는데, JsonUtility의 필드 순서에
    /// 의존하게 되어 필드를 하나 추가하는 순간 조용히 깨진다.
    /// </summary>
    [Serializable]
    private class SignedEnvelope
    {
        public string payload;
        public string sig;
    }

    /// <summary>
    /// 진행 중 세션을 저장한다. **절대 예외를 던지지 않는다.**
    ///
    /// 서명이 추가되면서 이 경로에 HMACSHA256이 들어왔다. 매니지드 스트리핑이나 플랫폼
    /// 제약으로 실패할 여지가 생겼는데, 호출자가 하필 위험한 위치에 있다:
    /// ReviveGame는 보상형 광고 닫힘 콜백 안에서 실행되고, 그 콜백은
    /// AdMobProvider.RegisterRewardedCallbacks에서 다음 광고를 프리로드하기 **직전**이다.
    /// 여기서 예외가 나가면 LoadRewarded()가 실행되지 않아 앱을 재시작할 때까지
    /// 부활 기능이 죽는다. 저장 실패가 게임 기능을 망가뜨리면 안 된다.
    /// </summary>
    public void SaveSession(IGMain.GameSessionData data)
    {
        try
        {
            // 일련번호를 하나 올려 찍는다. 이 값이 서명 대상에 포함되므로 위조할 수 없다.
            int next = PlayerPrefs.GetInt(SESSION_SERIAL_KEY, 0) + 1;
            data.serial = next;

            string payload = JsonUtility.ToJson(data);
            var envelope = new SignedEnvelope
            {
                payload = payload,
                sig     = SessionIntegrity.Sign(payload),
            };

            string path = Path.Combine(Application.persistentDataPath, SESSION_FILE_NAME);

            // 기대값은 **쓰기가 성공한 뒤에만** 올린다. 순서가 뒤바뀌면 쓰기에 실패한 기기에서
            // 다음 실행에 정상 세션이 "번호가 낮다"는 이유로 폐기된다.
            // (gameData의 SignedOnce 플래그와 같은 함정이다)
            if (WriteAtomic(path, JsonUtility.ToJson(envelope)))
            {
                PlayerPrefs.SetInt(SESSION_SERIAL_KEY, next);
                PlayerPrefs.Save();
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 세션 저장 실패 — 진행은 계속합니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>
    /// 진행 중이던 세션을 읽는다. 손상 시 null을 반환하고 파일을 지운다.
    ///
    /// 세션 파일은 게임오버/재시작/포기 때만 삭제되므로, 복원 중 예외가 나면
    /// 파일이 그대로 남아 앱을 다시 켜도 같은 지점에서 계속 실패한다.
    /// 손상된 세션은 버리는 것이 안전한 기본값이다 — 최고 점수는 별도 파일이라 보존된다.
    /// </summary>
    public IGMain.GameSessionData LoadSession()
    {
        string path = Path.Combine(Application.persistentDataPath, SESSION_FILE_NAME);

        try
        {
            if (!File.Exists(path)) return null;
            string raw = File.ReadAllText(path);

            var envelope = JsonUtility.FromJson<SignedEnvelope>(raw);
            if (envelope == null || string.IsNullOrEmpty(envelope.payload) ||
                !SessionIntegrity.Verify(envelope.payload, envelope.sig))
            {
                // 변조됐거나, 다른 기기/설치에서 온 파일이거나, 서명 이전의 구버전 포맷이다.
                // 어느 쪽이든 신뢰할 수 없으므로 버린다. 세션 하나를 잃을 뿐
                // 최고 점수는 별도 파일이라 보존된다.
                Debug.LogWarning("[SaveManager] 세션 서명 검증 실패 — 세션을 버립니다.");
                TryDelete(path);
                return null;
            }

            var session = JsonUtility.FromJson<IGMain.GameSessionData>(envelope.payload);
            if (session == null) return null;

            // ── 되감기(replay) 방어 ──────────────────────────────────────────
            //
            // 서명이 유효해도 **예전에** 이 앱이 쓴 파일일 수 있다. 기대값보다 번호가 낮으면
            // 바깥에 떠 뒀던 복사본을 되민 것이다(부활권 재사용·세이브 스커밍).
            int expectedSerial = PlayerPrefs.GetInt(SESSION_SERIAL_KEY, 0);

            if (session.serial < expectedSerial)
            {
                Debug.LogWarning(
                    $"[SaveManager] 세션 번호가 기대값보다 낮습니다 ({session.serial} < {expectedSerial}) — " +
                    "되감기로 보고 세션을 버립니다.");
                TryDelete(path);
                return null;
            }

            if (session.serial > expectedSerial)
            {
                // 파일은 써졌는데 PlayerPrefs 기록이 유실된 경우다(강제 종료 등).
                // 유효한 서명이 있으므로 이 파일을 기준으로 기대값을 맞춰 둔다.
                PlayerPrefs.SetInt(SESSION_SERIAL_KEY, session.serial);
                PlayerPrefs.Save();
            }

            SanitizeSessionData(session);
            return session;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 세션 로드 실패 — 세션을 버리고 새 판으로 시작합니다. ({e.GetType().Name}: {e.Message})");
            TryDelete(path);
            return null;
        }
    }

    /// <summary>
    /// 진행 중 세션을 지운다. 게임오버/재시작/포기 시퀀스 도중에 호출되므로
    /// 삭제 실패(파일 잠김 등)로 예외가 나가면 그 시퀀스 전체가 중단된다. 절대 던지지 않는다.
    /// </summary>
    public void ClearSession()
    {
        string path = Path.Combine(Application.persistentDataPath, SESSION_FILE_NAME);
        TryDelete(path);
        TryDelete(path + ".tmp");   // 원자적 쓰기가 중간에 실패해 남은 잔재

        // 파일을 지우는 것만으로는 부족하다. 바깥에 떠 둔 복사본의 번호가 여전히 기대값과
        // 같으면, 게임오버로 정리된 판을 되밀어 되살릴 수 있다(부활권까지 함께).
        // 번호를 올려 지금 존재하는 모든 복사본을 무효화한다.
        //
        // **PlayerPrefs.SetInt/Save 는 저장 실패 시 예외를 던진다.** 이 메서드는
        // CheckGameOver·ForfeitGame·RestartGame 에서 불리는데, 거기서 예외가 새면
        // 점수 확정·상태 전이·팝업이 전부 취소되어 게임오버 상태로 굳는다(복구 불가).
        // 번호를 못 올리는 것은 치팅 방어의 약화일 뿐이고, 진행 불가는 실사용자 피해다.
        TryBumpSessionSerial();
    }

    private static void TryBumpSessionSerial()
    {
        try
        {
            PlayerPrefs.SetInt(SESSION_SERIAL_KEY, PlayerPrefs.GetInt(SESSION_SERIAL_KEY, 0) + 1);
            PlayerPrefs.Save();
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveManager] 세션 번호 갱신 실패 — 진행은 계속합니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    public void SaveSettings(float musicVolume, float sfxVolume)
    {
        PlayerPrefs.SetFloat("MusicVolume", musicVolume);
        PlayerPrefs.SetFloat("SFXVolume", sfxVolume);
        PlayerPrefs.Save();
    }

    public (float musicVolume, float sfxVolume) LoadSettings()
    {
        float musicVolume = PlayerPrefs.GetFloat("MusicVolume", 1f);
        float sfxVolume = PlayerPrefs.GetFloat("SFXVolume", 1f);
        return (musicVolume, sfxVolume);
    }
}
