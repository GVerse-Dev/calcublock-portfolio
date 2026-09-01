namespace IGMain.Ads
{
        /// <summary>
        /// 광고 단위 ID 집중 관리.
        ///
        /// 개발 중엔 TEST_xxx 사용, 출시 직전에 PROD_xxx 로 변경.
        ///
        /// ─────────────────────────────────────────────────────────────
        /// ⚠ 이 파일은 공개 저장소용 사본이다. 운영 ID는 자리표시자로 대체했다.
        ///   원본에는 AdMob·앱인토스 콘솔에서 발급받은 실제 값이 들어 있다.
        ///   (구조와 폴백 규칙은 원본과 동일하다.)
        ///
        ///   덧붙여, 이 상수 하드코딩 자체가 스스로 진단한 한계 항목이다.
        ///   docs/known-limitations.md #4 — ScriptableObject 기반 환경 설정으로
        ///   분리하고 빌드 시 주입하는 방향으로 정리 중이다.
        /// ─────────────────────────────────────────────────────────────
        /// </summary>
        public static class AdUnitIds
        {
                // ── 테스트 광고 ID (개발용) ─────────────────
                // 출처: https://developers.google.com/admob/unity/test-ads
                // 구글이 공개한 공용 테스트 ID라 그대로 둔다.

#if UNITY_ANDROID
                public const string TEST_APP_ID = "ca-app-pub-3940256099942544~3347511713";
                public const string TEST_INTERSTITIAL = "ca-app-pub-3940256099942544/1033173712";
                public const string TEST_REWARDED = "ca-app-pub-3940256099942544/5224354917";
#else
                public const string TEST_APP_ID         = "";
                public const string TEST_INTERSTITIAL   = "";
                public const string TEST_REWARDED       = "";
#endif

                // ── 프로덕션 광고 ID ────────────────────────
                // 빈 문자열이면 릴리스 빌드에서도 테스트 ID로 폴백한다.
                // 즉 "값을 안 채운 채 출시"해도 실광고가 나가지 않는다 — 의도된 안전판.

#if UNITY_ANDROID
                public const string PROD_APP_ID = "";        // ca-app-pub-XXXXXXXXXXXXXXXX~XXXXXXXXXX
                public const string PROD_INTERSTITIAL = "";  // ca-app-pub-XXXXXXXXXXXXXXXX/XXXXXXXXXX
                public const string PROD_REWARDED = "";      // ca-app-pub-XXXXXXXXXXXXXXXX/XXXXXXXXXX
#else
                public const string PROD_APP_ID         = "";
                public const string PROD_INTERSTITIAL   = "";
                public const string PROD_REWARDED       = "";
#endif

                // ── 현재 사용할 ID (디버그/릴리즈 분기) ──────

#if UNITY_EDITOR || DEBUG_ADS
                public static string AppId => TEST_APP_ID;
                public static string Interstitial => TEST_INTERSTITIAL;
                public static string Rewarded => TEST_REWARDED;
#else
                public static string AppId          => string.IsNullOrEmpty(PROD_APP_ID) ? TEST_APP_ID : PROD_APP_ID;
                public static string Interstitial   => string.IsNullOrEmpty(PROD_INTERSTITIAL) ? TEST_INTERSTITIAL : PROD_INTERSTITIAL;
                public static string Rewarded       => string.IsNullOrEmpty(PROD_REWARDED) ? TEST_REWARDED : PROD_REWARDED;
#endif

#if UNITY_WEBGL
                // ── 앱인토스 광고 그룹 ID ────────────────────
                //
                // AdMob과 달리 "앱 ID"가 없고 광고 그룹 ID 하나로 종류(전면/리워드)와
                // 노출 정책이 결정된다. 운영 ID는 콘솔에서 광고 그룹을 만들어야 발급된다.
                //
                // ⚠ **개발 중에는 반드시 테스트 ID를 쓴다.** 운영 ID로 테스트하면 제재 대상이다.
                // 아래 PROD 값이 비어 있는 동안에는 릴리스 빌드에서도 테스트 ID로 폴백하므로,
                // 실 광고를 태우려면 값을 채우는 것 외에 다른 조작이 필요 없다.

                public const string AIT_TEST_INTERSTITIAL = "ait-ad-test-interstitial-id";
                public const string AIT_TEST_REWARDED     = "ait-ad-test-rewarded-id";

                // 게임오버 전면 = INTERSTITIAL / 부활 리워드 = REWARDED(보상 「부활」 1개)
                public const string AIT_PROD_INTERSTITIAL = "";  // ait.v2.live.XXXXXXXXXXXXXXXX
                public const string AIT_PROD_REWARDED     = "";  // ait.v2.live.XXXXXXXXXXXXXXXX

#if UNITY_EDITOR || DEBUG_ADS
                public static string AitInterstitial => AIT_TEST_INTERSTITIAL;
                public static string AitRewarded     => AIT_TEST_REWARDED;
#else
                public static string AitInterstitial =>
                        string.IsNullOrEmpty(AIT_PROD_INTERSTITIAL) ? AIT_TEST_INTERSTITIAL : AIT_PROD_INTERSTITIAL;
                public static string AitRewarded =>
                        string.IsNullOrEmpty(AIT_PROD_REWARDED) ? AIT_TEST_REWARDED : AIT_PROD_REWARDED;
#endif
#endif
        }
}
