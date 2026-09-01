using System.Collections;
using System.Collections.Generic;
using IGMain.Ads;
using UnityEngine;

namespace IGMain
{
    public class IGGameController : ControllerBase
    {
        public IGBlockController _blockController { private set; get; }
        public IGBoardController _boardController { private set; get; }
        public IGInputController _inputController { private set; get; }
        public IGScoreController _scoreController { private set; get; }
        // ScoreToast 시스템 제거됨 — ComboChipView + ScoreView(+N)로 대체

        // ── 부활 상태 ─────────────────────────────────────────────────────────
        private bool _hasRevived;

        // ── 드래그 상태 캐시 ──────────────────────────────────────────────────
        private Vector2Int _lastDragGridPos = new Vector2Int(int.MinValue, int.MinValue);

        // ── 게임오버 지연 체크 ────────────────────────────────────────────────
        private Coroutine _gameOverCheckCoroutine;

        /// <summary>이번 판에서 아직 부활하지 않았으면 true. GameOverView가 버튼 표시 여부 판단에 사용.</summary>
        public bool CanRevive => !_hasRevived;

#if UNITY_EDITOR
        private Vector3 _dbg_selectedBlockPos;
        private Vector3 _dbg_gridWorldPos;
        private bool _dbg_isDragging;

        private void OnDrawGizmos()
        {
            DrawDragGizmos();
            DrawPlaceablePositionsGizmos();
        }

        private void DrawDragGizmos()
        {
            if (!_dbg_isDragging) return;

            const float size = 1f;

            Gizmos.color = Color.red;
            Gizmos.DrawWireCube(_dbg_selectedBlockPos, new Vector3(size, size, 0f));

            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(_dbg_gridWorldPos, new Vector3(size, size, 0f));
        }

        // 선택된 블록을 놓을 수 있는 모든 보드 위치를 씬뷰에 표시한다.
        // 초록 = 배치 가능, 파랑 = 현재 드래그 중인 그리드 셀
        private void DrawPlaceablePositionsGizmos()
        {
            if (_blockController == null || _boardController == null) return;

            var selected = _blockController.SelectedBlock;
            if (selected == null) return;

            float unit    = 100f;
            float tileW   = IGConfig.TILE_WIDTH  / unit;   // 0.68
            float gap     = IGConfig.TILE_GAP    / unit;   // 0.03
            float step    = tileW + gap;                   // 0.71
            float bW      = IGConfig.BOARD_COL * tileW + (IGConfig.BOARD_COL - 1) * gap;
            float bH      = IGConfig.BOARD_ROW * tileW + (IGConfig.BOARD_ROW - 1) * gap;
            float startX  = -bW / 2f;
            float startY  =  bH / 2f;

            var tileSize = new Vector3(tileW * 0.9f, tileW * 0.9f, 0f);

            // 모든 보드 위치에서 배치 가능 여부 확인 후 표시
            for (int gy = 0; gy < IGConfig.BOARD_ROW; gy++)
            {
                for (int gx = 0; gx < IGConfig.BOARD_COL; gx++)
                {
                    var gridPos = new Vector2Int(gx, gy);
                    bool canPlace = _boardController.CanPlaceBlockAtPosition(selected, gridPos);
                    if (!canPlace) continue;

                    // 피벗의 월드 중심 좌표
                    float wx = startX + gx * step + tileW * 0.5f;
                    float wy = startY - gy * step - tileW * 0.5f;

                    // 피벗 위치 (초록)
                    Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
                    Gizmos.DrawCube(new Vector3(wx, wy, 0f), tileSize);

                    // 실제 타일이 놓일 셀들 (밝은 초록 아웃라인)
                    Gizmos.color = new Color(0f, 1f, 0.4f, 0.8f);
                    foreach (var rel in selected.GetRelativeTilePositions())
                    {
                        float tx = startX + (gx + rel.x) * step + tileW * 0.5f;
                        float ty = startY - (gy + rel.y) * step - tileW * 0.5f;
                        Gizmos.DrawWireCube(new Vector3(tx, ty, 0f), tileSize);
                    }
                }
            }

            // 현재 드래그 중인 그리드 셀 강조 (파랑)
            if (_dbg_isDragging)
            {
                var raw = _blockController.WorldToGridPosition(_dbg_gridWorldPos, selected.VisualPivot);
                var clamped = _blockController.ClampGridPos(selected, raw);
                float cx = startX + clamped.x * step + tileW * 0.5f;
                float cy = startY - clamped.y * step - tileW * 0.5f;
                Gizmos.color = new Color(0f, 0.5f, 1f, 0.9f);
                Gizmos.DrawWireCube(new Vector3(cx, cy, 0f), tileSize * 1.2f);
            }
        }
#endif

