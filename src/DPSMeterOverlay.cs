using System.Text.Json;
using Godot;

namespace DPSMeter;

/// <summary>
/// Flat, table-style damage overlay. Simple colors, clear columns, no overlap.
/// </summary>
public sealed partial class DPSMeterOverlay : CanvasLayer
{
    private const float ExpandedWidth = 600f;
    private const float CompactWidth = 248f;
    private const float SideTabWidth = 86f;
    private const float SideTabHeight = 32f;
    private const float ViewportMargin = 16f;
    private const float MinPanelHeight = 72f;

    // ── Localization ───────────────────────────────────────────

    private static Dictionary<string, string>? _locStrings;

    private static Dictionary<string, string> LocStrings => _locStrings ??= LoadLocStrings();

    private static Dictionary<string, string> DefaultLocStrings() => new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["TITLE"] = "DPS Meter",
        ["PLAYER"] = "Player",
        ["PCT"] = "%",
        ["TOTAL"] = "Total",
        ["COMBAT"] = "Combat",
        ["LAST"] = "Last",
        ["MAX"] = "Max",
        ["EMPTY"] = "Waiting for combat eventsâ€¦",
        ["TAB_METER"] = "Meter",
        ["TAB_CARDS"] = "Card Usage",
        ["TAB_RECEIVED"] = "Received Damage",
        ["CARD_USAGE"] = "Card Usage",
        ["CARDS"] = "Cards",
        ["ATTACK"] = "Attack",
        ["SKILL"] = "Skill",
        ["POWER"] = "Power",
        ["OTHER"] = "Other",
        ["AUTO"] = "Auto",
        ["RECEIVED_DAMAGE"] = "Received Damage",
        ["INCOMING"] = "Incoming",
        ["BLOCKED"] = "Blocked",
        ["HP_LOST"] = "HP Lost",
        ["BLOCK_GAINED"] = "Block+",
        ["TAB_SUPPORT"] = "Support",
        ["RUN_SCOPE"] = "Whole run",
        ["COPY_SUMMARY"] = "Copy summary",
        ["COPIED"] = "Copied!",
        ["SCOPE_NOTE"] = "Scope for Support, Card Usage, Received Damage, Kills / Deaths, Healing, and copied summaries. Meter shows both fight and run damage.",
        ["BLOCK_GIVEN"] = "Block made",
        ["ALLY_BLOCK"] = "Ally block",
        ["ALLY_BUFFS"] = "Ally buffs",
        ["DEBUFFS"] = "Debuffs",
        ["ALLY_SKILLS"] = "Ally skills",
        ["DAMAGE"] = "Damage",
        ["SUMMARY_TITLE"] = "Slay the Spire 2 - Party contributions",
        ["SUMMARY_NOTE"] = "Observed events only. Ally skills = Skill plays that granted tracked block, positive buffs or healing to another player (once per play). Buff/debuff counts are additions per recipient; stacks are listed by type. Block made uses known card sources and is not damage prevented. Healing counts actual HP restored; unknown providers receive no credit. Energy, draw, cleanses and damage amplification are not attributed. Kills require a sourced lethal hit and confirmed death; automatic cleanup and prevented deaths are excluded. Deaths exclude abandoning. No overall contribution score.",
        ["SUPPORT_HELP"] = "Block made: source-attributed card block, including self. Ally block: granted to other players. Ally buffs: positive buff additions per recipient. Debuffs: positive debuff additions on enemies. Ally skills: Skill plays with tracked ally block/buff/healing, once per play. Hover a row for buff stacks. Unattributed passive block is excluded.",
        ["BUFF_DETAIL"] = "Buff stacks given to allies",
        ["TAB_KILLS"] = "Kills / Deaths",
        ["KILLS"] = "Enemy kills",
        ["BOSS_KILLS"] = "Boss kills",
        ["BOSS_HONORS"] = "Boss finishing blows",
        ["LAST_BOSS"] = "Last boss finished",
        ["NO_BOSS"] = "—",
        ["KILLS_HELP"] = "Credit for a sourced lethal hit followed by confirmed death. Boss kills are primary enemies in boss encounters; summoned minions only count as enemy kills when directly killed. Prevented deaths and automatic cleanup get no credit. Revived enemies can be credited on a later death. Boss honors belong to this run. Deaths count confirmed player deaths once per life; prevented deaths and abandoning the run are excluded. Reviving allows a later death to count again.",
        ["DEATHS"] = "Deaths",
        ["TAB_HEALING"] = "Healing",
        ["HEALING_DONE"] = "Healing done",
        ["ALLY_HEALING"] = "Ally healing",
        ["HEALING_RECEIVED"] = "Healing received",
        ["HEALING_HELP"] = "Actual HP restored, excluding overhealing. Healing done includes self; ally healing excludes self. Provider credit requires an identifiable executing card or effect. Unattributed heals, including rest sites, count only as received. Revivals by direct HP assignment are not healing. Combat shows this fight; Whole run includes out-of-combat heals.",
    };

    private static Dictionary<string, string> LoadLocStrings()
    {
        const string resPath = "res://assets/localization/eng/dps_meter.json";
        if (!Godot.FileAccess.FileExists(resPath))
        {
            return DefaultLocStrings();
        }

        using Godot.FileAccess file = Godot.FileAccess.Open(resPath, Godot.FileAccess.ModeFlags.Read);
        string json = file?.GetAsText() ?? "{}";
        Dictionary<string, string>? loaded = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
        return loaded is { Count: > 0 } ? loaded : DefaultLocStrings();
    }

    internal static string L(string key) => LocStrings.TryGetValue(key, out string? v) ? v : key;

    // ── Palette (keep it minimal) ──────────────────────────────
    private static readonly Color White = new("FFFFFF");
    private static readonly Color Gray = new("A0A8B4");
    private static readonly Color DimGray = new("687480");
    private static readonly Color Green = new("4ADE80");
    private static readonly Color Red = new("F87171");
    private static readonly Color Yellow = new("FACC15");
    private static readonly Color Cyan = new("22D3EE");
    private static readonly Color BgDark = new("000000B0");
    private static readonly Color BgRow = new("FFFFFF10");
    private static readonly Color BgActiveRow = new("4ADE8018");
    private static readonly Color Border = new("3A3A5C");
    private static readonly Color BorderActive = new("4ADE80");

    // Character theme colors based on their visual identity
    private static readonly Dictionary<string, Color> CharTheme = new(System.StringComparer.OrdinalIgnoreCase)
    {
        ["ironclad"]    = new Color("E05050"),  // red
        ["silent"]      = new Color("5DB85D"),  // green
        ["defect"]      = new Color("4AA8D8"),  // blue
        ["necrobinder"] = new Color("B060D0"),  // purple
        ["regent"]      = new Color("D8A030"),  // gold/orange
    };

    private static readonly Dictionary<string, Texture2D?> IconCache = new(System.StringComparer.OrdinalIgnoreCase);

    private static DPSMeterOverlay? _instance;
    private static bool _shuttingDown;
    private bool _statsRefreshQueued;

    private Control? _root;
    private MarginContainer? _contentPad;
    private ScrollContainer? _bodyScroll;
    private VBoxContainer? _bodyContent;
    private VBoxContainer? _rows;
    private Label? _emptyLabel;
    private HBoxContainer? _columnHeadings;
    private Control? _separator;
    private Button? _meterTabBtn;
    private Button? _cardsTabBtn;
    private Button? _receivedTabBtn;
    private Button? _supportTabBtn;
    private Button? _killsTabBtn;
    private Button? _healingTabBtn;
    private bool _runScope = true;
    private Button? _toggleBtn;
    private Button? _hideBtn;
    private Button? _sideTab;
    private bool _isDragging;
    private Vector2 _dragOffset;
    private bool _expanded = true;
    private OverlayTab _activeTab = OverlayTab.Support;
    private bool _hiddenToSide;
    private HiddenDockSide _hiddenSide = HiddenDockSide.Right;
    private bool _layoutRefreshQueued;
    private bool _resetScrollPending;
    private string? _lastAppliedRunToken;
    private int _lastAppliedCombatIndex = -1;
    private OverlayState? _lastState;
    private static bool _pendingCreate;

    /// <summary>
    /// Schedule overlay creation on next frame (safe to call from mod init before game loop is ready).
    /// </summary>
    public static void ScheduleCreate()
    {
        if (_shuttingDown) return;
        _pendingCreate = true;
    }

    public static void Shutdown()
    {
        _shuttingDown = true;
        _pendingCreate = false;
        var overlay = _instance;
        _instance = null;
        if (GodotObject.IsInstanceValid(overlay))
        {
            RunDPSMeterService.Changed -= overlay!.OnChanged;
            overlay._lastState = null;
            if (overlay.IsInsideTree()) overlay.Free();
            else overlay.QueueFree();
        }
        // These textures are owned by the game; drop references without disposing them.
        IconCache.Clear();
    }



    public override void _EnterTree()
    {
        Layer = 100;
        Name = nameof(DPSMeterOverlay);
        RunDPSMeterService.Changed += OnChanged;
    }

    public override void _ExitTree()
    {
        RunDPSMeterService.Changed -= OnChanged;
        if (ReferenceEquals(_instance, this)) _instance = null;
    }

    public override void _Ready()
    {
        _root = new PanelContainer
        {
            Name = "Root",
            MouseFilter = Control.MouseFilterEnum.Stop,
            Position = new Vector2(16, 16),
            CustomMinimumSize = new Vector2(ExpandedWidth, 0),
            Size = new Vector2(ExpandedWidth, 0)
        };

        // Background panel (the root itself)
        PanelContainer bg = (PanelContainer)_root;
        bg.GuiInput += OnGuiInput;
        bg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = BgDark,
            CornerRadiusTopLeft = 6, CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6, CornerRadiusBottomRight = 6
        });

        // Outer margin
        _contentPad = new MarginContainer();
        _contentPad.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        foreach (string side in new[] { "margin_left", "margin_right" })
            _contentPad.AddThemeConstantOverride(side, 10);
        foreach (string side in new[] { "margin_top", "margin_bottom" })
            _contentPad.AddThemeConstantOverride(side, 8);

        VBoxContainer col = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        col.AddThemeConstantOverride("separation", 4);

        // ── Header row: title + toggle ──
        col.AddChild(BuildHeader());

        // ── Metric tabs ──
        col.AddChild(BuildTabBar());
        col.AddChild(BuildShareBar());

        // ── Column headings ──
        _columnHeadings = new HBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _columnHeadings.AddThemeConstantOverride("separation", 0);
        RefreshColumnHeadings();
        col.AddChild(_columnHeadings);

        // ── Thin separator ──
        _separator = HLine();
        col.AddChild(_separator);

        // ── Scrollable body ──
        _bodyScroll = new ScrollContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ShrinkBegin,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };

        _bodyContent = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _bodyContent.AddThemeConstantOverride("separation", 4);

        // ── Player rows ──
        _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        _rows.AddThemeConstantOverride("separation", 2);
        _bodyContent.AddChild(_rows);

        // ── Empty hint ──
        _emptyLabel = MakeLabel(L("EMPTY"), 12, DimGray);
        _emptyLabel.HorizontalAlignment = HorizontalAlignment.Center;
        _bodyContent.AddChild(_emptyLabel);

        _bodyScroll.AddChild(_bodyContent);
        col.AddChild(_bodyScroll);

        _contentPad.AddChild(col);
        bg.AddChild(_contentPad);
        AddChild(_root);

        _sideTab = BuildSideTab();
        AddChild(_sideTab);

        RefreshChrome();
        ApplyState(RunDPSMeterService.BuildOverlayState());
    }

    // ── Header ─────────────────────────────────────────────────

    private Control BuildHeader()
    {
        HBoxContainer h = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        h.AddThemeConstantOverride("separation", 6);

        Label title = MakeLabel(L("TITLE"), 15, White);
        title.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        _toggleBtn = new Button
        {
            Text = "\u25bc",
            CustomMinimumSize = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None
        };
        _toggleBtn.AddThemeFontSizeOverride("font_size", 12);
        _toggleBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("FFFFFF10"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("FFFFFF20"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("FFFFFF30"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _toggleBtn.AddThemeColorOverride("font_color", Gray);
        _toggleBtn.Pressed += OnToggle;

        _hideBtn = new Button
        {
            Text = "\u00bb",
            CustomMinimumSize = new Vector2(24, 24),
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            TooltipText = "Hide to side"
        };
        _hideBtn.AddThemeFontSizeOverride("font_size", 12);
        _hideBtn.AddThemeStyleboxOverride("normal", new StyleBoxFlat { BgColor = new Color("FFFFFF10"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeStyleboxOverride("hover", new StyleBoxFlat { BgColor = new Color("FFFFFF20"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeStyleboxOverride("pressed", new StyleBoxFlat { BgColor = new Color("FFFFFF30"), CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3, CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3 });
        _hideBtn.AddThemeColorOverride("font_color", Gray);
        _hideBtn.Pressed += OnHideToSide;

        h.AddChild(title);
        h.AddChild(_toggleBtn);
        h.AddChild(_hideBtn);
        return h;
    }

    private Button BuildSideTab()
    {
        Button tab = new()
        {
            Name = "SideTab",
            Visible = false,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            Text = L("TITLE"),
            CustomMinimumSize = new Vector2(SideTabWidth, SideTabHeight),
            Size = new Vector2(SideTabWidth, SideTabHeight)
        };
        tab.AddThemeFontSizeOverride("font_size", 12);
        tab.AddThemeColorOverride("font_color", White);
        tab.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = new Color("121821EE"),
            BorderColor = BorderActive,
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        tab.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = new Color("1A2431EE"),
            BorderColor = BorderActive,
            BorderWidthLeft = 2,
            BorderWidthTop = 1,
            BorderWidthRight = 2,
            BorderWidthBottom = 1,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6
        });
        tab.Pressed += OnRestoreFromSide;
        return tab;
    }

    private SupportMetrics Stats(PlayerDamageSnapshot snapshot) => _runScope ? snapshot.RunSupport : snapshot.CombatSupport;

    private Control BuildShareBar()
    {
        var bar = new HBoxContainer();
        bar.AddThemeConstantOverride("separation", 6);
        var scope = new Button { Text = L("RUN_SCOPE"), TooltipText = L("SCOPE_NOTE"), FocusMode = Control.FocusModeEnum.None };
        scope.Pressed += () =>
        {
            _runScope = !_runScope;
            scope.Text = L(_runScope ? "RUN_SCOPE" : "COMBAT");
            ApplyState(RunDPSMeterService.BuildOverlayState());
        };
        var copy = new Button { Text = L("COPY_SUMMARY"), FocusMode = Control.FocusModeEnum.None };
        copy.Pressed += () =>
        {
            DisplayServer.ClipboardSet(RunDPSMeterService.BuildShareSummary(_runScope));
            copy.Text = L("COPIED");
        };
        copy.MouseExited += () => copy.Text = L("COPY_SUMMARY");
        bar.AddChild(scope);
        bar.AddChild(copy);
        return bar;
    }

    private Control CreateSupportRow(PlayerDamageSnapshot snap)
    {
        var stats = Stats(snap);
        var row = CreateStatsRow(snap, new[]
        {
            (RunDPSMeterService.Format(stats.BlockGiven), Green, 70),
            (RunDPSMeterService.Format(stats.AllyBlock), Cyan, 70),
            (stats.AllyBuffs.ToString(), Yellow, 70),
            (stats.EnemyDebuffs.ToString(), new Color("FF79C6"), 70),
            (stats.AllySkillPlays.ToString(), Green, 70)
        });
        row.TooltipText = L("SUPPORT_HELP") + "\n\n" + L("BUFF_DETAIL") + "\n" +
            string.Join("\n", stats.AllyBuffStacks.OrderBy(p => p.Key).Select(p => $"{p.Key}: +{RunDPSMeterService.Format(p.Value)}"));
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        return row;
    }

    private Control CreateKillsRow(PlayerDamageSnapshot snap)
    {
        var stats = Stats(snap);
        var last = stats.BossFinishes.LastOrDefault() ?? L("NO_BOSS");
        var row = CreateStatsRow(snap, new[]
        {
            (stats.Kills.ToString(), White, 78),
            (stats.BossKills.ToString(), Yellow, 78),
            (stats.Deaths.ToString(), Red, 54),
            (last.Length > 15 ? last[..14] + "…" : last, Cyan, 140)
        });
        row.TooltipText = L("KILLS_HELP") + "\n\n" + L("BOSS_HONORS") + "\n" + string.Join("\n", stats.BossFinishes);
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        return row;
    }

    private Control CreateHealingRow(PlayerDamageSnapshot snap)
    {
        var stats = Stats(snap);
        var row = CreateStatsRow(snap, new[]
        {
            (RunDPSMeterService.Format(stats.HealingDone), Green, 100),
            (RunDPSMeterService.Format(stats.AllyHealing), Cyan, 100),
            (RunDPSMeterService.Format(stats.HealingReceived), Yellow, 100)
        });
        row.TooltipText = L("HEALING_HELP");
        row.MouseFilter = Control.MouseFilterEnum.Pass;
        return row;
    }

    private Control BuildTabBar()
    {
        HBoxContainer tabs = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        tabs.AddThemeConstantOverride("separation", 4);

        _meterTabBtn = BuildTabButton(L("TAB_METER"), OverlayTab.Meter);
        _cardsTabBtn = BuildTabButton(L("TAB_CARDS"), OverlayTab.CardUsage);
        _receivedTabBtn = BuildTabButton(L("TAB_RECEIVED"), OverlayTab.ReceivedDamage);

        tabs.AddChild(_meterTabBtn);
        tabs.AddChild(_cardsTabBtn);
        tabs.AddChild(_receivedTabBtn);
        _supportTabBtn = BuildTabButton(L("TAB_SUPPORT"), OverlayTab.Support);
        _supportTabBtn.TooltipText = L("SUPPORT_HELP");
        tabs.AddChild(_supportTabBtn);
        _killsTabBtn = BuildTabButton(L("TAB_KILLS"), OverlayTab.Kills);
        _killsTabBtn.TooltipText = L("KILLS_HELP");
        tabs.AddChild(_killsTabBtn);
        _healingTabBtn = BuildTabButton(L("TAB_HEALING"), OverlayTab.Healing);
        _healingTabBtn.TooltipText = L("HEALING_HELP");
        tabs.AddChild(_healingTabBtn);
        RefreshTabButtons();
        return tabs;
    }

    private Button BuildTabButton(string text, OverlayTab tab)
    {
        Button button = new()
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Stop,
            FocusMode = Control.FocusModeEnum.None,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 24)
        };
        button.AddThemeFontSizeOverride("font_size", 11);
        button.Pressed += () => SetActiveTab(tab);
        return button;
    }

    private void SetActiveTab(OverlayTab tab)
    {
        if (_activeTab == tab)
            return;

        _activeTab = tab;
        RefreshTabButtons();
        RefreshColumnHeadings();

        if (_lastState != null)
            ApplyState(RunDPSMeterService.BuildOverlayState());
        else
            QueueLayoutRefresh();
    }

    private void RefreshTabButtons()
    {
        ApplyTabStyle(_meterTabBtn, _activeTab == OverlayTab.Meter);
        ApplyTabStyle(_cardsTabBtn, _activeTab == OverlayTab.CardUsage);
        ApplyTabStyle(_receivedTabBtn, _activeTab == OverlayTab.ReceivedDamage);
        ApplyTabStyle(_supportTabBtn, _activeTab == OverlayTab.Support);
        ApplyTabStyle(_killsTabBtn, _activeTab == OverlayTab.Kills);
        ApplyTabStyle(_healingTabBtn, _activeTab == OverlayTab.Healing);
    }

    private static void ApplyTabStyle(Button? button, bool active)
    {
        if (button == null)
            return;

        button.AddThemeColorOverride("font_color", active ? White : Gray);
        button.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = active ? new Color("FFFFFF20") : new Color("FFFFFF0C"),
            BorderColor = active ? BorderActive : Border,
            BorderWidthBottom = active ? 2 : 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
        });
        button.AddThemeStyleboxOverride("hover", new StyleBoxFlat
        {
            BgColor = active ? new Color("FFFFFF28") : new Color("FFFFFF18"),
            BorderColor = active ? BorderActive : Border,
            BorderWidthBottom = active ? 2 : 1,
            CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
            CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3
        });
    }

    // ── Column headings ────────────────────────────────────────

    private void RefreshColumnHeadings()
    {
        if (_columnHeadings == null)
            return;

        foreach (Node child in _columnHeadings.GetChildren())
        {
            _columnHeadings.RemoveChild(child);
            child.QueueFree();
        }

        _columnHeadings.AddChild(Spacer(40));
        _columnHeadings.AddChild(HeadLabel(L("PLAYER"), true));

        switch (_activeTab)
        {
            case OverlayTab.Meter:
                _columnHeadings.AddChild(HeadLabel(L("PCT"), false, 38));
                _columnHeadings.AddChild(HeadLabel(L("TOTAL"), false, 62));
                _columnHeadings.AddChild(HeadLabel(L("COMBAT"), false, 62));
                _columnHeadings.AddChild(HeadLabel(L("LAST"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("MAX"), false, 52));
                break;
            case OverlayTab.CardUsage:
                _columnHeadings.AddChild(HeadLabel(L("CARDS"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("ATTACK"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("SKILL"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("POWER"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("AUTO"), false, 52));
                break;
            case OverlayTab.Kills:
                _columnHeadings.AddChild(HeadLabel(L("KILLS"), false, 78));
                _columnHeadings.AddChild(HeadLabel(L("BOSS_KILLS"), false, 78));
                _columnHeadings.AddChild(HeadLabel(L("DEATHS"), false, 54));
                _columnHeadings.AddChild(HeadLabel(L("LAST_BOSS"), false, 140));
                break;
            case OverlayTab.Healing:
                foreach (var heading in new[] { "HEALING_DONE", "ALLY_HEALING", "HEALING_RECEIVED" })
                    _columnHeadings.AddChild(HeadLabel(L(heading), false, 100));
                break;
            case OverlayTab.Support:
                foreach (var heading in new[] { "BLOCK_GIVEN", "ALLY_BLOCK", "ALLY_BUFFS", "DEBUFFS", "ALLY_SKILLS" })
                    _columnHeadings.AddChild(HeadLabel(L(heading), false, 70));
                break;
            case OverlayTab.ReceivedDamage:
                _columnHeadings.AddChild(HeadLabel(L("INCOMING"), false, 62));
                _columnHeadings.AddChild(HeadLabel(L("BLOCKED"), false, 62));
                _columnHeadings.AddChild(HeadLabel(L("HP_LOST"), false, 62));
                _columnHeadings.AddChild(HeadLabel(L("MAX"), false, 52));
                _columnHeadings.AddChild(HeadLabel(L("BLOCK_GAINED"), false, 52));
                break;
        }
    }

    // ── Player row ─────────────────────────────────────────────

    private Control CreateRow(PlayerDamageSnapshot snap, float ratio)
    {
        bool active = snap.IsActive;
        Color theme = GetCharTheme(snap.CharacterName);

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 4);
        mp.AddThemeConstantOverride("margin_bottom", 4);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);

        // Avatar 32x32
        row.AddChild(BuildAvatar(snap, 32));
        row.AddChild(Spacer(8));

        // Name + character stacked
        VBoxContainer nameCol = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameCol.AddThemeConstantOverride("separation", 0);

        Label nameLabel = MakeLabel(snap.DisplayName, 13, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;

        Label charLabel = MakeLabel(snap.CharacterName, 10, new Color(theme, 0.7f));
        charLabel.ClipText = true;

        nameCol.AddChild(nameLabel);
        nameCol.AddChild(charLabel);
        row.AddChild(nameCol);

        // Percentage label
        int pct = (int)(ratio * 100f);
        row.AddChild(StatCell($"{pct}%", new Color(theme, 0.9f), 38));

        // Stat columns — right-aligned, fixed width
        row.AddChild(StatCell(RunDPSMeterService.Format(snap.TotalDamage), Yellow, 62));
        row.AddChild(StatCell(RunDPSMeterService.Format(snap.CombatDamage), Cyan, 62));
        row.AddChild(StatCell(RunDPSMeterService.Format(snap.LastDamage), Red, 52));
        row.AddChild(StatCell(RunDPSMeterService.Format(snap.MaxHitDamage), new Color("FF79C6"), 52));

        mp.AddChild(row);

        // ── Damage share bar ──
        Control barBg = new()
        {
            CustomMinimumSize = new Vector2(0, 3),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };

        ColorRect barTrack = new()
        {
            Color = new Color("FFFFFF0A"),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        barTrack.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        ColorRect barFill = new()
        {
            Color = new Color(theme, 0.6f),
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0, AnchorTop = 0,
            AnchorRight = Mathf.Clamp(ratio, 0f, 1f),
            AnchorBottom = 1,
            OffsetLeft = 0, OffsetTop = 0, OffsetRight = 0, OffsetBottom = 0
        };

        barBg.AddChild(barTrack);
        barBg.AddChild(barFill);

        VBoxContainer cardContent = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        cardContent.AddThemeConstantOverride("separation", 0);
        cardContent.AddChild(mp);
        cardContent.AddChild(barBg);

        card.AddChild(cardContent);

        // Flash animation on update
        card.Modulate = new Color(1, 1, 1, 0.3f);
        card.TreeEntered += () =>
        {
            Tween? tw = card.CreateTween();
            tw?.TweenProperty(card, "modulate", new Color(1, 1, 1, 1), 0.35f)
               .SetTrans(Tween.TransitionType.Cubic)
               .SetEase(Tween.EaseType.Out);
        };

        return card;
    }

    private Control CreateCardUsageRow(PlayerDamageSnapshot snap)
    {
        return CreateStatsRow(
            snap,
            new[]
            {
                (RunDPSMeterService.Format(Stats(snap).Cards), Yellow, 52),
                (RunDPSMeterService.Format(Stats(snap).Attacks), Red, 52),
                (RunDPSMeterService.Format(Stats(snap).Skills), Cyan, 52),
                (RunDPSMeterService.Format(Stats(snap).Powers), new Color("FF79C6"), 52),
                (RunDPSMeterService.Format(Stats(snap).Auto), Green, 52)
            });
    }

    private Control CreateReceivedDamageRow(PlayerDamageSnapshot snap)
    {
        return CreateStatsRow(
            snap,
            new[]
            {
                (RunDPSMeterService.Format(Stats(snap).Incoming), Yellow, 62),
                (RunDPSMeterService.Format(Stats(snap).Blocked), Cyan, 62),
                (RunDPSMeterService.Format(Stats(snap).HpLost), Red, 62),
                (RunDPSMeterService.Format(Stats(snap).MaxHpLost), new Color("FF79C6"), 52),
                (RunDPSMeterService.Format(Stats(snap).BlockReceived), Green, 52)
            });
    }

    private Control CreateStatsRow(PlayerDamageSnapshot snap, IEnumerable<(string Value, Color Color, int Width)> cells)
    {
        bool active = snap.IsActive;
        Color theme = GetCharTheme(snap.CharacterName);

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 4);
        mp.AddThemeConstantOverride("margin_bottom", 4);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);
        row.AddChild(BuildAvatar(snap, 32));
        row.AddChild(Spacer(8));

        VBoxContainer nameCol = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        nameCol.AddThemeConstantOverride("separation", 0);
        Label nameLabel = MakeLabel(snap.DisplayName, 13, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        Label charLabel = MakeLabel(snap.CharacterName, 10, new Color(theme, 0.7f));
        charLabel.ClipText = true;
        nameCol.AddChild(nameLabel);
        nameCol.AddChild(charLabel);
        row.AddChild(nameCol);

        foreach ((string value, Color color, int width) in cells)
            row.AddChild(StatCell(value, color, width));

        mp.AddChild(row);
        card.AddChild(mp);
        return card;
    }

    // ── Avatar ─────────────────────────────────────────────────

    private static Control BuildAvatar(PlayerDamageSnapshot snap, int size)
    {
        Texture2D? tex = LoadIcon(snap.CharacterName) ?? snap.PortraitTexture;

        if (tex != null)
        {
            TextureRect img = new()
            {
                Texture = tex,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                CustomMinimumSize = new Vector2(size, size),
                SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
            };
            return img;
        }

        // Colored square fallback with initials
        PanelContainer frame = new()
        {
            CustomMinimumSize = new Vector2(size, size),
            SizeFlagsVertical = Control.SizeFlags.ShrinkCenter
        };
        frame.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = Color.FromHsv((snap.PlayerKey % 360) / 360f, 0.4f, 0.5f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        CenterContainer cc = new();
        cc.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        Label ini = MakeLabel(Initials(snap), 12, White);
        cc.AddChild(ini);
        frame.AddChild(cc);
        return frame;
    }

    // ── Toggle expand / collapse ───────────────────────────────

    private void OnToggle()
    {
        _expanded = !_expanded;
        RefreshChrome();

        if (_lastState != null)
            ApplyState(RunDPSMeterService.BuildOverlayState());
        else
            QueueLayoutRefresh();
    }

    private void OnHideToSide()
    {
        if (_root == null)
            return;

        _hiddenSide = ResolveHiddenSide();
        _hiddenToSide = true;
        RefreshChrome();
        UpdateSideTabPosition();
    }

    private void OnRestoreFromSide()
    {
        _hiddenToSide = false;
        RefreshChrome();
        SnapRootIntoViewFromSide();
        if (_lastState != null)
            ApplyState(RunDPSMeterService.BuildOverlayState());
        else
            QueueLayoutRefresh();
    }

    // ── Compact row (icon + name + total only) ────────────────

    private Control CreateCompactRow(PlayerDamageSnapshot snap, float ratio)
    {
        Color theme = GetCharTheme(snap.CharacterName);
        bool active = snap.IsActive;

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 2);
        mp.AddThemeConstantOverride("margin_bottom", 2);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);

        // Avatar 24x24
        row.AddChild(BuildAvatar(snap, 24));
        row.AddChild(Spacer(6));

        // Name only (no character subtitle)
        Label nameLabel = MakeLabel(snap.DisplayName, 12, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);

        // Total damage
        row.AddChild(StatCell(RunDPSMeterService.Format(snap.TotalDamage), Yellow, 62));

        mp.AddChild(row);
        card.AddChild(mp);

        card.Modulate = new Color(1, 1, 1, 0.3f);
        card.TreeEntered += () =>
        {
            Tween? tw = card.CreateTween();
            tw?.TweenProperty(card, "modulate", new Color(1, 1, 1, 1), 0.35f)
               .SetTrans(Tween.TransitionType.Cubic)
               .SetEase(Tween.EaseType.Out);
        };

        return card;
    }

    private Control CreateCompactStatsRow(PlayerDamageSnapshot snap, string value, Color valueColor)
    {
        Color theme = GetCharTheme(snap.CharacterName);
        bool active = snap.IsActive;

        PanelContainer card = new()
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 28)
        };
        card.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(theme, active ? 0.18f : 0.08f),
            BorderColor = active ? new Color(theme, 0.9f) : new Color(theme, 0.3f),
            BorderWidthLeft = 3,
            BorderWidthTop = 0, BorderWidthRight = 0, BorderWidthBottom = 0,
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4
        });

        MarginContainer mp = new();
        mp.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        mp.AddThemeConstantOverride("margin_left", 6);
        mp.AddThemeConstantOverride("margin_right", 6);
        mp.AddThemeConstantOverride("margin_top", 2);
        mp.AddThemeConstantOverride("margin_bottom", 2);

        HBoxContainer row = new() { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 0);
        row.AddChild(BuildAvatar(snap, 24));
        row.AddChild(Spacer(6));

        Label nameLabel = MakeLabel(snap.DisplayName, 12, active ? theme.Lightened(0.3f) : White);
        nameLabel.ClipText = true;
        nameLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        row.AddChild(nameLabel);
        row.AddChild(StatCell(value, valueColor, 62));

        mp.AddChild(row);
        card.AddChild(mp);
        return card;
    }

    // ── State update ───────────────────────────────────────────

    private void OnChanged(OverlayState s)
    {
        if (_shuttingDown || !IsInsideTree() || _statsRefreshQueued) return;
        _statsRefreshQueued = true;
        Callable.From(() =>
        {
            _statsRefreshQueued = false;
            if (!_shuttingDown && IsInsideTree()) ApplyState(RunDPSMeterService.BuildOverlayState());
        }).CallDeferred();
    }

    private void ApplyState(OverlayState s)
    {
        if (_shuttingDown || _rows == null || _emptyLabel == null) return;
        Visible = s.Players.Count > 0;

        bool resetScroll = !string.Equals(_lastAppliedRunToken, s.RunToken, System.StringComparison.Ordinal)
            || _lastAppliedCombatIndex != s.CombatIndex;
        _lastAppliedRunToken = s.RunToken;
        _lastAppliedCombatIndex = s.CombatIndex;
        _resetScrollPending |= resetScroll;

        _lastState = s;

        RefreshTabButtons();
        RefreshColumnHeadings();

        // Hide column headings and separator in compact mode
        if (_columnHeadings != null) _columnHeadings.Visible = _expanded;
        if (_separator != null) _separator.Visible = _expanded;

        foreach (Node c in _rows.GetChildren())
        {
            _rows.RemoveChild(c);
            c.QueueFree();
        }

        _emptyLabel.Visible = s.Players.Count == 0;

        decimal teamTotal = 0;
        for (int i = 0; i < s.Players.Count; i++)
            teamTotal += s.Players[i].TotalDamage;

        var displayPlayers = _activeTab == OverlayTab.Meter ? s.Players : s.Players.OrderBy(p => p.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        for (int i = 0; i < displayPlayers.Count; i++)
        {
            float ratio = teamTotal > 0 ? (float)(s.Players[i].TotalDamage / teamTotal) : 0f;
            _rows.AddChild(CreatePlayerRowForActiveTab(displayPlayers[i], ratio));
        }

        RefreshChrome();
        QueueLayoutRefresh();
    }

    private Control CreatePlayerRowForActiveTab(PlayerDamageSnapshot snap, float damageRatio)
    {
        if (!_expanded)
        {
            return _activeTab switch
            {
                OverlayTab.Healing => CreateCompactStatsRow(snap, L("ALLY_HEALING") + " " + RunDPSMeterService.Format(Stats(snap).AllyHealing), Green),
                OverlayTab.Kills => CreateCompactStatsRow(snap, L("KILLS") + " " + Stats(snap).Kills + " | " + L("BOSS_KILLS") + " " + Stats(snap).BossKills, Yellow),
                OverlayTab.Support => CreateCompactStatsRow(snap, L("ALLY_BLOCK") + " " + RunDPSMeterService.Format(Stats(snap).AllyBlock) + " | " + L("ALLY_BUFFS") + " " + Stats(snap).AllyBuffs, Cyan),
                OverlayTab.CardUsage => CreateCompactStatsRow(snap, RunDPSMeterService.Format(Stats(snap).Cards), Yellow),
                OverlayTab.ReceivedDamage => CreateCompactStatsRow(snap, RunDPSMeterService.Format(Stats(snap).HpLost), Red),
                _ => CreateCompactRow(snap, damageRatio)
            };
        }

        return _activeTab switch
        {
            OverlayTab.Healing => CreateHealingRow(snap),
            OverlayTab.Kills => CreateKillsRow(snap),
            OverlayTab.Support => CreateSupportRow(snap),
            OverlayTab.CardUsage => CreateCardUsageRow(snap),
            OverlayTab.ReceivedDamage => CreateReceivedDamageRow(snap),
            _ => CreateRow(snap, damageRatio)
        };
    }

    // ── Drag ───────────────────────────────────────────────────

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_isDragging && @event is InputEventMouseButton mb && !mb.Pressed && mb.ButtonIndex == MouseButton.Left)
            _isDragging = false;
    }

    private void OnGuiInput(InputEvent @event)
    {
        if (_root == null) return;
        switch (@event)
        {
            case InputEventMouseButton mb when mb.ButtonIndex == MouseButton.Left:
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _dragOffset = GetViewport().GetMousePosition() - _root.Position;
                    GetViewport().SetInputAsHandled();
                }
                else _isDragging = false;
                break;
            case InputEventMouseMotion when _isDragging:
                ClampPos(GetViewport().GetMousePosition() - _dragOffset);
                GetViewport().SetInputAsHandled();
                break;
        }
    }

    private void ClampPos(Vector2 p)
    {
        if (_root == null) return;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        Vector2 sz = GetCurrentRootSize();
        _root.Position = new Vector2(
            Mathf.Clamp(p.X, 0, Mathf.Max(0, vp.X - sz.X)),
            Mathf.Clamp(p.Y, 0, Mathf.Max(0, vp.Y - sz.Y)));
        UpdateSideTabPosition();
    }

    private void RefreshChrome()
    {
        if (_root == null)
            return;

        if (_toggleBtn != null)
            _toggleBtn.Text = _expanded ? "\u25bc" : "\u25b6";

        if (_hideBtn != null)
            _hideBtn.Text = _hiddenSide == HiddenDockSide.Left ? "\u00ab" : "\u00bb";

        _root.Visible = !_hiddenToSide;
        if (_sideTab != null)
        {
            _sideTab.Visible = _hiddenToSide;
            _sideTab.Text = _hiddenSide == HiddenDockSide.Left ? $"\u00bb {L("TITLE")}" : $"{L("TITLE")} \u00ab";
        }

        float width = _expanded ? ExpandedWidth : CompactWidth;
        _root.CustomMinimumSize = new Vector2(width, _root.CustomMinimumSize.Y);

        if (_contentPad != null)
            _contentPad.Visible = !_hiddenToSide;

        UpdateSideTabPosition();
        if (!_hiddenToSide)
            QueueLayoutRefresh();
    }

    private HiddenDockSide ResolveHiddenSide()
    {
        if (_root == null)
            return HiddenDockSide.Right;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float centerX = _root.Position.X + GetCurrentRootSize().X * 0.5f;
        return centerX < viewportSize.X * 0.5f ? HiddenDockSide.Left : HiddenDockSide.Right;
    }

    private void SnapRootIntoViewFromSide()
    {
        if (_root == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float width = GetCurrentRootSize().X;
        float margin = 12f;
        float x = _hiddenSide == HiddenDockSide.Left ? margin : viewportSize.X - width - margin;
        ClampPos(new Vector2(x, _root.Position.Y));
    }

    private void UpdateSideTabPosition()
    {
        if (_sideTab == null || _root == null)
            return;

        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float y = Mathf.Clamp(_root.Position.Y + 10f, 0f, Mathf.Max(0f, viewportSize.Y - SideTabHeight));
        float x = _hiddenSide == HiddenDockSide.Left ? 0f : Mathf.Max(0f, viewportSize.X - SideTabWidth);
        _sideTab.Position = new Vector2(x, y);
        _sideTab.Size = new Vector2(SideTabWidth, SideTabHeight);
    }

    private Vector2 GetCurrentRootSize()
    {
        if (_root == null)
            return Vector2.Zero;

        float width = _expanded ? ExpandedWidth : CompactWidth;
        float height = _root.Size.Y > 0 ? _root.Size.Y : _root.GetCombinedMinimumSize().Y;
        return new Vector2(width, height);
    }

    private void QueueLayoutRefresh()
    {
        if (_shuttingDown || _layoutRefreshQueued)
            return;

        _layoutRefreshQueued = true;
        Callable.From(RefreshLayoutMetrics).CallDeferred();
    }

    private void RefreshLayoutMetrics()
    {
        _layoutRefreshQueued = false;

        if (_shuttingDown || !IsInsideTree() || _hiddenToSide || _root == null || _bodyScroll == null || _bodyContent == null)
            return;

        float width = _expanded ? ExpandedWidth : CompactWidth;
        Vector2 viewportSize = GetViewport().GetVisibleRect().Size;
        float maxPanelHeight = Mathf.Max(MinPanelHeight, viewportSize.Y - ViewportMargin * 2f);
        float bodyContentHeight = _bodyContent.GetCombinedMinimumSize().Y;
        float scrollMinHeight = _bodyScroll.GetCombinedMinimumSize().Y;
        float fixedHeight = Mathf.Max(0f, _root.GetCombinedMinimumSize().Y - scrollMinHeight);
        float availableBodyHeight = Mathf.Max(0f, maxPanelHeight - fixedHeight);
        float bodyHeight = Mathf.Min(bodyContentHeight, availableBodyHeight);
        float panelHeight = fixedHeight + bodyHeight;

        _bodyScroll.CustomMinimumSize = new Vector2(0, bodyHeight);
        _root.CustomMinimumSize = new Vector2(width, panelHeight);
        _root.Size = new Vector2(width, panelHeight);

        int maxScroll = Mathf.Max(0, Mathf.CeilToInt(bodyContentHeight - bodyHeight));
        if (_resetScrollPending)
        {
            _bodyScroll.ScrollVertical = 0;
            _resetScrollPending = false;
        }
        else if (_bodyScroll.ScrollVertical > maxScroll)
        {
            _bodyScroll.ScrollVertical = maxScroll;
        }

        ClampPos(_root.Position);
    }

    // ── Icon loader ────────────────────────────────────────────

    private static Texture2D? LoadIcon(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        string key = name.Trim().ToLowerInvariant().Replace(' ', '_');
        if (IconCache.TryGetValue(key, out Texture2D? t)) return t;
        string path = $"res://images/ui/top_panel/character_icon_{key}.png";
        t = ResourceLoader.Exists(path) ? ResourceLoader.Load<Texture2D>(path) : null;
        IconCache[key] = t;
        return t;
    }

    // ── Tiny helpers ───────────────────────────────────────────

    private static Label MakeLabel(string text, int size, Color color)
    {
        Label l = new()
        {
            Text = text,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            VerticalAlignment = VerticalAlignment.Center
        };
        l.AddThemeFontSizeOverride("font_size", size);
        l.AddThemeColorOverride("font_color", color);
        return l;
    }

    private static Control HeadLabel(string text, bool expand, int width = 0)
    {
        Label l = MakeLabel(text, 10, DimGray);
        if (expand)
        {
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            l.ClipText = true;
        }
        else
        {
            l.CustomMinimumSize = new Vector2(width, 0);
            l.HorizontalAlignment = HorizontalAlignment.Right;
        }
        return l;
    }

    private static Control StatCell(string val, Color color, int width)
    {
        Label l = MakeLabel(val, 13, color);
        l.CustomMinimumSize = new Vector2(width, 0);
        l.HorizontalAlignment = HorizontalAlignment.Right;
        return l;
    }

    private static Control Spacer(int w)
    {
        Control c = new() { CustomMinimumSize = new Vector2(w, 0), MouseFilter = Control.MouseFilterEnum.Ignore };
        return c;
    }

    private static Control HLine()
    {
        ColorRect r = new()
        {
            Color = Border,
            CustomMinimumSize = new Vector2(0, 1),
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        return r;
    }

    private static Color GetCharTheme(string? charName)
    {
        if (!string.IsNullOrWhiteSpace(charName))
        {
            string key = charName.Trim().ToLowerInvariant().Replace(' ', '_');
            if (CharTheme.TryGetValue(key, out Color c)) return c;
        }
        return Gray;
    }

    private static string Initials(PlayerDamageSnapshot s)
    {
        string src = !string.IsNullOrWhiteSpace(s.CharacterName) ? s.CharacterName : s.DisplayName;
        string[] p = src.Split(new[] { ' ', '-', '_' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return "?";
        if (p.Length == 1) return p[0].Length >= 2 ? p[0][..2].ToUpperInvariant() : p[0].ToUpperInvariant();
        return string.Concat(p[0][0], p[1][0]).ToUpperInvariant();
    }

    public override void _Process(double _)
    {
        if (!_pendingCreate) return;
        _pendingCreate = false;
        GD.Print("[DPSMeter] _Process: calling EnsureCreated...");
        EnsureCreated();
    }

    public static void EnsureCreated()
    {
        if (_shuttingDown) return;
        if (IsInstanceValid(_instance))
        {
            return;
        }
        if (Engine.GetMainLoop() is not SceneTree tree || tree.Root == null)
        {
            GD.Print("[DPSMeter] EnsureCreated: game loop not ready, re-scheduling");
            _pendingCreate = true;
            return;
        }
        GD.Print("[DPSMeter] EnsureCreated: creating overlay now!");
        _instance = new DPSMeterOverlay();
        tree.Root.CallDeferred(Node.MethodName.AddChild, _instance);
    }

    private enum HiddenDockSide
    {
        Left,
        Right
    }

    private enum OverlayTab
    {
        Meter,
        CardUsage,
        ReceivedDamage,
        Support,
        Kills,
        Healing
    }
}
