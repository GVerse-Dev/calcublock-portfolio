using UnityEngine;
using UnityEngine.SceneManagement;
using IGMain;
using IGMain.Design;
using TMPro;
using System;

public class IGEngine : MonoBehaviour
{
    [Header("Design System")]
    [SerializeField] private TMP_FontAsset _fontDisplay;
    [SerializeField] private TMP_FontAsset _fontUI;

    [Header("Camera")]
    [SerializeField] private Camera[] cameras;


    private void Awake()
    {
        FirebaseManager.Initialize();
        CTFonts.Initialize(_fontDisplay, _fontUI);

    }

    private void Start()
    {
        InitializeManagers();
        InitializeControllers();
        StartGame();

        if (GameStateManager.IsValidInstance())
            GameStateManager.Instance.OnMainMenuRequested += GoToMainMenu;

        // 뷰포트가 바뀌면 카메라를 다시 맞춘다.
        //
        // StartGame()의 SetupCamera()가 orthographicSize를 정하는 **마지막** 코드다
        // (InitializeControllers → IGBoardModel.Initialize 가 먼저 설정하지만 여기서 덮인다).
        // 따라서 SetupCamera만 다시 돌리면 로드 시점 상태가 정확히 재현된다.
        //
        // 화면이 고정된 플랫폼에서는 이벤트가 발생하지 않아 동작이 동일하다.
        ScreenChangeWatcher.EnsureRunning();
        ScreenChangeWatcher.OnChanged += SetupCamera;
    }

    private void OnDestroy()
    {
        if (GameStateManager.IsValidInstance())
            GameStateManager.Instance.OnMainMenuRequested -= GoToMainMenu;

        ScreenChangeWatcher.OnChanged -= SetupCamera;
    }

    private void GoToMainMenu() => SceneManager.LoadScene("TitleScene");

    private void InitializeManagers()
    {
        if (IGGameManager.IsValidInstance()) IGGameManager.Instance.InitializeManager();
        if (PoolManager.IsValidInstance()) PoolManager.Instance.InitializeManager();
        if (ThemeManager.IsValidInstance()) ThemeManager.Instance.InitializeManager();
        if (GameStateManager.IsValidInstance()) GameStateManager.Instance.InitializeManager();
    }

    private void InitializeControllers()
    {
        var gameControllerObj = new GameObject("IGGameController");
        var gameController = gameControllerObj.AddComponent<IGGameController>();
        gameController.InitializeController();
    }

    public void StartGame()
    {
        SetupCamera();

        if (GameStateManager.IsValidInstance())
            GameStateManager.Instance.SetGameState(GameState.Playing);

        if (DifficultyManager.IsValidInstance())
            DifficultyManager.Instance.ResetDifficulty();

        if (AudioManager.IsValidInstance())
            AudioManager.Instance.Play("GameStart");

    }

    private void SetupCamera()
    {
        foreach (var cam in cameras)
        {
            cam.transform.position = new Vector3(0, 0, -10);
            cam.orthographic = true;

            // 720x1280 resolution with PPU 100
            // Height: 12.8 units -> OrthoSize: 6.4
            // Width: 7.2 units

            float targetHeightUnits = 12.8f;
            float targetWidthUnits = 7.2f;

            float aspect = (float)Screen.width / Screen.height;
            float targetAspect = targetWidthUnits / targetHeightUnits;

            if (aspect >= targetAspect)
            {
                // Screen is wider than 9:16 (Tablet, etc) -> Fit height
                cam.orthographicSize = targetHeightUnits / 2f;
            }
            else
            {
                // Screen is narrower than 9:16 -> Fit width
                cam.orthographicSize = (targetWidthUnits / 2f) / aspect;
            }

            // 월드 콘텐츠(보드/타일)를 세이프 에어리어 중심에 정렬.
            // UI(SafeAreaHandler)와 월드 배치가 펀치홀 인셋만큼 어긋나는 것을 보정한다.
            if (cam.CompareTag("MainCamera"))
            {
                var aligner = cam.GetComponent<SafeAreaCameraAligner>();
                if (aligner == null)
                    aligner = cam.gameObject.AddComponent<SafeAreaCameraAligner>();
                aligner.RebaseToCurrentPosition();
            }
        }
    }
}