        // ── 초기화 ────────────────────────────────────────────────────────────

        public override void InitializeController()
        {
            // 점수
            _scoreController = CreateObj<IGScoreController>(transform, true, "InGame");
            _scoreController.InitializeController();

            // 보드 (상태 + 로직 통합)
            _boardController = CreateObj<IGBoardController>(transform, true, "InGame");
            _boardController.SetScoreController(_scoreController);
            _boardController.InitializeController();

            // 블록 (상태 + 로직 통합)
            _blockController = CreateObj<IGBlockController>(transform, true, "InGame");
            _blockController.SetPhaseDataProvider(_boardController);
            _blockController.InitializeController();

            // 입력
            _inputController = CreateObj<IGInputController>(transform, true, "InGame");
            _inputController.InitializeController();

            // 이벤트 구독
            _inputController.OnBlockSelected += HandleBlockOnPointerDown;
            _inputController.OnBlockReleased += HandleBlockOnPointerUp;
            _inputController.OnBlockDragged += HandleBlockOnPointerDrag;

            if (GameStateManager.IsValidInstance())
            {
                GameStateManager.Instance.OnRestartRequested += RestartGame;
                GameStateManager.Instance.OnReviveRequested += ReviveGame;
                GameStateManager.Instance.OnForfeitRequested += ForfeitGame;
            }

#if UNITY_WEBGL
            // 구독 전에 전제 두 가지를 직접 세운다. 둘 중 하나만 빠져도 이벤트는
            // 조용히 오지 않는다 — 예외도 로그도 없어서 실기기에서야 드러난다.
            //
            // 1) SDK의 jslib은 visibilitychange를 SendMessage("AITCore", ...)로 던진다.
            //    그 이름의 GameObject는 AITCore.Instance를 처음 건드릴 때 생기고,
            //    SDK 안에서 대신 만들어 주는 곳은 없다(PerformanceLogger는 별도 extern을
            //    쓴다). 아직 광고 등 다른 AIT API를 쓰지 않으므로 여기서 깨워야 한다.
            // 2) jslib의 DOM 리스너 등록 자체가 IsVisible 게터에서 지연 수행된다.
            //    구독만으로는 리스너가 붙지 않는다.
            _ = AppsInToss.AITCore.Instance;
            _ = AppsInToss.AITVisibilityHelper.IsVisible;

            AppsInToss.AITVisibilityHelper.OnVisibilityChanged += HandleVisibilityChanged;
#endif

            TryRestoreSession();
        }

        public override void UpdateController() { }

        private void OnDestroy()
        {
#if UNITY_WEBGL
            // static 이벤트다. 여기서 못 떼면 파괴된 컨트롤러가 계속 불려온다.
            AppsInToss.AITVisibilityHelper.OnVisibilityChanged -= HandleVisibilityChanged;
#endif

            if (GameStateManager.IsValidInstance())
            {
                GameStateManager.Instance.OnRestartRequested -= RestartGame;
                GameStateManager.Instance.OnReviveRequested -= ReviveGame;
                GameStateManager.Instance.OnForfeitRequested -= ForfeitGame;
            }

            if (_inputController != null)
            {
                _inputController.OnBlockSelected -= HandleBlockOnPointerDown;
                _inputController.OnBlockReleased -= HandleBlockOnPointerUp;
                _inputController.OnBlockDragged -= HandleBlockOnPointerDrag;
            }
        }

        // ── 입력 핸들러 ───────────────────────────────────────────────────────

        public void HandleBlockOnPointerDown(IGBlockModel selectedBlock)
        {
            _boardController.HandleBlockOnPointerDown(selectedBlock);
            _blockController.HandleBlockOnPointerDown(selectedBlock);
        }

