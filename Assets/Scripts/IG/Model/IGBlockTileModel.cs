using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using IGMain;
using UnityEngine.UI;
using System;

namespace IGMain
{
    [Serializable]
    public class IGBlockTileModel : IGTileModel
    {

        //public IGTile NearestTile { get { return _nearestColider != null ? _nearestColider.gameObject.GetComponent<IGTile>() : null; } }

        private void OnDrawGizmos()
        {
            // Gizmos.color = Color.green;
            // Gizmos.DrawWireCube(transform.position, new Vector3(IGConfig.TILE_WIDTH_HALF, IGConfig.TILE_HEIGHT_HALF, 0));

            // Gizmos.color = Color.red;
            // if (_nearestColider != null)
            // {
            //     Gizmos.DrawWireSphere(_nearestColider.transform.position, 20f);
            // }
        }

        public override void ResetTile()
        {
            base.ResetTile();
            CreateTileData();
        }

        public virtual void CreateTileData()
        {
            SetTileData(new TileData());
        }

        public void SetTileValue(string value)
        {
            SetTileData(new TileData(value));
        }
    }


}
