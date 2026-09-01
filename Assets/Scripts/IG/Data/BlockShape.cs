using System.Collections.Generic;
using UnityEngine;
using System;


[Serializable]
public class BlockShape
{
    public IGConfig.EBlockShapeType ShapeType;
    public int[,] Shape { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public int Complexity { get; private set; }

    /// <summary>
    /// 채워진 셀의 중심 질량점 (0-indexed 셀 좌표 기준).
    /// 블록 오브젝트 로컬 좌표 (0,0)이 이 피벗에 대응한다.
    /// </summary>
    public Vector2 VisualPivot { get; private set; }

    private List<Vector2Int> _cachedRelativePositions;

    public BlockShape()
    {
        int random = UnityEngine.Random.Range(0, IGConfig.BlockTypes.Count);
        ShapeType = (IGConfig.EBlockShapeType)random;
        Shape = IGConfig.BlockTypes[ShapeType];
        Height = Shape.GetLength(0);
        Width = Shape.GetLength(1);
        Complexity = 0;
        VisualPivot = ComputeVisualPivot(Shape);
    }

    public BlockShape(IGConfig.EBlockShapeType shapeType)
    {
        // 세션 파일에서 읽은 값이 그대로 캐스팅되어 들어오는 경로가 있다.
        // BlockTypes에 없는 값(예: 딕셔너리에 미등록된 Square3, 또는 변조된 정수)이면
        // 인덱서가 KeyNotFoundException을 던져 게임 초기화 전체가 멈춘다.
        // 정의된 모양으로 대체해 진행을 보장한다.
        if (!IGConfig.BlockTypes.TryGetValue(shapeType, out var shape))
        {
            Debug.LogWarning($"[BlockShape] 정의되지 않은 블록 모양({shapeType}) — 기본 모양으로 대체합니다.");
            shapeType = IGConfig.EBlockShapeType.Dot;

            if (!IGConfig.BlockTypes.TryGetValue(shapeType, out shape))
                shape = new[,] { { 1 } };
        }

        ShapeType = shapeType;
        Shape = shape;
        Height = Shape.GetLength(0);
        Width = Shape.GetLength(1);
        Complexity = 0;
        VisualPivot = ComputeVisualPivot(Shape);
    }

    /// <summary>
    /// BlockTypes에 실제로 정의된 모양인지 확인한다.
    /// 세션 복원처럼 외부 데이터에서 온 값을 쓰기 전에 호출할 것.
    /// </summary>
    public static bool IsDefined(IGConfig.EBlockShapeType shapeType) =>
        IGConfig.BlockTypes.ContainsKey(shapeType);

    /// <summary>
    /// Shape 배열에서 채워진 셀들의 바운딩박스 중심을 계산한다.
    /// </summary>
    private static Vector2 ComputeVisualPivot(int[,] shape)
    {
        int minX = int.MaxValue, maxX = int.MinValue;
        int minY = int.MaxValue, maxY = int.MinValue;

        for (int y = 0; y < shape.GetLength(0); y++)
        {
            for (int x = 0; x < shape.GetLength(1); x++)
            {
                if (shape[y, x] == 1)
                {
                    if (x < minX) minX = x;
                    if (x > maxX) maxX = x;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }
            }
        }

        return (minX == int.MaxValue) ? Vector2.zero : new Vector2((minX + maxX) / 2f, (minY + maxY) / 2f);
    }

    /// <summary>
    /// VisualPivot 기준 상대좌표(오프셋) 목록을 반환한다.
    /// IGBlockModel.GetRelativeTilePositions() 와 SimBlock 양쪽에서 위임한다.
    /// </summary>
    public List<Vector2Int> GetRelativeTilePositions()
    {
        if (_cachedRelativePositions != null) return _cachedRelativePositions;

        _cachedRelativePositions = new List<Vector2Int>();
        if (Shape == null) return _cachedRelativePositions;

        int pivotX = Mathf.RoundToInt(VisualPivot.x);
        int pivotY = Mathf.RoundToInt(VisualPivot.y);

        for (int y = 0; y < Shape.GetLength(0); y++)
            for (int x = 0; x < Shape.GetLength(1); x++)
                if (Shape[y, x] == 1)
                    _cachedRelativePositions.Add(new Vector2Int(x - pivotX, y - pivotY));

        return _cachedRelativePositions;
    }

    public override string ToString()
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"ShapeType: {ShapeType}  Pivot: {VisualPivot}");
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
                sb.Append(Shape[y, x] == 1 ? "■ " : "□ ");
            sb.AppendLine();
        }
        return sb.ToString();
    }


}