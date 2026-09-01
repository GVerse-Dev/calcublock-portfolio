using UnityEngine;
using IGMain.Design;

namespace IGMain
{
    /// <summary>
    /// 타일 값(숫자/연산자) → 색상 매핑.
    /// 팔레트가 존재하면 CurrentPalette에서, 없으면 CTColors 기본값을 사용한다.
    /// </summary>
    public static class TileColorMapper
    {
        public static Color GetColor(string value)
        {
            if (string.IsNullOrEmpty(value)) return Color.white;

            var pal = ThemeManager.IsValidInstance() ? ThemeManager.Instance.CurrentPalette : null;
            char c = value[0];

            switch (c)
            {
                case '+': return pal != null ? pal.blockAdd  : CTColors.BlockAdd;
                case '-': return pal != null ? pal.blockSub  : CTColors.BlockSub;
                case '*':
                case '×': return pal != null ? pal.blockMul  : CTColors.BlockMul;
                case '/':
                case '÷': return pal != null ? pal.blockDiv  : CTColors.BlockDiv;
            }

            if (char.IsDigit(c)) return pal != null ? pal.tileText : CTColors.BlockNumber;

            return Color.magenta;
        }

        public static Color GetColorByEnum(ETileValue tileValue)
        {
            var pal = ThemeManager.IsValidInstance() ? ThemeManager.Instance.CurrentPalette : null;

            switch (tileValue)
            {
                case ETileValue.Add:      return pal != null ? pal.blockAdd  : CTColors.BlockAdd;
                case ETileValue.Subtract: return pal != null ? pal.blockSub  : CTColors.BlockSub;
                case ETileValue.Multiply: return pal != null ? pal.blockMul  : CTColors.BlockMul;
                case ETileValue.Divede:   return pal != null ? pal.blockDiv  : CTColors.BlockDiv;

                case ETileValue.Zero:
                case ETileValue.One:
                case ETileValue.Two:
                case ETileValue.Three:
                case ETileValue.Four:
                case ETileValue.Five:
                case ETileValue.Six:
                case ETileValue.Seven:
                case ETileValue.Eight:
                case ETileValue.Nine:
                    return pal != null ? pal.tileText : CTColors.BlockNumber;

                case ETileValue.Empty:
                default:
                    return Color.white;
            }
        }
    }
}
