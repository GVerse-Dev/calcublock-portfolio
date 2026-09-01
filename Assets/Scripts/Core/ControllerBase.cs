using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem; // 네임스페이스 추가

public abstract class ControllerBase : MonoBehaviour
{
    public abstract void InitializeController();

    public abstract void UpdateController();

    public void Update()
    {
        UpdateController();
    }

    public T CreateObj<T>(Transform inParent, bool isActive = false, string inLayerName = "Default") where T : Component
    {
        GameObject go = new GameObject(typeof(T).Name);
        go.layer = LayerMask.NameToLayer(inLayerName);
        go.transform.SetParent(inParent);
        return go.AddComponent<T>();
    }
}