        public void HandleBlockOnPointerUp(IGBlockModel selectedBlock, Vector3 inputPosition)
        {
            _lastDragGridPos = new Vector2Int(int.MinValue, int.MinValue);
            _boardController.ClearAllHighlights();

            var selectedBlockPos = inputPosition + new Vector3(0f, 0.68f * 3f, 0f);
            var rawGridPos = _blockController.WorldToGridPosition(selectedBlockPos, selectedBlock.VisualPivot);

            // 보드 밖에서 손 뗀 경우(빠른 탭 포함) → 원위치 복귀
            if (_blockController.IsCompletelyOffBoard(selectedBlock, rawGridPos))
            {
                _blockController.HandleBlockOnPointerUp(selectedBlock, canPlace: false, Vector2Int.zero);
                return;
            }

            var gridPos = _blockController.ClampGridPos(selectedBlock, rawGridPos);
            var canPlace = _boardController.CanPlaceBlockAtPosition(selectedBlock, gridPos);

            _boardController.HandleBlockOnPointerUp(selectedBlock, canPlace, gridPos);
            _blockController.HandleBlockOnPointerUp(selectedBlock, canPlace, gridPos);

            // 클리어 애니메이션(최대 ~0.89s)이 완료된 후 체크해야 플레이어가 시각적으로
            // 클리어 후 게임오버임을 인지할 수 있다. 클리어 없으면 즉시 체크.
            if (canPlace && _boardController.LastClearedCount > 0)
                ScheduleCheckGameOver(1.0f);
            else
                ScheduleCheckGameOver();
        }

        public void HandleBlockOnPointerDrag(IGBlockModel selectedBlock, Vector3 inputPosition)
        {
            var selectedBlockPos = inputPosition + new Vector3(0f, 0.68f * 3f, 0f);

            // 블록 위치는 항상 손가락 따라 자연스럽게 이동
            _blockController.HandleBlockOnPointerDrag(false, selectedBlock, inputPosition, selectedBlockPos);

            // 하이라이트/체크는 nearest 그리드 셀 기준 (셀이 바뀔 때만 갱신)
            var rawGridPos = _blockController.WorldToGridPosition(selectedBlockPos, selectedBlock.VisualPivot);

            if (_blockController.IsCompletelyOffBoard(selectedBlock, rawGridPos))
            {
                if (_lastDragGridPos.x != int.MinValue)
                {
                    _lastDragGridPos = new Vector2Int(int.MinValue, int.MinValue);
                    _boardController.ClearAllHighlights();
                }
            }
            else
            {
                var gridPos = _blockController.ClampGridPos(selectedBlock, rawGridPos);
                if (gridPos != _lastDragGridPos)
                {
                    _lastDragGridPos = gridPos;

                    var canPlace = _boardController.CanPlaceBlockAtPosition(selectedBlock, gridPos);
                    selectedBlock.SetIndexByWorldToGridPosition(_boardController.GetGridIndex(gridPos));
                    _boardController.HandleBlockOnPointerDrag(canPlace, selectedBlock, gridPos);
                }
            }

#if UNITY_EDITOR
            _dbg_selectedBlockPos = inputPosition;
            _dbg_gridWorldPos = selectedBlockPos;
            _dbg_isDragging = true;
#endif
        }

        // ── 재시작 ────────────────────────────────────────────────────────────

        // ── 세션 저장/복원 ────────────────────────────────────────────────────

        /// <summary>
        /// 진행 중이던 세션을 복원한다.
        ///
        /// 복원 중 예외가 나가면 InitializeController가 중단되어 StartGame이 통째로 스킵되고,
        /// GameState가 Playing으로 바뀌지 않아 HUD조차 뜨지 않는다. 세션 파일은 게임오버/재시작
        /// 때만 지워지므로 앱을 다시 켜도 같은 지점에서 계속 실패한다 — 앱 데이터 삭제 외에는
        /// 빠져나올 수 없다. 그래서 실패 시 세션을 버리고 새 판으로 진행한다.
        /// </summary>
        private void TryRestoreSession()
        {
            // 일단 새 판으로 잡아 둔다. 복원에 성공하면 아래에서 측정 대상에서 뺀다 —
            // 복원 가능 여부는 실제로 시도해 봐야 알 수 있어서 순서가 이렇게 된다.
            Telemetry.NoteGameStart();

            if (!SaveManager.IsValidInstance()) return;

            var session = SaveManager.Instance.LoadSession();
            if (session == null || !session.hasActiveSession) return;

            try
            {
                _boardController.RestoreSessionBoardData(session.boardTiles);
                _blockController.RestoreSessionBlocks(session.pendingBlocks);
                _scoreController.RestoreScore(session.currentScore, session.comboCount);

                // 부활 소진 여부도 복원한다. 이게 없으면 앱을 다시 켜는 것만으로
                // 부활 1회 제한이 초기화되어 광고 한 번으로 무제한 부활이 된다.
                _hasRevived = session.hasRevived;

                // 복원된 판은 이전 배치 수를 알 수 없어 첫 소거 턴 수를 셀 수 없다.
                Telemetry.NoteGameRestored();
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[Session] 복원 실패 — 세션을 버리고 새 판으로 시작합니다. ({e.GetType().Name}: {e.Message})");
                SaveManager.Instance.ClearSession();
                ResetToNewSession();
                return;
            }

#if UNITY_EDITOR
            Debug.Log($"<color=cyan>[Session] Restored — score:{session.currentScore} combo:{session.comboCount}</color>");
#endif
        }

