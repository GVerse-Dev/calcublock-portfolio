using System;
using UniRx;
using UnityEngine;

namespace IGMain
{
    /// <summary>
    /// 게임 중 점수와 콤보 상태를 관리하는 데이터 모델.
    /// Observable을 통해 View와 통신하며, Controller가 상태를 업데이트한다.
    /// </summary>
    public class ScoreModel : IGObject
    {
        /// <summary>라인 클리어 시 연산식 칩 표시용 데이터.</summary>
        public struct ComboChipData
        {
            public long  baseScore;   // ExpressionEvaluator 결과 (한 턴에 사라진 수의 합)
            public float mult;        // 콤보 배수 (1.0·1.1 … 최대 2.0)
            public long  total;       // 실제 추가 점수 = baseScore × mult
            public int   clearedCount;// 한 턴에 지워진 라인/스퀘어 수 (라벨 텍스트 결정용)
        }

        private long _currentScore;
        private int _comboCount;
        private long _bestScore;

        private readonly Subject<long>          _onScoreChanged    = new();
        private readonly Subject<int>           _onComboChanged    = new();
        private readonly Subject<long>          _onBestScoreChanged= new();
        private readonly Subject<long>          _onScoreAdded      = new();
        private readonly Subject<ComboChipData> _onComboChip       = new();

        public long BestScore
        {
            get => _bestScore;
            set
            {
                if (_bestScore != value)
                {
                    _bestScore = value;
                    _onBestScoreChanged.OnNext(_bestScore);
                }
            }
        }
        public long CurrentScore => _currentScore;
        public int ComboCount => _comboCount;

        public IObservable<long>          OnScoreChangedObservable    => _onScoreChanged.AsObservable();
        public IObservable<int>           OnComboChangedObservable    => _onComboChanged.AsObservable();
        public IObservable<long>          OnBestScoreChangedObservable=> _onBestScoreChanged.AsObservable();
        public IObservable<long>          OnScoreAddedObservable      => _onScoreAdded.AsObservable();
        /// <summary>라인 클리어 시 연산식 칩 데이터를 발행. ScoreView·ComboChipView가 구독.</summary>
        public IObservable<ComboChipData> OnComboChipObservable       => _onComboChip.AsObservable();

        public override void Initialize()
        {
            base.Initialize();
            _currentScore = 0;
            _comboCount = 0;
            _bestScore = 0;
        }

        public void SetScore(long score)
        {
            _currentScore = score;
            _onScoreChanged.OnNext(_currentScore);
        }

        public void SetCombo(int combo)
        {
            _comboCount = combo;
            _onComboChanged.OnNext(_comboCount);
        }

        /// <summary>IGScoreController가 finalScore 확정 후 호출.</summary>
        public void NotifyScoreAdded(long delta) => _onScoreAdded.OnNext(delta);

        /// <summary>라인 클리어 연산식 칩 데이터 발행.</summary>
        public void NotifyComboChip(long baseScore, float mult, long total, int clearedCount)
            => _onComboChip.OnNext(new ComboChipData { baseScore = baseScore, mult = mult, total = total, clearedCount = clearedCount });

        public override void Clear()
        {
            base.Clear();
            _currentScore = 0;
            _comboCount = 0;
        }

        public override void OnDestroy()
        {
            base.OnDestroy();
            _onScoreChanged?.Dispose();
            _onComboChanged?.Dispose();
            _onBestScoreChanged?.Dispose();
            _onScoreAdded?.Dispose();
            _onComboChip?.Dispose();
        }
    }
}
