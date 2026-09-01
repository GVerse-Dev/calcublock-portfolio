using System.Collections.Generic;
using UnityEngine;
using DG.Tweening;
using IGMain;
using IGMain.Design;

// =============================================================
// [IGBlockController]
//
// 블록의 상태 소유 + 게임 로직을 담당한다.
// 이전에는 IGBlockManager(상태/스폰)와 IGBlockController(로직)로 분리되어 있었으나,
// 두 클래스가 강하게 결합되어 있고 Singleton 패턴이 씬 범위를 벗어나는 문제가 있어 통합했다.
//
// [초기화 / 재시작]
//   InitializeController() : 최초 1회. 스폰 위치 설정 후 첫 블록 세트 생성.
//   Reset()                 : 재시작 시. 현재 블록을 풀에 반환 후 새 세트 생성.
// =============================================================
public class IGBlockController : ControllerBase
{
    // ── 블록 상태 (이전 IGBlockManager 소유분) ──────────────────────────────

    private List<IGBlockModel> _blockList;
    private Vector2[] _spawnPositions;
    private TileValueGenerator _tileValueGenerator;

    // ── 입력 상태 ─────────────────────────────────────────────────────────────

    public bool IsBlockMoving { get; private set; }
    public IGBlockModel SelectedBlock { get; private set; }

    // ── 프로퍼티 ──────────────────────────────────────────────────────────────

    public List<IGBlockModel> BlockList => _blockList;

    // ── ControllerBase 구현 ───────────────────────────────────────────────────

    /// <summary>최초 1회 초기화. 스폰 위치를 설정하고 첫 블록 세트를 생성한다.</summary>
    public override void InitializeController()
    {
        _blockList ??= new List<IGBlockModel>();

        // Based on plan: Bottom -6.4, padding 0.56u, height 1.6u center at -5.04.
        // Screen width 7.2u, gap 0.12u, slot width 2.08u.
        _spawnPositions = new Vector2[]
        {
            new Vector2(-2.2f, -4.77f),
            new Vector2( 0.0f, -4.77f),
            new Vector2( 2.2f, -4.77f),
        };

        SpawnBlocks();
    }

    public override void UpdateController() { }

    // ── 외부 주입 ─────────────────────────────────────────────────────────────

    public void SetPhaseDataProvider(IPhaseDataProvider phaseDataProvider)
    {
        _tileValueGenerator = new TileValueGenerator(phaseDataProvider);
    }

    // ── 재시작 ───────────────────────────────────────────────────────────────

    /// <summary>
    /// 재시작 시 호출. 현재 블록 전체를 풀에 반환하고 새 세트를 생성한다.
    /// </summary>
    /// <summary>
    /// 게임오버 실패 연출 — 트레이에 남은 블록들을 무채색으로 죽인다.
    /// 보드만 회색이 되면 트레이 블록이 혼자 살아 있는 것처럼 보인다.
    ///
    /// 복원은 따로 부르지 않아도 된다: Reset()이 블록을 풀로 돌려보내고 새로 스폰하므로
    /// IGBlockTileView.Initialize 가 원래 색을 되돌린다(OnDestroy 에도 같은 정리가 있다).
    /// </summary>
    public void PlayGameOverCue()
    {
        if (_blockList == null) return;

        foreach (var block in _blockList)
        {
            if (block == null) continue;

            foreach (var view in block.GetComponentsInChildren<IGBlockTileView>(true))
                view?.PlayGameOverTint(IGConfig.GAME_OVER_GRAY_DURATION, IGConfig.GAME_OVER_GRAY);
        }
    }

    public void Reset()
    {
        ReturnAllBlocksToPool();

        SelectedBlock = null;
        IsBlockMoving = false;

        SpawnBlocks();
    }

    // ── 입력 핸들러 ───────────────────────────────────────────────────────────

    public void HandleBlockOnPointerDown(IGBlockModel block)
    {
        SelectedBlock = block;
        IsBlockMoving = true;

        // scale=1.0으로 즉시 설정해야 WorldToGridPosition 계산과 시각 위치가 일치한다.
        // 0.12s tween(0.62→1.0) 도중에는 압축된 시각 위치로 사용자가 배치하게 되어 오차 발생.
        block.transform.DOKill();
        block.transform.localScale = Vector3.one;

        SelectedBlock.OnSelected(true);
    }

