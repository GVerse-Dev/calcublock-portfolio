using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// key-value 방식의 범용 로컬 저장소.
/// 하나의 JSON 파일(localStorage.json)에 모든 항목을 저장한다.
///
/// 사용 예:
///   LocalStorage.Instance.SetBool("tutorial_seen", true);
///   bool seen = LocalStorage.Instance.GetBool("tutorial_seen");
/// </summary>
public class LocalStorage : SingletonClass<LocalStorage>
{
    private const string FILE_NAME = "localStorage.json";

    private Dictionary<string, string> _cache;
    private string FilePath => Path.Combine(Application.persistentDataPath, FILE_NAME);

    // ── 라이프사이클 ─────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();
        Load();
    }

    // ── bool ─────────────────────────────────────────────────────────────────

    public bool GetBool(string key, bool defaultValue = false)
        => bool.TryParse(GetRaw(key), out bool v) ? v : defaultValue;

    public void SetBool(string key, bool value)
        => SetRaw(key, value.ToString());

    // ── int ──────────────────────────────────────────────────────────────────

    public int GetInt(string key, int defaultValue = 0)
        => int.TryParse(GetRaw(key), out int v) ? v : defaultValue;

    public void SetInt(string key, int value)
        => SetRaw(key, value.ToString());

    // ── float ────────────────────────────────────────────────────────────────

    public float GetFloat(string key, float defaultValue = 0f)
        => float.TryParse(GetRaw(key), out float v) ? v : defaultValue;

    public void SetFloat(string key, float value)
        => SetRaw(key, value.ToString());

    // ── string ───────────────────────────────────────────────────────────────

    public string GetString(string key, string defaultValue = "")
        => GetRaw(key) ?? defaultValue;

    public void SetString(string key, string value)
        => SetRaw(key, value);

    // ── 유틸 ─────────────────────────────────────────────────────────────────

    public bool HasKey(string key) => _cache.ContainsKey(key);

    public void DeleteKey(string key)
    {
        if (_cache.Remove(key)) Save();
    }

    public void DeleteAll()
    {
        _cache.Clear();
        Save();
    }

    // ── 내부 ─────────────────────────────────────────────────────────────────

    private string GetRaw(string key)
        => _cache.TryGetValue(key, out string v) ? v : null;

    private void SetRaw(string key, string value)
    {
        _cache[key] = value;
        Save();
    }

    private void Save()
    {
        var data = new LocalStorageData();
        foreach (var kvp in _cache)
            data.Entries.Add(new LocalStorageEntry { Key = kvp.Key, Value = kvp.Value });

        File.WriteAllText(FilePath, JsonUtility.ToJson(data, prettyPrint: true));
    }

    private void Load()
    {
        _cache = new Dictionary<string, string>();

        if (!File.Exists(FilePath)) return;

        var data = JsonUtility.FromJson<LocalStorageData>(File.ReadAllText(FilePath));
        if (data?.Entries == null) return;

        foreach (var entry in data.Entries)
            if (!string.IsNullOrEmpty(entry.Key))
                _cache[entry.Key] = entry.Value;
    }
}

// ── 직렬화 모델 ───────────────────────────────────────────────────────────────

[Serializable]
public class LocalStorageData
{
    public List<LocalStorageEntry> Entries = new();
}

[Serializable]
public class LocalStorageEntry
{
    public string Key;
    public string Value;
}
