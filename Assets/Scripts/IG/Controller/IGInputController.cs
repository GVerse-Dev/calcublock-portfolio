using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using IGMain;
using UnityEngine.InputSystem; // 네임스페이스 추가
public class IGInputController : ControllerBase
{
    private Vector3 _dragStartPosition;
    private Vector3 _dragCurrentPosition;
    private IGBlockModel _selectedBlock;
    private Camera _mainCamera;
    private bool _isDragging = false;

    // 다른 컨트롤러에서 참조할 수 있도록 델리게이트 이벤트 정의
    public delegate void BlockSelectedHandler(IGBlockModel block);
    public delegate void BlockDraggedHandler(IGBlockModel block, Vector3 inputPosition);
    public delegate void BlockReleasedHandler(IGBlockModel block, Vector3 inputPosition);

    public event BlockSelectedHandler OnBlockSelected;
    public event BlockDraggedHandler OnBlockDragged;
    public event BlockReleasedHandler OnBlockReleased;

    public override void InitializeController()
    {
        // 이벤트 구독 해제 등 정리 작업
        OnBlockSelected = null;
        OnBlockDragged = null;
        OnBlockReleased = null;

        _mainCamera = Camera.main;

        _selectedBlock = null;
        _isDragging = false;
    }

    public override void UpdateController()
    {
        HandleInputs();
    }

    //     private void HandleInputs()
    //     {
    //         if (_mainCamera == null)
    //         {
    //             _mainCamera = Camera.main;
    //             if (_mainCamera == null) return;
    //         }

    //         // 터치 및 마우스 입력 모두 처리 (모바일 & PC 호환)
    //         if (Input.touchCount > 0)
    //         {
    //             Touch touch = Input.GetTouch(0);
    //             HandleTouchInput(touch);
    //         }
    //         else
    //         {
    //             HandleMouseInput();
    //         }

    // #if UNITY_EDITOR
    //         if (Keyboard.current.gKey.isPressed)  // G 키로 강제 게임오버
    //         {
    //             if (GameStateManager.IsValidInstance())
    //                 GameStateManager.Instance.SetGameState(GameState.GameOver);
    //         }
    // #endif
    //     }

    private void HandleInputs()
    {
        if (GameStateManager.IsValidInstance() &&
            GameStateManager.Instance.CurrentState != GameState.Playing)
            return;

        if (_mainCamera == null)
        {
            _mainCamera = Camera.main;
            if (_mainCamera == null) return;
        }

        var touchscreen = Touchscreen.current;
        if (touchscreen != null && touchscreen.touches.Count > 0)
        {
            HandleTouchInput(touchscreen.touches[0]);
        }
        else if (Mouse.current != null)
        {
            HandleMouseInput();
        }

#if UNITY_EDITOR
        if (Keyboard.current != null && Keyboard.current.gKey.isPressed)
        {
            if (GameStateManager.IsValidInstance())
                GameStateManager.Instance.SetGameState(GameState.GameOver);
        }
#endif
    }

    private void HandleTouchInput(UnityEngine.InputSystem.Controls.TouchControl touch)
    {
        Vector3 touchPosition = _mainCamera.ScreenToWorldPoint(touch.position.ReadValue());
        touchPosition.z = 0;

        var phase = touch.phase.ReadValue();
        switch (phase)
        {
            case UnityEngine.InputSystem.TouchPhase.Began:
                HandleInputBegan(touchPosition);
                break;
            case UnityEngine.InputSystem.TouchPhase.Moved:
            case UnityEngine.InputSystem.TouchPhase.Stationary:
                HandleInputMoved(touchPosition);
                break;
            case UnityEngine.InputSystem.TouchPhase.Ended:
            case UnityEngine.InputSystem.TouchPhase.Canceled:
                HandleInputEnded(touchPosition);
                break;
        }
    }

    private void HandleMouseInput()
    {
        Vector3 mousePosition = _mainCamera.ScreenToWorldPoint(Mouse.current.position.ReadValue());
        mousePosition.z = 0;
        IGLog.Verbose(Mouse.current.leftButton.isPressed + "  /  " + _isDragging);
        if (Mouse.current.leftButton.isPressed && !_isDragging)
        {
            HandleInputBegan(mousePosition);
        }
        else if (Mouse.current.leftButton.isPressed && _isDragging)
        {
            HandleInputMoved(mousePosition);
        }
        else if (!Mouse.current.leftButton.isPressed && _isDragging)
        {
            HandleInputEnded(mousePosition);
        }
    }

    private void HandleInputBegan(Vector3 position)
    {
        _dragStartPosition = position;
        _isDragging = false;

        // 레이캐스트로 블록 선택 확인
        RaycastHit2D hit = Physics2D.Raycast(position, Vector2.zero);
        if (hit.collider != null)
        {
            IGBlockModel block = hit.collider.GetComponent<IGBlockModel>();

            if (block != null)
            {
                _selectedBlock = block;
                _isDragging = true;
                OnBlockSelected?.Invoke(_selectedBlock);
            }
        }
    }

    private void HandleInputMoved(Vector3 position)
    {
        if (!_isDragging || _selectedBlock == null)
            return;

        _dragCurrentPosition = position;

        //_selectedBlock.transform.position = position + new Vector3(0f,300f);

        OnBlockDragged?.Invoke(_selectedBlock, position);
    }

    private void HandleInputEnded(Vector3 position)
    {
        if (!_isDragging || _selectedBlock == null)
            return;

        _isDragging = false;

        OnBlockReleased?.Invoke(_selectedBlock, position);

    }




}