    public void HandleBlockOnPointerUp(IGBlockModel block, bool canPlace, Vector2Int gridPos)
    {
        var targetBlock = SelectedBlock ?? block;

        if (targetBlock == null)
        {
            Debug.LogWarning("IGBlockController: No block selected on pointer up");
            IsBlockMoving = false;
            return;
        }

        if (canPlace)
        {
            AudioManager.Instance.Play("BlockPlace");
            RemoveAndReplaceBlock(targetBlock);
            CheckAndSpawnNewSet();
        }
        else
        {
            targetBlock.OnSelected(false);
            targetBlock.ReturnToOriginalPosition();
            AudioManager.Instance.Play("BlockInvalid");
        }

        SelectedBlock = null;
        IsBlockMoving = false;
    }

    public void HandleBlockOnPointerDrag(bool canPlace, IGBlockModel selectedBlock, Vector3 inputPosition, Vector3 selectedBlockPos)
    {
        selectedBlock.transform.position = selectedBlockPos;
    }


    // ── 좌표 변환 ─────────────────────────────────────────────────────────────

    /// <summary>
    /// 월드 좌표를 보드 그리드 좌표로 변환한다.
    /// visualPivot의 소수 부분을 보정하여 banker's rounding 오차를 제거한다.
    /// 2x2 바운딩박스 블록(SL_*, Square2)은 로컬 원점이 셀 경계 정중앙에 위치하므로
    /// RoundToInt(n + 0.5)가 홀수 열에서 1칸 밀리는 문제를 방지한다.
    /// </summary>
    public Vector2Int WorldToGridPosition(Vector3 worldPosition, Vector2 visualPivot)
    {
        float unit = 100f;
        float tileW = IGConfig.TILE_WIDTH / unit;
        float gap = IGConfig.TILE_GAP / unit;
        float step = tileW + gap;

        float boardWidth = (IGConfig.BOARD_COL * tileW) + ((IGConfig.BOARD_COL - 1) * gap);
        float boardHeight = (IGConfig.BOARD_ROW * tileW) + ((IGConfig.BOARD_ROW - 1) * gap);

        float startX = -boardWidth / 2f;
        float startY = boardHeight / 2f;

        // 피벗 소수 부분만큼 offset 보정 → RoundToInt가 일관되게 동작
        float fracX = visualPivot.x - Mathf.Floor(visualPivot.x);
        float fracY = visualPivot.y - Mathf.Floor(visualPivot.y);

        float offsetX = worldPosition.x - startX - fracX * step;
        float offsetY = startY - worldPosition.y - fracY * step;

        int gridX = Mathf.RoundToInt(offsetX / step);
        int gridY = Mathf.RoundToInt(offsetY / step);

        return new Vector2Int(gridX, gridY);
    }

    /// <summary>
    /// 블록의 상대 좌표 범위를 고려해 gridPos를 보드 경계 안으로 클램핑한다.
    /// 손가락 위치로 인해 피벗이 보드 밖을 가리켜도 블록 전체가 보드 안에 들어오도록 보정한다.
    /// </summary>
    public Vector2Int ClampGridPos(IGBlockModel block, Vector2Int gridPos)
    {
        GetBlockExtents(block, out int minRelX, out int maxRelX, out int minRelY, out int maxRelY);
        return new Vector2Int(
            Mathf.Clamp(gridPos.x, -minRelX, IGConfig.BOARD_COL - 1 - maxRelX),
            Mathf.Clamp(gridPos.y, -minRelY, IGConfig.BOARD_ROW - 1 - maxRelY)
        );
    }

