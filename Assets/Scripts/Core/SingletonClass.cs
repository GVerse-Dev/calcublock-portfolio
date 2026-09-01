using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class SingletonClass<T> : MonoBehaviour where T : MonoBehaviour
{
    private static T _instance;
    private static bool _isApplicationQuitting = false;
    private static bool _isDestroying = false;

    public static T Instance
    {
        get
        {
#if UNITY_EDITOR
            // 에디터에서 재생 모드 종료 후 다시 시작할 때 정적 필드가 남는 경우를 방지
            if (!Application.isPlaying)
            {
                _instance = null;
                _isDestroying = false;
                _isApplicationQuitting = false;
            }
#endif
            // 애플리케이션 종료 중이면 null 반환 (객체 생성 방지)
            if (_isApplicationQuitting)
            {
                return null;
            }

            if (_instance == null)
            {
                _instance = (T)FindAnyObjectByType(typeof(T));

                if (_instance != null)
                {
                    // 씬에 이미 있는 경우 파괴 플래그가 true일 수 있으므로 강제 초기화
                    _isDestroying = false;

                    // [추가] Awake 호출 전 Instance가 먼저 접근될 경우를 대비해 DontDestroyOnLoad 설정
                    if (Application.isPlaying)
                        SetupPersistence(_instance.gameObject);
                }
            }

            if (_instance == null && !_isDestroying)
            {
                var singletonObject = new GameObject();
                _instance = singletonObject.AddComponent<T>();
                _instance.gameObject.name = typeof(T).ToString() + " (Singleton)";

                SetupPersistence(singletonObject);
            }

            return _instance;
        }
    }

    private static void SetupPersistence(GameObject obj)
    {
        if (obj.transform.parent == null)
        {
            DontDestroyOnLoad(obj);
        }
        else
        {
            Debug.LogWarning($"{obj.name} (Singleton) has a parent. DontDestroyOnLoad only works on root GameObjects.");
        }
    }

    protected virtual void Awake()
    {
        _isDestroying = false;
        _isApplicationQuitting = false;

        if (_instance == null)
        {
            _instance = this as T;
            SetupPersistence(gameObject);
        }
        else if (_instance == this)
        {
            // Instance Getter에서 이미 찾아서 DontDestroyOnLoad를 했을 수 있음.
            // 혹시 모르니 다시 한번 체크 (SetupPersistence 내부에서 중복 호출은 무해함)
            SetupPersistence(gameObject);
        }
        else
        {
            Debug.LogWarning($"Duplicate instance of {typeof(T)} found on {gameObject.name}, destroying.");
            Destroy(gameObject);
        }
    }

    protected virtual void OnApplicationQuit()
    {
        _isApplicationQuitting = true;

        if (_instance == this)
        {
            _instance = null;
        }
    }

    protected virtual void OnDestroy()
    {
        // 유효한 인스턴스만 플래그를 세팅한다.
        // 중복 인스턴스(Awake에서 Destroy된 것)가 파괴될 때는
        // _isDestroying을 건드리지 않아 Valid 인스턴스의 상태를 오염시키지 않는다.
        if (_instance == this)
        {
            _isDestroying = true;
            _instance = null;
        }
    }

    /// <summary>
    /// 인스턴스가 유효한지 확인 (앱 종료 중이 아니고 파괴 중이 아닐 때).
    /// _instance가 null이면 씬에서 직접 탐색해 복구한다 (Execution Order 차이 대비).
    /// </summary>
    public static bool IsValidInstance()
    {
        if (_isApplicationQuitting || _isDestroying) return false;
        if (_instance == null)
            _instance = (T)FindAnyObjectByType(typeof(T));
        return _instance != null;
    }

#if UNITY_EDITOR
    private static void ResetStaticData()
    {
        _instance = null;
        _isApplicationQuitting = false;
        _isDestroying = false;
    }
#endif
}
