using System.Collections;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using IGMain;

namespace CalculationTetris.Tests.PlayMode
{
    public class SmokeTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator Initialize_IGScene_LoadsCorrectly()
        {
            // IGBoardView는 런타임에 Pool에서 동적 생성됨
            var board = Object.FindAnyObjectByType<IGBoardView>();
            Assert.IsNotNull(board, "IGBoardView should exist in the scene.");

            // HUDView는 TitleScene에서 UIManager(DontDestroyOnLoad)와 함께 생성되어 유지됨
            var hud = Object.FindAnyObjectByType<HUDView>();
            Assert.IsNotNull(hud, "HUDView should exist (created in TitleScene, persisted via DontDestroyOnLoad).");

            yield return null;
        }

        [UnityTest]
        public IEnumerator Manager_Singletons_AreInitialized()
        {
            Assert.IsNotNull(IGGameManager.Instance, "IGGameManager.Instance should not be null.");
            Assert.IsNotNull(UIOverlayManager.Instance, "UIOverlayManager.Instance should not be null.");

            yield return null;
        }
    }
}
