#if UNITY_WEBGL
using System;
using AppsInToss;
using UnityEngine;

/// <summary>
/// 세이브를 토스 네이티브 앱의 로컬 저장소에 복제해 둔다.
///
/// ── 무엇을 막는가 ────────────────────────────────────────────────────────────
/// WebGL의 저장 수단은 웹뷰의 IndexedDB뿐이고(파일도 PlayerPrefs도 전부 거기 있다),
/// 토스 웹뷰가 그것을 언제 비울지 보장이 없다. 비워지면 최고 점수가 0으로 돌아간다.
/// 심사 요건에도 "재접속 후 플레이 기록 유지"가 있다.
///
/// <c>AIT.Storage*</c>는 **토스 네이티브 앱의 로컬 저장소**다(서버가 아니다 — SDK에
/// 서버 저장 API는 없다). 웹뷰 저장소와 수명주기가 달라서, 웹뷰 데이터가 정리돼도
/// 이쪽은 남는다. 반대로 **기기 변경·토스 앱 재설치는 이것으로 막지 못한다.**
///
/// ── 왜 서명 봉투가 아니라 payload를 넣는가 ───────────────────────────────────
/// HMAC 키는 PlayerPrefs에 있고, WebGL에서 PlayerPrefs는 세이브 파일과 같은
/// IndexedDB에 산다. 즉 우리가 막으려는 그 사건이 일어나면 키도 함께 사라진다.
/// 서명본을 복제해 두면 복원 시점에 키가 달라 검증이 **반드시** 실패한다.
/// 그래서 서명 이전의 payload를 넣고, 복원한 값은 현재 키로 다시 서명해 로컬에 쓴다.
/// 복원값을 무조건 믿지는 않는다 — SaveManager가 파일과 똑같이 클램프·워터마크를 태운다.
///
/// ── 세션은 복제하지 않는다 ───────────────────────────────────────────────────
/// 진행 중이던 판(sessionData)은 되감기 방어가 PlayerPrefs의 일련번호에 묶여 있어
/// 복제·복원이 그 방어와 얽힌다. 심사 요건도 "기록 유지"라 최고 점수·누적 통계면 된다.
/// </summary>
public static class AitSaveMirror
{
    private const string STORAGE_KEY = "calcublock.gameData";

    /// <summary>
    /// 무기한 대기(0)는 쓰지 않는다. 응답이 오지 않아도 게임은 계속돼야 한다.
    /// </summary>
    private const int TIMEOUT_MS = 10_000;

    /// <summary>
    /// 직전에 보낸 payload. SaveGame은 점수 갱신마다 불리므로 같은 값을 반복해서
    /// 브리지로 밀어내지 않는다.
    /// </summary>
    private static string _lastPushed;

    /// <summary>복제본에 소유자를 함께 남긴다. 다른 계정의 기록이 섞여 들어오는 것을 막는다.</summary>
    [Serializable]
    private class MirrorEnvelope
    {
        public string owner;
        public string payload;
    }

    /// <summary>
    /// 로컬 쓰기가 성공한 payload를 복제한다. 실패해도 게임에는 영향이 없다.
    ///
    /// ⚠ 이 호출은 페이지가 숨겨지는 순간의 저장 경로(IGGameController)에서도 들어온다.
    /// 그때는 player loop가 멈춰 <c>await</c> 이후가 영영 실행되지 않을 수 있다.
    /// 브리지 호출 자체는 동기로 나가므로 데이터는 전달되고, 여기서 잃는 것은
    /// 실패 로그뿐이다. 그래서 <c>await</c> 뒤에 상태를 바꾸는 코드를 두지 않는다.
    /// </summary>
    public static async void Push(string payloadJson)
    {
        if (string.IsNullOrEmpty(payloadJson)) return;
        if (payloadJson == _lastPushed) return;

        string owner = SignInManager.IsValidInstance() ? SignInManager.Instance.PlayerId : string.Empty;

        string body;
        try
        {
            body = JsonUtility.ToJson(new MirrorEnvelope { owner = owner ?? string.Empty, payload = payloadJson });
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveMirror] 복제본 직렬화 실패: {e.GetType().Name} - {e.Message}");
            return;
        }

        // 낙관적으로 먼저 기록한다. 실패 시 되돌리면 재시도가 되지만, 저장은 곧 다시
        // 불리므로(점수 갱신·게임오버) 굳이 여기서 재시도 상태를 만들지 않는다.
        _lastPushed = payloadJson;

        try
        {
            await AIT.StorageSetItem(STORAGE_KEY, body, TIMEOUT_MS);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveMirror] 복제 실패 — 로컬 저장은 정상입니다. ({e.GetType().Name}: {e.Message})");
        }
    }

    /// <summary>
    /// 복제본을 읽어 <paramref name="onLoaded"/>에 payload를 넘긴다.
    /// 없거나 실패하면 호출하지 않는다 — 호출부는 아무 일도 일어나지 않은 것으로 취급하면 된다.
    /// </summary>
    public static async void Restore(Action<string> onLoaded)
    {
        if (onLoaded == null) return;

        string raw;
        try
        {
            raw = await AIT.StorageGetItem(STORAGE_KEY, TIMEOUT_MS);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveMirror] 복제본 조회 실패: {e.GetType().Name} - {e.Message}");
            return;
        }

        if (string.IsNullOrEmpty(raw)) return;   // 복제본이 아직 없다 (첫 실행 등)

        MirrorEnvelope env;
        try
        {
            env = JsonUtility.FromJson<MirrorEnvelope>(raw);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveMirror] 복제본 해석 실패: {e.GetType().Name} - {e.Message}");
            return;
        }

        if (env == null || string.IsNullOrEmpty(env.payload)) return;

        // 소유자가 서로 다른 것이 확실할 때만 막는다. 한쪽이라도 비어 있으면
        // (토스 앱 밖이거나 키를 아직 못 받은 시점) 판단 근거가 없으므로 통과시킨다.
        string owner = SignInManager.IsValidInstance() ? SignInManager.Instance.PlayerId : string.Empty;
        if (!string.IsNullOrEmpty(env.owner) && !string.IsNullOrEmpty(owner) && env.owner != owner)
        {
            Debug.Log("[SaveMirror] 다른 사용자의 복제본이라 복원하지 않습니다.");
            return;
        }

        // 같은 값을 다시 밀어내지 않도록 기준선을 맞춘다.
        _lastPushed = env.payload;

        try
        {
            onLoaded(env.payload);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[SaveMirror] 복원 처리 중 예외: {e.GetType().Name} - {e.Message}");
        }
    }
}
#endif
