
using System;
using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// 타일의 데이터를 저장하는 구조체
    ///
    /// </summary>
    /// 
    [Serializable]
    public struct TileData
    {
      /// <summary>
        ///
        /// </summary>
        public string Value;

        /// <summary>
        /// 생성자
        /// </summary>
        public TileData(string value)
        {
            Value = value;
        }

        /// <summary>
        /// 빈 데이터 (기본값)
        /// </summary>
        public static TileData Empty => new TileData("");

        /// <summary>
        /// 유효한 데이터인지 확인
        /// </summary>
        public bool IsValid => !string.IsNullOrEmpty(Value);
    }

}