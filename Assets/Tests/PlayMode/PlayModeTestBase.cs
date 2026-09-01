using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using NUnit.Framework;

namespace CalculationTetris.Tests.PlayMode
{
    public abstract class PlayModeTestBase
    {
        protected const string TitleSceneName = "TitleScene";
        protected const string IGSceneName    = "IGScene";

        /// <summary>
        /// UIManager(DontDestroyOnLoad)와 HUDView 등 전역 UI는 TitleScene에서 생성된다.
        /// 실제 게임 흐름과 동일하게 TitleScene → IGScene 순서로 로드한다.
        /// </summary>
        [UnitySetUp]
        public virtual IEnumerator SetUp()
        {
            // 1. TitleScene 로드 — UIManager, HUDView 등 DontDestroyOnLoad 객체 생성
            yield return SceneManager.LoadSceneAsync(TitleSceneName);

            // Start()가 실행될 때까지 대기
            for (int i = 0; i < 5; i++)
                yield return null;

            // 2. IGScene 로드 — UIManager는 DontDestroyOnLoad로 유지됨
            yield return SceneManager.LoadSceneAsync(IGSceneName);

            // IGScene의 Start()가 실행될 때까지 대기
            for (int i = 0; i < 5; i++)
                yield return null;
        }

        [UnityTearDown]
        public virtual IEnumerator TearDown()
        {
            yield return null;
        }
    }
}
