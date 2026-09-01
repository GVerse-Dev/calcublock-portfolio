using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using System;


namespace IGMain
{

    [Serializable]
    public class IGBoardTileModel : IGTileModel
    {
        // public int Score    {get; private set;}

        // public IsEmpty      {get; private set;}

        // public IsFilled     {get; private set;}
        private void Awake()
        {
        }
        
        public override void Initialize()
        {
            base.Initialize();
        }

        public override void ResetTile()
        {
            base.ResetTile();
        }

    }
}

