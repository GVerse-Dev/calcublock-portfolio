using System.Collections;
using System.Collections.Generic;
using UnityEngine;


public abstract class ManagerBase<T> : SingletonClass<T> where T : ManagerBase<T>
{
    public abstract void InitializeManager();

    public abstract void ClearManager();

    public abstract void FinalizeManager();

    protected override void OnApplicationQuit()
    {
        FinalizeManager();
        ClearManager();
        base.OnApplicationQuit();
    }
}
