using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.TestTools;
using NUnit.Framework;
using IGMain;

namespace CalculationTetris.Tests.PlayMode
{
    public class GameplayTests : PlayModeTestBase
    {
        [UnityTest]
        public IEnumerator Block_Spawning_GeneratesThreeBlocks()
        {
            // Wait for controllers to initialize
            yield return new WaitForSeconds(0.5f);

            var blockController = GameObject.FindObjectOfType<IGBlockController>();
            Assert.IsNotNull(blockController, "IGBlockController should exist.");
            
            // Check if 3 blocks are in the list
            Assert.AreEqual(3, blockController.BlockList.Count, "Initially, 3 blocks should be spawned.");
            
            // Verify each block has some data
            foreach (var block in blockController.BlockList)
            {
                Assert.IsNotNull(block.BlockTiles, "Spawned block should have tiles.");
            }
        }

        [UnityTest]
        public IEnumerator Board_Model_Initialization()
        {
            var boardModel = GameObject.FindObjectOfType<IGBoardModel>();
            Assert.IsNotNull(boardModel, "IGBoardModel should exist.");
            
            // Verify board exists by checking occupation (should be false for 0,0 initially)
            bool isOccupied = boardModel.IsTileOccupied(0, 0);
            Assert.IsFalse(isOccupied, "Tile (0,0) should be empty initially.");
            
            yield return null;
        }
    }
}
