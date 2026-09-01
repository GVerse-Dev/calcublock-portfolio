using UnityEngine;
using System.Collections.Generic;
using IGMain.Design;

public enum TileColorScheme { Flat, ValueMapped }

[System.Serializable]
public class Theme
{
    public string name;

    [Header("Backgrounds")]
    public Color bgBase = Hex("#070B14");
    public Color bgSurface = Hex("#0D1525");
    public Color bgElevated = Hex("#131E30");

    [Header("Accents")]
    public Color cyan = Hex("#00E5FF");
    public Color blue = Hex("#0066FF");
    public Color violet = Hex("#4B6EFF");
    public Color amber = Hex("#FFB700");

    [Header("States")]
    public Color success = Hex("#00FFC2");
    public Color danger = Hex("#FF3B5C");

    [Header("Foregrounds")]
    public Color fg1 = Hex("#E0EEFF");
    public Color fg2 = Hex("#8BA8C8");
    public Color fg3 = Hex("#3D5A7A");

    [Header("Legacy & Mapping")]
    public Color backgroundColor;
    public Color uiBackgroundColor;
    public Color textColor;
    public Color gridLineColor;
    public Color blockColor;

    [Header("Tiles")]
    public Color tileHighlightColor = new Color(0f, 0.898f, 1.0f, 0.14f);
    public Color tileFilledColor = new Color(1f, 1f, 1f, 0.82f);
    public Color tileGlowColor = new Color(0f, 0.898f, 1.0f, 0.3f);
    public TileColorScheme colorScheme = TileColorScheme.Flat;

    private static Color Hex(string hex)
    {
        if (ColorUtility.TryParseHtmlString(hex, out var c)) return c;
        return Color.magenta;
    }

    public void SyncLegacy()
    {
        backgroundColor = bgBase;
        uiBackgroundColor = bgSurface;
        textColor = fg1;
        gridLineColor = fg3;
        blockColor = tileFilledColor;
    }
}


public class ThemeManager : ManagerBase<ThemeManager>
{
    private const string PaletteIndexKey = "ct_palette";
    private const string BrightKey = "ct_brightness";

    // Inspector에서 팔레트 asset들을 할당한다 — 각 팔레트가 하나의 테마가 된다
    [SerializeField] private GamePalette[] palettes;

#if UNITY_EDITOR
    [Header("▼ 현재 팔레트 (읽기 전용 — 에디터 미리보기)")]
#pragma warning disable 0414
    [SerializeField] private GamePalette _editorCurrentPalette;
#pragma warning restore 0414
#endif

    private readonly List<IThemeListener> _listeners = new List<IThemeListener>();
    private readonly List<Theme> _themes = new List<Theme>();

    [SerializeField] private int _currentIndex;

    // ── 프로퍼티 ──────────────────────────────────────────────────────────────

    public GamePalette[] palettesArray => palettes;

    public GamePalette CurrentPalette
    {
        get
        {
#if UNITY_EDITOR
            if (!Application.isPlaying && _editorCurrentPalette != null) return _editorCurrentPalette;
#endif
            return palettes != null && _currentIndex >= 0 && _currentIndex < palettes.Length ? palettes[_currentIndex] : null;
        }
    }

    public Theme CurrentTheme { get; private set; }

    /// 드롭다운 등 외부에서 팔레트 이름 목록이 필요할 때 사용
    public IReadOnlyList<Theme> AvailableThemes => _themes;
    public int CurrentThemeIndex => _currentIndex;

    public delegate void ThemeChangedHandler(Theme newTheme);
    public event ThemeChangedHandler OnThemeChanged;

    // ── PaletteTint 등록 ─────────────────────────────────────────────────────

    public static void Register(IThemeListener listener)
    {
        if (IsValidInstance()) Instance._listeners.Add(listener);
    }

    public static void Unregister(IThemeListener listener)
    {
        if (IsValidInstance()) Instance._listeners.Remove(listener);
    }

    // ── Unity 생명주기 ────────────────────────────────────────────────────────

    protected override void Awake()
    {
        base.Awake();

        if (palettes != null && palettes.Length > 0)
        {
            // _currentIndex = Mathf.Clamp(
            //     PlayerPrefs.GetInt(PaletteIndexKey, 0), 0, palettes.Length - 1);

            _currentIndex = 2;
        }

    }

    // ── ManagerBase 구현 ──────────────────────────────────────────────────────

    public override void InitializeManager()
    {
        BuildThemesFromPalettes();

        var savedBright = PlayerPrefs.GetFloat(BrightKey, -1f);
        if (savedBright >= 0f && CurrentPalette != null)
            CurrentPalette.globalBrightness = savedBright;

        ApplyTheme(CurrentTheme);
        ApplyAllTints();
    }

    public override void ClearManager()
    {
        OnThemeChanged = null;
    }

    public override void FinalizeManager()
    {
        PlayerPrefs.Save();
    }

    // ── 팔레트(= 테마) 교체 ───────────────────────────────────────────────────

