using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using IGMain;

public enum EPoolType
{
    Block,
    BlockTile,
    Board,
    BoardTile,

}


public class PoolManager : ManagerBase<PoolManager>
{
    public readonly int POOL_COUNT = 1;

    private const string PREFAB_ROOT_PATH = "Prefabs/";

    private Dictionary<EPoolType, Queue<GameObject>> _poolList = new Dictionary<EPoolType, Queue<GameObject>>();

    private Dictionary<EPoolType, string> _objectPath = new Dictionary<EPoolType, string>() 
    {
        { EPoolType.Block, "IGBlock" },
        { EPoolType.BlockTile, "IGBlockTile" },
        { EPoolType.Board, "IGBoard" },
        { EPoolType.BoardTile, "IGBoardTile" },
    };

    private bool _isDestroying = false;

    public void Push<T>(EPoolType type, T obj) where T : IGObject
    {
        // 파괴 중이거나 오브젝트가 null이면 무시
        if (_isDestroying || obj == null || this == null)
            return;

        // 키가 존재하지 않으면 생성
        if (!_poolList.ContainsKey(type))
        {
            _poolList.Add(type, new Queue<GameObject>());
        }

        obj.transform.parent = this.transform;
        obj.gameObject.SetActive(false);
        _poolList[type].Enqueue(obj.gameObject);
    }

    public T Pop<T>(EPoolType type) where T : IGObject
    {
        if (!_poolList.ContainsKey(type) || _poolList[type].Count <= 0)
            Create(type);

        if (!_poolList.ContainsKey(type) || _poolList[type].Count == 0)
        {
            Debug.LogError($"PoolManager: Failed to create pool object of type {type}. Resource missing?");
            return null;
        }

        var obj = _poolList[type].Dequeue();
        obj.gameObject.SetActive(true);

        T component = obj.GetComponent<T>();
        if (component == null)
            Debug.LogError($"Object in pool doesn't have component of type {typeof(T)}");

        return component;
    }

    private void Create(EPoolType type)
    {
        var resource = Resources.Load<GameObject>(PREFAB_ROOT_PATH + $"{_objectPath[type]}");

        if (resource == null)
            return;


        if (_poolList.ContainsKey(type) == false)
        {
            _poolList.Add(type, new Queue<GameObject>());
        }


        for (int count = 0; count < POOL_COUNT; ++count)
        {
            var prefab = Instantiate(resource,this.transform);
            prefab.SetActive(false);

            _poolList[type].Enqueue(prefab);
        }

    }

    protected override void OnApplicationQuit()
    {
        _isDestroying = true;
        base.OnApplicationQuit();
    }

    protected override void OnDestroy()
    {
        _isDestroying = true;
        
        // 안전하게 풀 정리
        if (_poolList != null)
        {
            foreach (var poolItem in _poolList)
            {
                if (poolItem.Value != null)
                {
                    foreach (var item in poolItem.Value)
                    {
                        if (item != null && item.gameObject != null)
                        {
                            DestroyImmediate(item.gameObject);
                        }
                    }
                    poolItem.Value.Clear();
                }
            }
            _poolList.Clear();
        }
    }

#if UNITY_EDITOR
    [UnityEditor.InitializeOnLoadMethod]
    private static void InitializeOnLoad()
    {
        UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange state)
    {
        if (state == UnityEditor.PlayModeStateChange.ExitingPlayMode)
        {
            if (Instance != null)
            {
                Instance._isDestroying = true;
            }
        }
    }
#endif
  

    public override void InitializeManager()
    {
        // 모든 풀 타입을 미리 생성
        foreach (EPoolType poolType in System.Enum.GetValues(typeof(EPoolType)))
        {
            if (!_poolList.ContainsKey(poolType))
            {
                Create(poolType);
                Debug.Log($"PoolManager Initialize {poolType} Total Pooling Count : {_poolList[poolType].Count}");
            }
        }
    }

    public override void ClearManager()
    {
        if (_poolList != null)
        {
            foreach (var poolItem in _poolList)
            {
                if (poolItem.Value != null)
                {
                    while (poolItem.Value.Count > 0)
                    {
                        var item = poolItem.Value.Dequeue();
                        if (item != null && item.gameObject != null)
                        {
                            DestroyImmediate(item.gameObject);
                        }
                    }
                }
            }
            _poolList.Clear();
        }
    }

    public override void FinalizeManager()
    {
    }
}

