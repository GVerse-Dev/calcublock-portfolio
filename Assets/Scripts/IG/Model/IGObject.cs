using System.Collections;
using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;


namespace IGMain
{

    public enum EState
    {
        None,
        Stable,
        UnStable,
    }

    public class IGObject : MonoBehaviour
    {
        public EState State { get; set; } = EState.None;

        public int Index { get; private set; } = 0;

        public virtual void Initialize()
        {
            Index = 0;
            State = EState.None;

            //ApplyTheme(ThemeManager.Instance.CurrentTheme);
            //ThemeManager.Instance.OnThemeChanged += ApplyTheme;
        }

        public virtual void Clear()
        {

        }

        public virtual void OnDestroy()
        {

        }

        public void SetPos(Vector2 pos)
        {
            this.transform.position = pos;
        }
        public void SetIndex(int index)
        {
            Index = index;
        }

    }
}