        /// <summary>
        /// 세션 복원이 실패한 뒤 깨끗한 새 판 상태로 되돌린다.
        /// 반쯤 복원된 보드/블록이 남지 않도록 각 컨트롤러를 빈 상태로 다시 세운다.
        /// </summary>
        private void ResetToNewSession()
        {
            try
            {
                // 재시작 경로와 동일한 초기화를 재사용한다.
                _boardController.Reset();
                _blockController.Reset();
                _scoreController.RestoreScore(0, 0);
            }
            catch (System.Exception e)
            {
                // 여기서도 실패하면 더 할 수 있는 게 없다. 최소한 예외가 밖으로 나가지는 않게 한다.
                Debug.LogError($"[Session] 새 판 초기화 실패: {e.Message}");
            }
        }

        public void SaveCurrentSession()
        {
            if (!SaveManager.IsValidInstance()) return;

            var state = GameStateManager.IsValidInstance()
                ? GameStateManager.Instance.CurrentState
                : GameState.Playing;

            if (state == GameState.GameOver || state == GameState.MainMenu) return;

            var session = new GameSessionData
            {
                hasActiveSession = true,
                currentScore     = _scoreController.GetCurrentScore(),
                comboCount       = _scoreController.GetComboCount(),
                boardTiles       = _boardController.GetSessionBoardData(),
                pendingBlocks    = _blockController.GetSessionBlockData(),
                hasRevived       = _hasRevived
            };

            SaveManager.Instance.SaveSession(session);

#if UNITY_EDITOR
            Debug.Log($"<color=cyan>[Session] Saved — score:{session.currentScore}</color>");
#endif
        }

        private void OnApplicationPause(bool isPaused)
        {
            if (isPaused)
                SaveProgress();
        }

        private void OnApplicationQuit()
        {
            SaveProgress();
        }

#if UNITY_WEBGL
        /// <summary>
        /// WebGL에는 <c>OnApplicationPause</c>가 오지 않고, 탭/앱을 그냥 닫으면
        /// <c>OnApplicationQuit</c>도 오지 않는다. 저장 시점을 두 경로로 확보한다.
        ///
        /// 1) 이 콜백 — Unity WebGL 프레임워크는 window의 <c>blur</c>/<c>focus</c>만 듣는다
        ///    (빌드된 framework.js 확인). 데스크톱 탭 전환은 잡히지만 모바일 웹뷰가
        ///    백그라운드로 내려갈 때 blur가 온다는 보장은 없다.
        /// 2) <see cref="HandleVisibilityChanged"/> — AIT SDK가 직접 document의
        ///    visibilitychange를 듣는다. 토스 앱이 내려가는 실제 경로가 이쪽이다.
        ///
        /// 두 경로가 같은 전환에서 겹쳐 불릴 수 있으나 SaveProgress는 현재 상태를 다시
        /// 쓸 뿐이라 중복이 무해하다. 세션 일련번호가 하나 더 올라갈 뿐이다.
        ///
        /// ⚠ 문서가 화면에서 사라지는 순간 requestAnimationFrame이 멈추고 player loop도
        /// 함께 선다. 저장은 반드시 이 호출 안에서 끝나야 한다 — 코루틴이나 다음 프레임에
        /// 미루면 그 프레임이 오지 않는다.
        /// </summary>
        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus)
                SaveProgress();
        }

        private void HandleVisibilityChanged(bool isVisible)
        {
            // SDK가 구독자별 예외 격리를 하지 않는다. 여기서 예외가 나가면 같은 이벤트를
            // 듣는 다른 구독자(사운드 등)가 통째로 누락된다.
            try
            {
                if (this == null) return;   // 씬을 벗어난 뒤 도착한 이벤트
                if (!isVisible)
                    SaveProgress();
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[Session] 가시성 전환 저장 실패 — 진행은 계속합니다. ({e.GetType().Name}: {e.Message})");
            }
        }