    /// <summary>
    /// 블록이 보드 영역과 전혀 겹치지 않으면 true를 반환한다.
    /// 클램핑 전 raw gridPos를 전달해야 한다.
    /// </summary>
    public bool IsCompletelyOffBoard(IGBlockModel block, Vector2Int rawGridPos)
    {
        GetBlockExtents(block, out int minRelX, out int maxRelX, out int minRelY, out int maxRelY);
        return rawGridPos.x + maxRelX < 0
            || rawGridPos.x + minRelX >= IGConfig.BOARD_COL
            || rawGridPos.y + maxRelY < 0
            || rawGridPos.y + minRelY >= IGConfig.BOARD_ROW;
    }

    private static void GetBlockExtents(IGBlockModel block,
        out int minRelX, out int maxRelX, out int minRelY, out int maxRelY)
    {
        minRelX = int.MaxValue; maxRelX = int.MinValue;
        minRelY = int.MaxValue; maxRelY = int.MinValue;
        foreach (var o in block.GetRelativeTilePositions())
        {
            if (o.x < minRelX) minRelX = o.x;
            if (o.x > maxRelX) maxRelX = o.x;
            if (o.y < minRelY) minRelY = o.y;
            if (o.y > maxRelY) maxRelY = o.y;
        }
    }

    // ── 내부 : 스폰 (이전 IGBlockManager 로직) ───────────────────────────────

    private void SpawnBlocks()
    {
        for (int i = 0; i < 3; i++)
        {
            var block = CreateBlock();
            if (block == null) continue;

            var blockShape = CreateBlockShape();
            var blockTiles = CreateBlockTiles(block, blockShape);
            var blockView = block.GetComponent<IGBlockView>();

            Vector3 finalPos = _spawnPositions[i];

            block.Initialize(finalPos, blockTiles, blockShape);
            blockView.Initialize();

            // 잔여 트윈 제거 후 등장 애니메이션 시작
            block.transform.DOKill();
            block.transform.localPosition = finalPos + new Vector3(0f, 2f, 0f);
            block.transform
                .DOLocalMove(finalPos, CTAnimation.DurBase)
                .SetEase(Ease.OutBack)
                .SetDelay(i * 0.05f)
                .SetUpdate(true);

            _blockList.Add(block);
        }

        EnsureSetHasNumber();
    }

    /// <summary>
    /// 방금 생성된 블록 세트(_blockList)에 숫자(1-9) 타일이 하나도 없으면
    /// 랜덤 타일 하나를 현재 확률 분포에서 선택한 숫자로 교체한다.
    /// </summary>
    private void EnsureSetHasNumber()
    {
        if (_tileValueGenerator == null) return;

        // 세트 내 모든 타일을 수집하면서 숫자 존재 여부 확인
        var allTiles = new List<IGBlockTileModel>();
        bool hasNumber = false;

        foreach (var block in _blockList)
        {
            foreach (var tile in block.GetAllTiles())
            {
                if (tile == null) continue;
                allTiles.Add(tile);
                if (_tileValueGenerator.IsNumber(tile.GetTileValue()))
                    hasNumber = true;
            }
        }

        if (hasNumber || allTiles.Count == 0) return;

        allTiles[Random.Range(0, allTiles.Count)].SetTileValue(_tileValueGenerator.GetNumber());
    }

    private IGBlockModel CreateBlock()
    {
        var block = PoolManager.Instance.Pop<IGBlockModel>(EPoolType.Block);
        if (block == null)
        {
            Debug.LogError("IGBlockController: Failed to pop Block from pool");
            return null;
        }

        block.transform.SetParent(transform);
        return block;
    }

