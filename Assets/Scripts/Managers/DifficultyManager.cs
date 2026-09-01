using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class DifficultyManager : SingletonClass<DifficultyManager>
{

    public float CurrentSpeedMultiplier { get; private set; } = 1f;
    public int CurrentExtraShapeComplexity { get; private set; } = 0;

    private float _timeSinceLastIncrease = 0f;
    private const float DIFFICULTY_INCREASE_INTERVAL = 60f;


    private void Update()
    {
        if (GameStateManager.IsValidInstance() && GameStateManager.Instance.CurrentState == GameState.Playing)
        {
            _timeSinceLastIncrease += Time.deltaTime;
            if (_timeSinceLastIncrease >= DIFFICULTY_INCREASE_INTERVAL)
            {
                IncreaseDifficulty();
                _timeSinceLastIncrease = 0f;
            }
        }
    }

    private void IncreaseDifficulty()
    {
        CurrentSpeedMultiplier += 0.1f;
        if (Mathf.Abs(CurrentSpeedMultiplier % 0.5f) < 0.001f)
        {
            CurrentExtraShapeComplexity++;
        }
    }

    public void ResetDifficulty()
    {
        CurrentSpeedMultiplier = 1f;
        CurrentExtraShapeComplexity = 0;
        _timeSinceLastIncrease = 0f;
    }
}