    public void SetPalette(int index)
    {
        if (palettes == null || palettes.Length == 0) return;

        _currentIndex = Mathf.Clamp(index, 0, palettes.Length - 1);
        PlayerPrefs.SetInt(PaletteIndexKey, _currentIndex);

        // 현재 선택된 팔레트 → Theme 갱신
        if (_currentIndex < _themes.Count)
        {
            CurrentTheme = _themes[_currentIndex];
            CopyPaletteToTheme(CurrentPalette, CurrentTheme);
        }

        ApplyTheme(CurrentTheme);
        ApplyAllTints();
    }

    // 기존 API 호환 — SetTheme 호출 코드가 그대로 동작
    public void SetTheme(int index) => SetPalette(index);

    public void SetBrightness(float brightness)
    {
        if (CurrentPalette == null) return;

        CurrentPalette.globalBrightness = Mathf.Clamp(brightness, 0.45f, 1.8f);
        PlayerPrefs.SetFloat(BrightKey, CurrentPalette.globalBrightness);

        ApplyAllTints();
        if (CurrentTheme != null) OnThemeChanged?.Invoke(CurrentTheme);
    }

    // ── 내부 ──────────────────────────────────────────────────────────────────

    /// 팔레트 배열로부터 Theme 목록을 빌드한다.
    /// 팔레트가 없으면 하드코딩 기본 테마로 폴백한다.
    private void BuildThemesFromPalettes()
    {
        _themes.Clear();

        if (palettes == null || palettes.Length == 0)
        {
            Debug.LogWarning("ThemeManager: Palettes 배열이 비어있습니다. Inspector에서 GamePalette asset을 할당해주세요.");
            _themes.Add(CreateFallbackTheme());
            _currentIndex = 0;
            CurrentTheme = _themes[0];
            return;
        }

        foreach (var pal in palettes)
        {
            if (pal == null) continue;
            var theme = new Theme { name = pal.name };
            CopyPaletteToTheme(pal, theme);
            _themes.Add(theme);
        }

        _currentIndex = Mathf.Clamp(_currentIndex, 0, _themes.Count - 1);
        CurrentTheme = _themes[_currentIndex];

#if UNITY_EDITOR
        _editorCurrentPalette = CurrentPalette;
#endif
    }

#if UNITY_EDITOR
    private void OnValidate()
    {
        if (Application.isPlaying) return;

        // palettes 배열 범위 내로 _currentIndex 보정
        if (palettes != null && palettes.Length > 0)
            _currentIndex = Mathf.Clamp(_currentIndex, 0, palettes.Length - 1);

        // _editorCurrentPalette가 비어있으면 현재 인덱스의 팔레트로 채워줌 (최초 표시용)
        if (_editorCurrentPalette == null && palettes != null && _currentIndex < palettes.Length)
            _editorCurrentPalette = palettes[_currentIndex];

        // 씬 내의 모든 리스너 동기화
        RefreshAllListeners();
    }

    public void RefreshAllListeners()
    {
        var connectors = Object.FindObjectsByType<PaletteConnector>();
        foreach (var pc in connectors) pc.OnThemeApply();

        var tints = Object.FindObjectsByType<PaletteTint>();
        foreach (var t in tints) t.OnThemeApply();
    }
#endif

    private static void CopyPaletteToTheme(GamePalette pal, Theme theme)
    {
        theme.bgBase = pal.bgBase;
        theme.bgSurface = pal.bgSurface;
        theme.bgElevated = pal.bgElevated;
        theme.cyan = pal.cyan;
        theme.blue = pal.blue;
        theme.violet = pal.violet;
        theme.amber = pal.amber;
        theme.success = pal.success;
        theme.danger = pal.danger;
        theme.fg1 = pal.fg1;
        theme.fg2 = pal.fg2;
        theme.fg3 = pal.fg3;

        theme.tileHighlightColor = pal.tileHighlightColor;
        theme.tileFilledColor = pal.tileFilledColor;
        theme.tileGlowColor = pal.tileGlowColor;

        theme.SyncLegacy();
    }

    private void ApplyTheme(Theme theme)
    {
        if (theme == null) return;

        var pal = CurrentPalette;
        if (Camera.main != null)
        {
            Camera.main.clearFlags = CameraClearFlags.SolidColor;
            Camera.main.backgroundColor = pal != null ? pal.boardBg : theme.bgBase;
        }

        OnThemeChanged?.Invoke(theme);
    }

    private void ApplyAllTints()
    {
        for (int i = _listeners.Count - 1; i >= 0; i--)
        {
            var l = _listeners[i];
            // MonoBehaviour가 파괴됐을 때 인터페이스 참조는 non-null이지만
            // UnityEngine.Object 비교는 null을 반환한다 — 명시적 캐스트로 확인
            if (l is UnityEngine.Object obj && obj == null)
                _listeners.RemoveAt(i);
            else
                l.OnThemeApply();
        }
    }

    private static Theme CreateFallbackTheme()
    {
        var t = new Theme { name = "Default" };
        t.SyncLegacy();
        return t;
    }
}