    private IGBlockTileModel[,] CreateBlockTiles(IGBlockModel block, BlockShape blockShape)
    {
        // 가변 크기: Shape 실제 행/열 수로 배열 생성
        var blockTiles = new IGBlockTileModel[blockShape.Height, blockShape.Width];
        Vector2 pivot = blockShape.VisualPivot;

        float unit = 100f;
        float tileW = IGConfig.TILE_WIDTH / unit;
        float gap = IGConfig.TILE_GAP / unit;

        for (int y = 0; y < blockShape.Height; y++)
        {
            for (int x = 0; x < blockShape.Width; x++)
            {
                if (blockShape.Shape[y, x] != 1) continue;

                var tile = PoolManager.Instance.Pop<IGBlockTileModel>(EPoolType.BlockTile);
                if (tile == null)
                {
                    Debug.LogError($"IGBlockController: Failed to pop BlockTile at [{y},{x}]");
                    continue;
                }

                tile.Initialize();
                tile.GetComponent<IGBlockTileView>().Initialize();

                tile.ResetTile();
                tile.SetTileValue(_tileValueGenerator.GetValue());

                tile.transform.SetParent(block.transform);
                tile.transform.name = $"Block_Tile_{y}_{x}";
                // 리셋해 항상 1:1 비율을 보장한다. (유닛 단위이므로 Vector3.one)
                tile.transform.localScale = Vector3.one;

                // 피벗(중심 질량점)을 블록 로컬 원점(0,0)에 정렬
                tile.transform.localPosition = new Vector3(
                    (x - pivot.x) * (tileW + gap),
                   -(y - pivot.y) * (tileW + gap),
                    0f);

                tile.SetIndex(x + (y * blockShape.Width));
                blockTiles[y, x] = tile;
            }
        }

        return blockTiles;
    }

    private BlockShape CreateBlockShape()
    {
        var shape = new BlockShape();

#if UNITY_EDITOR
        Debug.Log(shape.ToString());
#endif

        return shape;
    }

    /// <summary>배치된 블록을 리스트에서 제거하고 타일·블록 오브젝트를 풀에 반환한다.</summary>
    private void RemoveAndReplaceBlock(IGBlockModel placedBlock)
    {
        _blockList.Remove(placedBlock);

        // 트윈이 살아있으면 풀 반환 후 재활성 시 재개되어 충돌하므로 미리 제거
        placedBlock.transform.DOKill();

        foreach (var tile in placedBlock.GetAllTiles())
            PoolManager.Instance.Push(EPoolType.BlockTile, tile);

        placedBlock.Clear();
        PoolManager.Instance.Push(EPoolType.Block, placedBlock);
    }

    /// <summary>블록 리스트가 비어있으면 새 세트를 생성한다.</summary>
    private void CheckAndSpawnNewSet()
    {
        if (_blockList.Count > 0) return;

#if UNITY_EDITOR
        Debug.Log("IGBlockController: All blocks placed. Spawning new set.");
#endif

        SpawnBlocks();
    }

    // ── 세션 저장/복원 ────────────────────────────────────────────────────────

    public IGMain.PendingBlockSaveData[] GetSessionBlockData()
    {
        if (_blockList == null || _blockList.Count == 0)
            return System.Array.Empty<IGMain.PendingBlockSaveData>();

        var data = new IGMain.PendingBlockSaveData[_blockList.Count];
        for (int i = 0; i < _blockList.Count; i++)
        {
            var block = _blockList[i];
            var positions = block.GetRelativeTilePositions();
            var values = new string[positions.Count];
            for (int j = 0; j < positions.Count; j++)
                values[j] = block.GetTileData(positions[j].x, positions[j].y).Value;

            data[i] = new IGMain.PendingBlockSaveData
            {
                shapeType = (int)block.ShapeType,
                tileValues = values
            };
        }
        return data;
    }