#endif

        /// <summary>
        /// 백그라운드 전환·종료 시 진행 상황을 확정한다.
        ///
        /// 세션만 저장하면 **타이틀 화면의 BEST 가 낡은 값으로 보인다.** 최고 점수는
        /// 게임오버나 홈 버튼(ForfeitGame)에서만 확정되므로, 시스템 홈으로 나간 경우
        /// 진행 중이던 판의 최고 기록이 디스크에 없다. 다시 켜면 타이틀이 이전 값을 읽는다.
        /// (판 자체는 세션 복원으로 이어지므로 점수를 잃지는 않지만 표시가 어긋난다)
        ///
        /// SaveScore 는 SaveManager 가 저장된 값과 비교하므로 낮은 점수로 덮이지 않는다.
        /// </summary>
        private void SaveProgress()
        {
            SaveCurrentSession();
            _scoreController?.SaveScore();
        }

        // ── 재시작 ────────────────────────────────────────────────────────────

        public void RestartGame()
        {
            // 게임오버 화면에서 눌린 재시작만 '다시하기'로 센다(일시정지 재시작 제외).
            // NoteGameStart 가 판 단위 상태를 리셋하기 전에 불러야
            // 복원판 제외(_tracked)가 방금 끝난 판 기준으로 판정된다.
            if (GameStateManager.IsValidInstance() &&
                GameStateManager.Instance.CurrentState == GameState.GameOver)
                Telemetry.NoteRetry();

            Telemetry.NoteGameStart();

            CancelPendingGameOverCheck();
            _hasRevived = false;
            if (GameStateManager.IsValidInstance())
                GameStateManager.Instance.SetReviveAvailable(false);

            // 이어하기 세션 삭제 (새 게임이므로 복원하지 않는다)
            if (SaveManager.IsValidInstance())
                SaveManager.Instance.ClearSession();

            // 보드 타일 상태 초기화 (오브젝트 재생성 없음)
            _boardController.Reset();

            // 블록 풀 반환 + 새 세트 스폰
            _blockController.Reset();

            // 점수 초기화
            _scoreController.InitializeController();

            // 난이도 리셋
            if (DifficultyManager.IsValidInstance())
                DifficultyManager.Instance.ResetDifficulty();

            // 게임 상태 복원 (내부에서 Time.timeScale 복원)
            if (GameStateManager.IsValidInstance())
                GameStateManager.Instance.SetGameState(GameState.Playing);
        }

        /// <summary>
        /// 리워드 광고 시청 완료 후 부활 처리.
        /// 보드와 블록만 리셋하고 점수는 유지한다. 판당 1회만 허용.
        /// </summary>
        public void ReviveGame()
        {
            if (_hasRevived) return;
            CancelPendingGameOverCheck();
            _hasRevived = true;

            _boardController.Reset();
            _blockController.Reset();
            // 점수는 _scoreController.InitializeController() 호출하지 않아 유지

            if (GameStateManager.IsValidInstance())
            {
                GameStateManager.Instance.SetReviveAvailable(false);
                GameStateManager.Instance.SetGameState(GameState.Playing);
            }

            // 부활을 썼다는 사실을 즉시 디스크에 남긴다.
            // 다음 일시정지까지 미루면, 부활 직후 앱을 강제 종료했을 때
            // hasRevived=false 인 세션이 남아 부활 1회 제한이 초기화된다.
            SaveCurrentSession();
        }

        /// <summary>
        /// 플레이 도중 홈으로 나가기 — 판을 포기하는 것으로 간주해 게임오버와 동일하게
        /// 점수를 확정하고 이어하기 세션을 정리한 뒤 메인 메뉴로 이동한다.
        /// </summary>
        private void ForfeitGame()
        {
            CancelPendingGameOverCheck();

            if (SaveManager.IsValidInstance())
            {
                SaveManager.Instance.ClearSession();
                SaveManager.Instance.IncrementGamesPlayed();
            }

            _scoreController.SaveScore();

            if (GameStateManager.IsValidInstance())
                GameStateManager.Instance.RequestGoToMainMenu();
        }

        // ── 게임오버 체크 ─────────────────────────────────────────────────────

        private void ScheduleCheckGameOver(float delay = 0f)
        {
            CancelPendingGameOverCheck();

            if (delay <= 0f)
                CheckGameOver();
            else
                _gameOverCheckCoroutine = StartCoroutine(CheckGameOverAfterDelay(delay));
        }

        private void CancelPendingGameOverCheck()
        {
            if (_gameOverCheckCoroutine != null)
            {
                StopCoroutine(_gameOverCheckCoroutine);
                _gameOverCheckCoroutine = null;
            }
        }

        private IEnumerator CheckGameOverAfterDelay(float delay)
        {
            yield return new WaitForSecondsRealtime(delay);
            _gameOverCheckCoroutine = null;
            CheckGameOver();
        }

        private void CheckGameOver()
        {
            if (GameStateManager.IsValidInstance() &&
                GameStateManager.Instance.CurrentState != GameState.Playing)
                return;

            var blocks = _blockController.BlockList;

            if (blocks == null || blocks.Count == 0) return;

            if (!_boardController.IsAnyBlockPlaceable(blocks))
            {
                // **점수를 가장 먼저 확정한다.** 주석과 코드가 어긋나 있었다 —
                // 예전에는 ClearSession·IncrementGamesPlayed 가 앞에 있었고, 그 둘은
                // PlayerPrefs 를 만지므로 예외가 날 수 있다. 그러면 최고 기록 저장부터
                // 상태 전이·팝업까지 전부 취소되어 진행 불가 상태가 된다.
                // 가장 잃으면 안 되는 것을 가장 먼저, 실패해도 흐름이 이어지도록 한다.
                _scoreController.SaveScore();

                // 계측은 저장 뒤에 둔다. 위 원칙 그대로 — 진단 하나 때문에 최고 기록이
                // 날아가면 안 된다. (Telemetry 내부도 예외를 밖으로 내보내지 않는다)
                Telemetry.NoteGameOver();

                // 게임오버 확정 — 이어하기 세션 삭제 (내부적으로 예외를 삼킨다)
                if (SaveManager.IsValidInstance())
                {
                    SaveManager.Instance.ClearSession();
                    SaveManager.Instance.IncrementGamesPlayed();
                }

                // 전면 광고 **집계**도 여기서 동기로 끝낸다. 노출은 연출 뒤로 미루지만
                // 집계까지 미루면 게임오버 직후 앱이 죽는 것만으로 카운터가 증가하지 않아
                // 전면 광고가 영구히 사라진다(1.2초 창). 집계와 노출은 분리해야 한다.
                if (AdManager.IsValidInstance())
                    AdManager.Instance.NotifyGameOver();

                if (GameStateManager.IsValidInstance())
                {
                    // SetReviveAvailable을 먼저 설정 — SetGameState가 OnGameStateChanged를
                    // 동기 발화하므로 부활 버튼 활성화 판정에 이 값이 이미 필요하다.
                    GameStateManager.Instance.SetReviveAvailable(!_hasRevived);

                    // 상태 전이는 **즉시** 한다. 결과 팝업은 HUDView가 연출 시간만큼 늦춰서
                    // 열지만, 상태가 Playing 을 벗어나야 그 사이 입력이 막힌다
                    // (IGInputController와 HUDView.OnPauseClicked 모두 Playing 만 통과시킨다).
                    GameStateManager.Instance.SetGameState(GameState.GameOver);
                }

                // 실패 연출 — 보드를 흔들고, 보드·트레이 타일을 무채색으로 죽인다.
                _boardController?.PlayGameOverCue();
                _blockController?.PlayGameOverCue();

                // 전면 광고는 연출이 끝난 뒤. 즉시 띄우면 유저가 실패를 인지할 틈이 없다.
                StartCoroutine(ShowInterstitialAfterPresentation());

#if UNITY_WEBGL
                // 리더보드 제출은 **맨 마지막**이다. 부가 기능이라 위의 어떤 것도 이것 때문에
                // 밀리거나 취소되면 안 된다. 응답을 기다리지 않고 내부에서 예외를 삼킨다.
                // 이번 판 점수가 아니라 **최고 기록**을 보낸다 — AitLeaderboard 주석 참조.
                // 부활 때문에 한 판에서 두 번 도달할 수 있는데, 갱신되지 않았으면 제출부가
                // 스스로 건너뛴다.
                if (SaveManager.IsValidInstance())
                    IGMain.Leaderboard.AitLeaderboard.SubmitBest(SaveManager.Instance.GetBestScore());
#endif
            }
        }

        /// <summary>
        /// 실패 연출이 끝난 뒤 전면 광고를 노출한다.
        ///
        /// 대기 시간은 결과 팝업과 **같은 상수**(IGConfig.GAME_OVER_PRESENTATION_DELAY)를 쓴다.
        /// timeScale 과 무관해야 하므로 WaitForSecondsRealtime 을 쓴다.
        ///
        /// 대기 중에 상태가 GameOver 를 벗어났다면(부활·재시작·홈) 광고를 띄우지 않는다 —
        /// 지금은 입력이 막혀 있어 도달하기 어렵지만, 나중에 흐름이 바뀌어도 어긋나지 않게 둔다.
        /// </summary>
        private IEnumerator ShowInterstitialAfterPresentation()
        {
            yield return new WaitForSecondsRealtime(IGConfig.GAME_OVER_PRESENTATION_DELAY);

            if (GameStateManager.IsValidInstance() &&
                GameStateManager.Instance.CurrentState != GameState.GameOver)
                yield break;

            // 정책 판단 후 전면 광고 노출 (부활 여부·쿨다운·횟수 자동 반영)
            if (AdManager.IsValidInstance())
                AdManager.Instance.TryShowInterstitial();
        }

        // ── QA 자동 플레이 봇 전용 API ──────────────────────────────────────────
        // 리플렉션·private 접근 없이 봇이 게임을 구동할 수 있도록 최소한의 인터페이스를 노출한다.

        /// <summary>QA 봇용: 현재 게임오버 상태인지 반환.</summary>
        public bool IsGameOver =>
            GameStateManager.IsValidInstance() &&
            GameStateManager.Instance.CurrentState == GameState.GameOver;

        /// <summary>QA 봇용: 현재 점수 반환.</summary>
        public long GetCurrentScore() => _scoreController?.GetCurrentScore() ?? 0L;

        /// <summary>
        /// QA 봇용: 현재 배치 가능한 블록 세트를 읽기 전용으로 반환.
        /// 내부 List 참조를 그대로 반환하므로 호출 쪽에서 수정하면 안 된다.
        /// </summary>
        public IReadOnlyList<IGBlockModel> GetAvailableBlocks()
        {
            if (_blockController == null || _blockController.BlockList == null)
                return System.Array.Empty<IGBlockModel>();
            return _blockController.BlockList;
        }

        /// <summary>QA 봇용: 특정 블록을 보드 그리드 위치에 배치할 수 있는지 확인.</summary>
        public bool CanPlaceBlock(IGBlockModel block, Vector2Int boardPosition)
        {
            if (_boardController == null || block == null) return false;
            return _boardController.CanPlaceBlockAtPosition(block, boardPosition);
        }

        /// <summary>
        /// QA 봇용: 특정 블록을 보드 그리드 위치에 직접 배치 시도.
        /// 성공 시 라인 클리어·다음 블록 스폰·게임오버 체크까지 처리하고 true 반환.
        /// 위치 불가·게임오버 상태·null 블록이면 false 반환 (보드 변화 없음).
        /// </summary>
        public bool TryPlaceBlock(IGBlockModel block, Vector2Int boardPosition)
        {
            if (IsGameOver || block == null) return false;
            if (_boardController == null || _blockController == null) return false;
            if (!_boardController.CanPlaceBlockAtPosition(block, boardPosition)) return false;

            // 보드에 배치 + 라인/스퀘어 클리어 처리
            _boardController.HandleBlockOnPointerUp(block, canPlace: true, boardPosition);
            // 블록 리스트에서 제거 + 필요 시 새 세트 스폰
            _blockController.HandleBlockOnPointerUp(block, canPlace: true, boardPosition);
            // 게임오버 판정
            CheckGameOver();
            return true;
        }
    }
}
