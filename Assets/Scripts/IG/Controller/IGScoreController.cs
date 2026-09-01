using UnityEngine;
using IGMain.UI;

namespace IGMain
{

    /// <summary>
    /// MVC 패턴의 Controller 레이어. 게임 중 점수와 콤보를 관리한다.
    ///
    /// [역할]
    /// - ExpressionEvaluator 결과를 받아 점수 계산 (콤보 배수 적용)
    /// - ScoreModel 상태 업데이트
    /// - 게임 종료 시 SaveManager에 BestScore 저장
    ///
    /// [호출 흐름]
    /// IGBoardController.CheckAndClearLines → NotifyTurnResult(rawScore, didClear)
    ///   → 점수 계산 및 Model 업데이트 → View 자동 갱신 (Observable 구독)
    /// </summary>
    public class IGScoreController : ControllerBase, IScoreProvider
    {
        [SerializeField] private ScoreModel _scoreModel;

        private long _bestScore;

        public long CurrentScore => _scoreModel.CurrentScore;


        public ScoreModel GetModel() => _scoreModel;

        public override void InitializeController()
        {
            if (_scoreModel == null)
            {
                GameObject modelObj = new("ScoreModel");
                modelObj.transform.SetParent(transform);
                _scoreModel = modelObj.AddComponent<ScoreModel>();
            }
            _scoreModel?.Clear();
            _scoreModel.Initialize();

            var scoreView = FindAnyObjectByType<ScoreView>(FindObjectsInactive.Include);
            scoreView?.Initialize(_scoreModel);
            scoreView?.ShowPanel();

            FindAnyObjectByType<ScoreGainView>(FindObjectsInactive.Include)?.Initialize(_scoreModel);
            FindAnyObjectByType<ComboChipView>(FindObjectsInactive.Include)?.Initialize(_scoreModel);

            _bestScore = SaveManager.Instance?.GetBestScore() ?? 0;
            _scoreModel.BestScore = _bestScore;

#if UNITY_EDITOR
            Debug.Log($"ScoreController initialized. BestScore: {_bestScore}");
#endif
        }

        /// <summary>
        /// 게임 종료 시 최고 점수를 디스크에 확정한다.
        ///
        /// **갱신 여부를 여기서 판정하지 않는다.** 예전에는 `CurrentScore > _bestScore` 로 걸렀는데,
        /// `NotifyTurnResult`(104행 근처)가 화면 표시를 위해 플레이 중에 `_bestScore` 를 이미
        /// 현재 점수로 올려놓기 때문에 게임오버 시점에는 두 값이 **항상 같았다.**
        /// 즉 조건이 늘 거짓이어서 최고 점수가 **한 번도 저장되지 않았고**, 타이틀 화면의
        /// BEST 가 영원히 0으로 남았다. (오래된 결함 — 커밋 632f369 이전부터 있었다)
        ///
        /// 비교는 저장된 값을 아는 `SaveManager.UpdateBestScore` 한 곳에만 둔다.
        /// 그쪽이 `score > CurrentSaveData.BestScore` 를 확인하므로 낮은 점수로 덮이지 않는다.
        /// </summary>
        public void SaveScore()
        {
            if (_scoreModel == null) return;

            SaveManager.Instance?.UpdateBestScore(_scoreModel.CurrentScore);

#if UNITY_EDITOR
            Debug.Log($"<color=gold>ScoreController: SaveScore — current:{_scoreModel.CurrentScore} " +
                      $"stored:{SaveManager.Instance?.GetBestScore()}</color>");
#endif
        }


        public override void UpdateController()
        {
            // 매 프레임 특별한 처리 없음
        }

        /// <summary>
        /// 블록 배치 턴의 결과를 통지한다.
        /// IGBoardController.CheckAndClearLines에서 호출.
        ///
        /// clearedCount = 이번 턴에 클리어된 행/열/스퀘어 패턴 수 (점수 배수 아님, 라벨 표시용).
        /// 점수 = rawScore × 콤보 배수. 콤보 배수는 연속 클리어마다 +0.1, 최대 2.0.
        /// (첫 클리어 1.0배 → 1.1 → 1.2 … 클리어 실패 시 0으로 리셋)
        /// </summary>
        public void NotifyTurnResult(long rawScore, bool didClear, int clearedCount = 0)
        {
            if (didClear)
            {
                // 증가 전 ComboCount 기준. 10 = 1.0배, 20 = 2.0배 상한. 정수 연산으로 정밀도 보존.
                // 감점(음수 연산식)에는 콤보 배수를 적용하지 않는다 — 양수 획득 점수에만 적용.
                int   comboTenths = Mathf.Min(20, 10 + _scoreModel.ComboCount);
                bool  applyCombo  = rawScore > 0;
                long  finalScore  = applyCombo ? rawScore * comboTenths / 10 : rawScore;
                float comboMult   = applyCombo ? comboTenths / 10f : 1f;

                // 음수 연산식(차감) 결과로도 총점은 0 미만으로 내려가지 않도록 하한 적용.
                long newTotal = System.Math.Max(0L, _scoreModel.CurrentScore + finalScore);
                _scoreModel.SetScore(newTotal);
                // 감점 턴은 콤보를 유지하되 증가시키지 않는다 — 양수 획득 시에만 +1.
                if (applyCombo)
                    _scoreModel.SetCombo(_scoreModel.ComboCount + 1);
                _scoreModel.NotifyScoreAdded(finalScore);
                _scoreModel.NotifyComboChip(rawScore, comboMult, finalScore, clearedCount);

                if (newTotal > _bestScore)
                {
                    _bestScore = newTotal;
                    _scoreModel.BestScore = _bestScore;
                }

#if UNITY_EDITOR
                Debug.Log($"<color=green>Score: +{rawScore} ×{comboMult:0.0} = {finalScore} | Total: {newTotal}</color>");
#endif
            }
            else
            {
                if (_scoreModel.ComboCount > 0)
                    _scoreModel.SetCombo(0);
            }
        }

        public long GetCurrentScore() => _scoreModel?.CurrentScore ?? 0;
        public int GetComboCount() => _scoreModel?.ComboCount ?? 0;
        public long GetBestScore() => _bestScore;

        /// <summary>
        /// 저장된 세션에서 점수·콤보를 복원한다.
        ///
        /// SaveManager.LoadSession이 이미 값을 잘라내지만, 여기서 한 번 더 막는다.
        /// 이 메서드는 Model을 거쳐 곧바로 표시 계층(ScoreView → ScoreFormatterUtility)까지
        /// 값을 흘려보내는 유일한 외부 입력 경로라, 호출자가 바뀌어도 방어가 남아야 한다.
        /// </summary>
        public void RestoreScore(long score, int combo)
        {
            _scoreModel?.SetScore(System.Math.Clamp(score, 0L, SaveManager.MAX_SCORE));
            _scoreModel?.SetCombo(System.Math.Clamp(combo, 0, SaveManager.MAX_COMBO));
        }
    }
}
