using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;
using System;
using UniRx;
using Unity.VisualScripting;
using System.ComponentModel;
using Unity.Collections;

namespace IGMain
{
    [Serializable]
    public class IGTileModel : IGObject, IBoardTile
    {

        [SerializeField] protected TileData _tileData;

        public TileData TileData => _tileData.IsValid == false ? TileData.Empty : _tileData;

        public bool IsColide { get; set; } = false;

        public bool IsPlaceBlock => _tileData.IsValid;

        private Subject<IGTileModel> OnTileColide = new Subject<IGTileModel>();

        public IObservable<IGTileModel> OnTileColideObservable => OnTileColide.AsObservable();

        private Subject<IGTileModel> OnTileDataChanged = new Subject<IGTileModel>();

        public IObservable<IGTileModel> OnTileDataChangedObservable => OnTileDataChanged.AsObservable();


        public override void Initialize()
        {
            base.Initialize();
            ResetTile();
        }

        public virtual void SetCollide(bool isCollide)
        {
            if (IsColide == isCollide) return;

            IsColide = isCollide;
            OnTileColide.OnNext(this);
        }

        public virtual void SetTileData(TileData data)
        {
            _tileData = data;

            OnTileDataChanged.OnNext(this);
        }
        public virtual void ResetTile()
        {
            SetCollide(false);
            SetTileData(TileData.Empty);
        }

        public string GetTileValue()
        {
            return _tileData.Value;
        }




    }




}
