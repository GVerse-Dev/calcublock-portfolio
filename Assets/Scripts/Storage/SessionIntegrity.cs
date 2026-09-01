using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

/// <summary>
/// 저장 파일의 변조를 탐지한다. sessionData.json과 gameData.json 양쪽이 쓴다.
///
/// 이름은 세션 전용으로 시작한 흔적이다(다른 파일의 주석에서 이 이름으로 참조되고 있어
/// 그대로 둔다). Sign/Verify는 payload 문자열만 받는 범용 메서드이므로 파일 종류를 모른다.
/// 두 파일이 **같은 키를 공유**하는 것은 의도다 — 위협 모델(adb push로 파일 교체)이 동일하고,
/// 키를 나눠도 공격자가 얻는 것이 달라지지 않는다.
///
/// 세이브는 <c>Application.persistentDataPath</c> = 외부 앱전용 저장소
/// (<c>/storage/emulated/0/Android/data/&lt;pkg&gt;/files</c>)에 있다. USB 디버깅만 켜면
/// 루팅 없이 <c>adb pull</c>/<c>adb push</c>로 통째 교체할 수 있고, **릴리스 빌드에서도
/// 그렇다는 것이 실기기로 확인됐다**(non-debuggable이라 <c>run-as</c>는 거부되지만 이 경로는 열려 있다).
/// 즉 파일 내용만으로는 그것을 이 앱이 썼는지 알 수 없다.
///
/// 이 방어가 성립하는 이유는 **서명 키를 PlayerPrefs(내부 저장소)에 두기 때문이다.**
/// 앱 바이너리에 상수로 박으면 IL2CPP 메타데이터에서 그대로 뽑히지만,
/// <c>/data/data/&lt;pkg&gt;/shared_prefs</c>는 루팅 없이 읽을 수 없다.
/// 따라서 adb만 가진 공격자는 세션 파일을 읽고 고칠 수는 있어도
/// **유효한 서명을 만들 수 없다.**
///
/// 키는 설치마다 무작위로 생성되므로 다른 기기에서 만든 파일도 거부된다.
///
/// **키를 잃으면 서명된 세이브도 함께 무효가 된다.** 앱 데이터 삭제는 내부·외부 앱 디렉터리를
/// 함께 지우므로 세이브도 같이 사라져 문제가 없지만, PlayerPrefs만 유실되는 경우
/// (아래 FormatException 경로, 또는 한쪽만 복원된 백업)에는 남아 있는 서명 세이브가
/// 검증에 실패해 최고 점수가 폐기된다. "키 유실"과 "변조"는 원리상 구별할 수 없어
/// 감수하는 위험이다 — 무결성을 얻는 대가이고, 도달 조건이 극히 좁다.
///
/// **이것은 치팅 방지이지 기밀 보호가 아니다.** 세션 내용은 여전히 평문으로 읽힌다.
/// 숨겨야 할 값이 생기면 그때는 암호화가 따로 필요하다.
/// </summary>
internal static class SessionIntegrity
{
    private const string PrefKey = "Session.HmacKey";
    private const int    KeyBytes = 32;

    private static byte[] _key;

    /// <summary>서명 키를 얻는다. 없으면 만들어 저장한다.</summary>
    private static byte[] GetKey()
    {
        if (_key != null) return _key;

        string stored = PlayerPrefs.GetString(PrefKey, string.Empty);
        if (!string.IsNullOrEmpty(stored))
        {
            try
            {
                var decoded = Convert.FromBase64String(stored);
                if (decoded.Length == KeyBytes)
                {
                    _key = decoded;
                    return _key;
                }
            }
            catch (FormatException)
            {
                // 저장된 값이 깨졌다. 아래에서 새로 만든다.
                // 기존 세션은 검증에 실패해 버려지는데, 그게 안전한 기본값이다.
            }
        }

        _key = CreateKey();
        PlayerPrefs.SetString(PrefKey, Convert.ToBase64String(_key));
        PlayerPrefs.Save();
        return _key;
    }

    private static byte[] CreateKey()
    {
        var key = new byte[KeyBytes];

        try
        {
            using (var rng = RandomNumberGenerator.Create())
                rng.GetBytes(key);

            return key;
        }
        catch (Exception e)
        {
            // 매니지드 스트리핑이 암호화 RNG 구현을 걷어낸 경우 등. 폴백을 둔다.
            //
            // Guid.NewGuid()는 v4(무작위)라 둘을 합치면 200비트 이상의 엔트로피가 나온다.
            // 이 키는 공격자가 애초에 읽을 수 없는 곳에 있으므로, 요구되는 성질은
            // "오프라인에서 추측 불가"뿐이고 그건 이것으로 충분하다.
            Debug.LogWarning($"[SessionIntegrity] 암호화 RNG 사용 불가, 폴백 사용: {e.GetType().Name}");

            Guid.NewGuid().ToByteArray().CopyTo(key, 0);
            Guid.NewGuid().ToByteArray().CopyTo(key, 16);
            return key;
        }
    }

    /// <summary>payload의 서명을 만든다.</summary>
    public static string Sign(string payload)
    {
        using (var hmac = new HMACSHA256(GetKey()))
            return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(payload ?? string.Empty)));
    }

    /// <summary>서명이 payload와 맞는지 확인한다.</summary>
    public static bool Verify(string payload, string signature)
    {
        if (string.IsNullOrEmpty(signature)) return false;

        string expected = Sign(payload);
        if (expected.Length != signature.Length) return false;

        // 첫 불일치에서 빠져나오지 않는다 — 비교 시간이 일치 길이에 비례하면
        // 서명을 한 글자씩 맞춰 나갈 수 있다. 로컬 공격이라 실익은 작지만 비용도 0이다.
        int diff = 0;
        for (int i = 0; i < expected.Length; i++)
            diff |= expected[i] ^ signature[i];

        return diff == 0;
    }
}
