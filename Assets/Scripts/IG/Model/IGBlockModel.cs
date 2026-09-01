using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using DG.Tweening;
using Unity.VisualScripting;
using static IGConfig;
using static Unity.Collections.AllocatorManager;
using UniRx;
using System;

namespace IGMain
{
    /// <summary>
    /// MVC 패턴의 Model 레이어.
    /// 블록의 상태(형태, 타일 구성, 위치)를 관리하며 순수 데이터 계층 역할을 담당한다.
    /// UniRx Subject를 통해 상태 변화를 발행하고, View와 Controller가 이를 구독하는 구조로 설계되었다.
    /// </summary>
    public class IGBlockModel : IGObject, IBlockData
    {
        // 블록을 구성하는 타일 2D 배열. [y, x] 순서 (row-major)
        [SerializeField] private IGBlockTileModel[,] _blockTiles;

        [SerializeField] private BlockShape _blockShape;

        private Vector3 _originalPosition;

        private Subject<bool> _onSelectedBlock = new Subject<bool>();

        public IObservable<bool> OnSelectedBlockObservable => _onSelectedBlock.AsObservable();

        public IGBlockTileModel[,] BlockTiles => _blockTiles;
        public Vector2 VisualPivot => _blockShape?.VisualPivot ?? Vector2.zero;
        public IGConfig.EBlockShapeType ShapeType => _blockShape?.ShapeType ?? default;

        public override void Initialize()
        {
            base.Initialize();
        }

        /// <summary>
        /// 블록을 초기화한다. 이전에 사용된 타일이 있으면 풀에 반환 후 새 데이터를 할당한다.
        /// 오브젝트 풀링으로 재사용될 때 이전 상태가 남지 않도록 Clear()를 선행한다.
        /// </summary>
        public void Initialize(Vector3 position, IGBlockTileModel[,] inBlockTiles, BlockShape inBlockShape)
        {
            base.Initialize();

            if (_blockTiles != null && _blockTiles.Length > 0)
                Clear();

            _blockTiles = inBlockTiles;
            _blockShape = inBlockShape;

            this.transform.localScale = new Vector3(0.62f, 0.62f, 1f);
            _originalPosition = position;
            this.transform.localPosition = position;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            Clear();
        }

        /// <summary>
        /// 블록이 보유한 타일들 참조를 해제한다.
        /// </summary>
        public override void Clear()
        {
            base.Clear();

            _blockShape = null;
            _blockTiles = null;
        }


        public IEnumerable<IGBlockTileModel> GetAllTiles()
        {
            if (_blockTiles == null) yield break;

            for (int y = 0; y < _blockTiles.GetLength(0); y++)
                for (int x = 0; x < _blockTiles.GetLength(1); x++)
                    if (_blockTiles[y, x] != null)
                        yield return _blockTiles[y, x];
        }

        /// <summary>
        /// 블록이 선택되었을 때 Observable을 통해 이벤트를 발행한다.
        /// Controller가 이를 구독하여 입력 흐름을 처리한다.
        /// </summary>
        public void OnSelected(bool isSelected)
        {
            _onSelectedBlock.OnNext(isSelected);
        }

        /// <summary>
        /// 블록 Shape 기준으로 타일이 존재하는 셀의 보드 배치용 오프셋 목록을 반환한다.
        /// VisualPivot의 floor를 기준으로 정규화하여 드래그 그리드 위치에 정확히 대응한다.
        /// (x: 우측, y: 아래로 증가)
        /// </summary>
        public List<Vector2Int> GetRelativeTilePositions()
        {
            if (_blockShape == null || _blockShape.Shape == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("IGBlock: _blockShape or _blockShape.Shape is null");
#endif
                return new List<Vector2Int>();
            }
            return _blockShape.GetRelativeTilePositions();
        }

        public void SetIndexByWorldToGridPosition(int inGridIndex)
        {
            if (_blockTiles == null) return;

            foreach (var tile in _blockTiles)
            {
                if (tile == null) continue;
                tile.SetIndex(inGridIndex);
            }
        }

        /// <summary>
        /// 블록을 구성하는 모든 타일의 충돌 상태를 일괄 설정한다.
        /// 드래그 중 보드 위 배치 가능 여부를 시각적으로 표시하기 위해 사용된다.
        /// </summary>
        public void SetCollisionState(bool isColliding)
        {
            if (_blockTiles == null)
            {
#if UNITY_EDITOR
                Debug.LogWarning("IGBlock: _blockTiles is null, cannot set collision state");
#endif
                return;
            }

            for (int y = 0; y < _blockTiles.GetLength(0); y++)
            {
                for (int x = 0; x < _blockTiles.GetLength(1); x++)
                {
                    if (_blockTiles[y, x] != null)
                        _blockTiles[y, x].SetCollide(isColliding);
                }
            }
        }

        /// <summary>
        /// 피벗 기준 상대 좌표 (relX, relY)에서 타일 데이터를 반환한다.
        /// PlaceBlock 내부에서 GetRelativeTilePositions() 오프셋을 그대로 전달한다.
        /// </summary>
        public TileData GetTileData(int relX, int relY)
        {
            if (_blockShape == null || _blockTiles == null)
                return TileData.Empty;

            // 피벗 상대 → Shape 배열 인덱스로 역변환 (GetRelativeTilePositions 와 동일한 RoundToInt 기준)
            int x = relX + Mathf.RoundToInt(_blockShape.VisualPivot.x);
            int y = relY + Mathf.RoundToInt(_blockShape.VisualPivot.y);

            if (y < 0 || y >= _blockTiles.GetLength(0) ||
                x < 0 || x >= _blockTiles.GetLength(1) ||
                _blockTiles[y, x] == null)
                return TileData.Empty;

            return _blockTiles[y, x].TileData;
        }

        /// <summary>
        /// 드래그 취소 또는 배치 불가 시 블록을 스폰 위치로 복귀시킨다.
        /// </summary>
        public void ReturnToOriginalPosition()
        {
            this.transform.localPosition = _originalPosition;
        }

        public void SetOriginalPosition(Vector3 position)
        {
            _originalPosition = position;
            this.transform.localPosition = position;
        }
    }
}