    public void RestoreSessionBlocks(IGMain.PendingBlockSaveData[] data)
    {
        ReturnAllBlocksToPool();
        SelectedBlock = null;
        IsBlockMoving = false;

        if (data == null || data.Length == 0) { SpawnBlocks(); return; }

        for (int i = 0; i < data.Length && i < _spawnPositions.Length; i++)
        {
            var saved = data[i];
            var shapeType = (IGConfig.EBlockShapeType)saved.shapeType;

            // 세션 파일에서 온 정수를 그대로 캐스팅한 값이다. 정의되지 않은 모양이면
            // 복원을 통째로 실패시키지 말고 해당 블록만 건너뛴다.
            if (!BlockShape.IsDefined(shapeType))
            {
                Debug.LogWarning($"[IGBlockController] 세션의 블록 모양이 유효하지 않습니다({saved.shapeType}) — 건너뜁니다.");
                continue;
            }

            var blockShape = new BlockShape(shapeType);

            var block = CreateBlock();
            if (block == null) continue;

            var blockTiles = CreateBlockTilesWithValues(block, blockShape, saved.tileValues);
            var blockView = block.GetComponent<IGBlockView>();

            Vector3 finalPos = _spawnPositions[i];
            block.Initialize(finalPos, blockTiles, blockShape);
            blockView.Initialize();

            block.transform.DOKill();
            block.transform.localPosition = finalPos + new Vector3(0f, 2f, 0f);
            block.transform
                .DOLocalMove(finalPos, IGMain.Design.CTAnimation.DurBase)
                .SetEase(Ease.OutBack)
                .SetDelay(i * 0.05f)
                .SetUpdate(true);

            _blockList.Add(block);
        }

        // 저장된 블록이 전부 유효하지 않았거나 풀에서 못 꺼낸 경우.
        // 블록이 하나도 없으면 플레이어가 아무것도 놓을 수 없어 진행이 막힌다.
        if (_blockList.Count == 0)
        {
            Debug.LogWarning("[IGBlockController] 복원된 블록이 없습니다 — 새로 생성합니다.");
            SpawnBlocks();
        }
    }

    private IGBlockTileModel[,] CreateBlockTilesWithValues(IGBlockModel block, BlockShape blockShape, string[] values)
    {
        var blockTiles = new IGBlockTileModel[blockShape.Height, blockShape.Width];
        Vector2 pivot = blockShape.VisualPivot;

        float unit = 100f;
        float tileW = IGConfig.TILE_WIDTH / unit;
        float gap = IGConfig.TILE_GAP / unit;

        int valueIndex = 0;
        for (int y = 0; y < blockShape.Height; y++)
        {
            for (int x = 0; x < blockShape.Width; x++)
            {
                if (blockShape.Shape[y, x] != 1) continue;

                var tile = PoolManager.Instance.Pop<IGBlockTileModel>(EPoolType.BlockTile);
                if (tile == null) continue;

                tile.Initialize();
                tile.GetComponent<IGBlockTileView>().Initialize();
                tile.ResetTile();

                string raw = (values != null && valueIndex < values.Length) ? values[valueIndex] : "1";

                // 세션 파일에서 온 값이라 보드 복원과 동일하게 검증한다.
                // 이 경로를 빼놓으면 조작된 tileValues가 블록에 실려 들어오고,
                // BoardGrid.PlaceBlock이 그 값을 보드 타일로 그대로 복사해
                // 보드 쪽 화이트리스트를 통째로 우회한다.
                string value = IGMain.TileValueSanitizer.Sanitize(raw);

                // 블록 타일은 모양상 '채워진 칸'이므로 빈 값이 될 수 없다.
                // 검증에서 떨어진 값은 기존 기본값과 같게 "1"로 대체한다.
                // (공백 " " 은 정상 값이라 여기서 걸리지 않는다)
                if (string.IsNullOrEmpty(value)) value = "1";

                tile.SetTileValue(value);
                valueIndex++;

                tile.transform.SetParent(block.transform);
                tile.transform.name = $"Block_Tile_{y}_{x}";
                tile.transform.localScale = Vector3.one;

                tile.transform.localPosition = new Vector3(
                    (x - pivot.x) * (tileW + gap),
                   -(y - pivot.y) * (tileW + gap),
                    0f);

                tile.SetIndex(x + (y * blockShape.Width));
                blockTiles[y, x] = tile;
            }
        }

        return blockTiles;
    }

    /// <summary>모든 블록을 풀에 반환한다 (Reset 내부에서 사용).</summary>
    private void ReturnAllBlocksToPool()
    {
        if (_blockList == null) return;

        foreach (var block in _blockList)
        {
            if (block == null) continue;

            // 트윈이 살아있으면 풀 반환 후 재활성 시 재개되어 충돌하므로 미리 제거
            block.transform.DOKill();

            foreach (var tile in block.GetAllTiles())
            {
                if (tile != null)
                    PoolManager.Instance.Push(EPoolType.BlockTile, tile);
            }

            block.Clear();
            PoolManager.Instance.Push(EPoolType.Block, block);
        }

        _blockList.Clear();
    }
}
