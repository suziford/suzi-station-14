// TEMPORARY: Remove Content.Client/_Suziford/TempPerfProfiler/ after optimization work is finished.

using System;
using System.Globalization;
using System.Linq;
using System.Text;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Profiling;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Client._Suziford.TempPerfProfiler;

/// <summary>
/// TEMPORARY self-contained performance profiler &amp; BEFORE/AFTER comparison window.
/// Open via console command: tempprof (or suziprof).
/// </summary>
internal sealed class TempPerfProfilerWindow : DefaultWindow
{
    [Dependency] private readonly ProfManager _prof = default!;
    [Dependency] private readonly IEntityManager _entMan = default!;
    [Dependency] private readonly IOverlayManager _overlayMan = default!;
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IClyde _clyde = default!;
    [Dependency] private readonly IClipboardManager _clipboard = default!;

    private readonly TempPerfProfilerSession _session;
    private readonly bool _prevProfEnabled;

    private readonly LineEdit _labelEdit;
    private readonly Button _btnBench5;
    private readonly Button _btnBench10;
    private readonly Button _btnToggleRec;
    private readonly Button _btnReset;
    private readonly Button _btnSaveBaseline;
    private readonly Button _btnCopyReport;
    private readonly Button _btnCopyCompare;
    private readonly Label _statusLabel;

    private readonly RichTextLabel _overviewText;
    private readonly RichTextLabel _stagesText;
    private readonly RichTextLabel _compareText;
    private readonly RichTextLabel _helpText;

    private bool _recording = true;
    private float _timedRemaining = -1f;
    private float _uiRefreshAccum;
    private string _flashNotice = string.Empty;
    private float _flashNoticeTimer;

    private const float UiRefreshInterval = 0.25f;

    public TempPerfProfilerWindow()
    {
        IoCManager.InjectDependencies(this);

        Title = "[TEMP] Suzi Performance Profiler (Before / After)";
        MinSize = new System.Numerics.Vector2(880, 640);
        SetSize = new System.Numerics.Vector2(940, 700);

        _prevProfEnabled = _cfg.GetCVar(CVars.ProfEnabled);
        if (!_prevProfEnabled)
            _cfg.SetCVar(CVars.ProfEnabled, true);

        _session = new TempPerfProfilerSession(_prof, _entMan, _overlayMan, _cfg, _clyde);
        _session.Reset(clearGraph: true);

        var root = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Vertical,
            SeparationOverride = 6,
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        // Top controls row 1: Label + timed benchmarks + manual start/stop
        var row1 = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };

        row1.AddChild(new Label { Text = "Метка:" });
        _labelEdit = new LineEdit
        {
            Text = TempPerfProfilerStore.Baseline == null ? "ДО (база)" : "ПОСЛЕ",
            HorizontalExpand = true,
            MinWidth = 160,
        };
        row1.AddChild(_labelEdit);

        _btnBench5 = new Button { Text = "Замер 5с" };
        _btnBench5.OnPressed += _ => StartTimedBenchmark(5f);
        row1.AddChild(_btnBench5);

        _btnBench10 = new Button { Text = "Замер 10с" };
        _btnBench10.OnPressed += _ => StartTimedBenchmark(10f);
        row1.AddChild(_btnBench10);

        _btnToggleRec = new Button { Text = "Пауза" };
        _btnToggleRec.OnPressed += _ => ToggleRecording();
        row1.AddChild(_btnToggleRec);

        _btnReset = new Button { Text = "Сброс" };
        _btnReset.OnPressed += _ =>
        {
            _timedRemaining = -1f;
            _session.Reset(clearGraph: false);
            SetNotice("Счётчики замера сброшены.");
            RefreshUi();
        };
        row1.AddChild(_btnReset);

        root.AddChild(row1);

        // Top controls row 2: Baseline save + clipboard export
        var row2 = new BoxContainer
        {
            Orientation = BoxContainer.LayoutOrientation.Horizontal,
            SeparationOverride = 6,
        };

        _btnSaveBaseline = new Button { Text = "Сохранить как ДО (База)" };
        _btnSaveBaseline.OnPressed += _ => SaveBaseline();
        row2.AddChild(_btnSaveBaseline);

        _btnCopyReport = new Button { Text = "Копировать отчёт (MD)" };
        _btnCopyReport.OnPressed += _ => CopyCurrentReport();
        row2.AddChild(_btnCopyReport);

        _btnCopyCompare = new Button { Text = "Копировать сравнение ДО/ПОСЛЕ (MD)" };
        _btnCopyCompare.OnPressed += _ => CopyComparisonReport();
        row2.AddChild(_btnCopyCompare);

        root.AddChild(row2);

        // Status line
        _statusLabel = new Label
        {
            FontColorOverride = Color.LightSkyBlue,
        };
        root.AddChild(_statusLabel);

        // Live FrameTime graph
        var graphHeader = new Label
        {
            Text = "График фреймтайма (240 кадров; зел. линия = 16.7ms / 60 FPS, жёлт. = 33.3ms / 30 FPS, верх = 50ms):",
            FontColorOverride = Color.LightGray,
        };
        root.AddChild(graphHeader);
        root.AddChild(new TempPerfFrameGraphControl(_session));

        // Tabs
        var tabs = new TabContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
        };

        _overviewText = CreateTabText(tabs, "Обзор (Live)");
        _stagesText = CreateTabText(tabs, "Этапы и Топ систем");
        _compareText = CreateTabText(tabs, "Сравнение ДО / ПОСЛЕ");
        _helpText = CreateTabText(tabs, "Инструкция [TEMP]");

        FillHelpText();

        root.AddChild(tabs);
        Contents.AddChild(root);

        RefreshUi();
    }

    private static RichTextLabel CreateTabText(TabContainer tabs, string title)
    {
        var scroll = new ScrollContainer
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            HScrollEnabled = true,
        };

        var label = new RichTextLabel
        {
            HorizontalExpand = true,
            VerticalExpand = true,
            Margin = new Thickness(6),
        };

        scroll.AddChild(label);
        tabs.AddChild(scroll);
        TabContainer.SetTabTitle(scroll, title);
        return label;
    }

    private void StartTimedBenchmark(float seconds)
    {
        _session.Reset(clearGraph: false);
        _recording = true;
        _timedRemaining = seconds;
        _btnToggleRec.Text = "Стоп";
        SetNotice($"Идёт автозамер на {seconds:0} сек... Не двигай настройки, стой/ходи как в тесте.");
        RefreshUi();
    }

    private void ToggleRecording()
    {
        _timedRemaining = -1f;
        _recording = !_recording;
        _btnToggleRec.Text = _recording ? "Пауза" : "Старт";

        if (!_recording && _session.FrameCount > 0)
        {
            TempPerfProfilerStore.Latest = _session.BuildSnapshot(_labelEdit.Text);
            SetNotice($"Замер остановлен ({TempPerfProfilerStore.Latest.FrameCount} кадров).");
        }
        else if (_recording)
        {
            SetNotice("Запись продолжена.");
        }

        RefreshUi();
    }

    private void SaveBaseline()
    {
        var snap = _session.FrameCount > 0
            ? _session.BuildSnapshot(_labelEdit.Text)
            : TempPerfProfilerStore.Latest;

        if (snap == null || snap.FrameCount == 0)
        {
            SetNotice("Нет собранных кадров для сохранения базы!");
            return;
        }

        TempPerfProfilerStore.Baseline = snap;
        _labelEdit.Text = "ПОСЛЕ";
        SetNotice($"Сохранено как БАЗА (ДО): '{snap.Label}' ({snap.FrameCount} кадров, Avg {snap.AvgFps:F1} FPS).");
        RefreshUi();
    }

    private void CopyCurrentReport()
    {
        var snap = GetActiveSnapshot();
        if (snap == null || snap.FrameCount == 0)
        {
            SetNotice("Нечего копировать — кадров ещё нет.");
            return;
        }

        _clipboard.SetText(snap.ToMarkdownReport());
        SetNotice($"Отчёт '{snap.Label}' скопирован в буфер обмена (Markdown).");
    }

    private void CopyComparisonReport()
    {
        if (TempPerfProfilerStore.Baseline == null)
        {
            SetNotice("Сначала нажми 'Сохранить как ДО (База)'!");
            return;
        }

        var after = GetActiveSnapshot();
        if (after == null || after.FrameCount == 0)
        {
            SetNotice("Нет текущего замера для сравнения!");
            return;
        }

        var md = TempPerfBenchmarkSnapshot.BuildComparisonMarkdown(TempPerfProfilerStore.Baseline, after);
        _clipboard.SetText(md);
        SetNotice("Таблица сравнения ДО/ПОСЛЕ скопирована в буфер обмена (Markdown).");
    }

    private TempPerfBenchmarkSnapshot? GetActiveSnapshot()
    {
        if (_session.FrameCount > 0)
            return _session.BuildSnapshot(_labelEdit.Text);

        return TempPerfProfilerStore.Latest;
    }

    private void SetNotice(string text)
    {
        _flashNotice = text;
        _flashNoticeTimer = 5f;
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        _session.PollFrames(_recording);

        if (_timedRemaining > 0f)
        {
            _timedRemaining -= args.DeltaSeconds;
            if (_timedRemaining <= 0f)
            {
                _timedRemaining = -1f;
                _recording = false;
                _btnToggleRec.Text = "Старт";
                TempPerfProfilerStore.Latest = _session.BuildSnapshot(_labelEdit.Text);
                SetNotice($"Автозамер завершён! Кадров: {TempPerfProfilerStore.Latest.FrameCount}, Avg: {TempPerfProfilerStore.Latest.AvgFps:F1} FPS.");
                RefreshUi();
                return;
            }
        }

        if (_flashNoticeTimer > 0f)
        {
            _flashNoticeTimer -= args.DeltaSeconds;
            if (_flashNoticeTimer <= 0f)
                _flashNotice = string.Empty;
        }

        _uiRefreshAccum += args.DeltaSeconds;
        if (_uiRefreshAccum >= UiRefreshInterval)
        {
            _uiRefreshAccum = 0f;
            RefreshUi();
        }
    }

    public override void Close()
    {
        // Restore prof.enabled to whatever it was before opening this temporary window.
        if (!_prevProfEnabled)
            _cfg.SetCVar(CVars.ProfEnabled, false);

        base.Close();
    }

    private void RefreshUi()
    {
        var baseInfo = TempPerfProfilerStore.Baseline != null
            ? $"БАЗА (ДО): '{TempPerfProfilerStore.Baseline.Label}' ({TempPerfProfilerStore.Baseline.AvgFps:F1} FPS, {TempPerfProfilerStore.Baseline.FrameCount} к.)"
            : "БАЗА (ДО): не сохранена";

        var stateText = _timedRemaining > 0f
            ? $"АВТОЗАМЕР (осталось {_timedRemaining:F1}с)"
            : _recording
                ? "ЗАПИСЬ"
                : "ПАУЗА";

        _statusLabel.Text = string.IsNullOrEmpty(_flashNotice)
            ? $"[{stateText}] Кадров: {_session.FrameCount} ({_session.ElapsedSeconds:F1}с) | {baseInfo}"
            : $"{_flashNotice} | {baseInfo}";

        var snap = GetActiveSnapshot();
        if (snap == null)
            return;

        UpdateOverviewTab(snap);
        UpdateStagesTab(snap);
        UpdateCompareTab(snap);
    }

    private void UpdateOverviewTab(TempPerfBenchmarkSnapshot s)
    {
        var sb = new StringBuilder(1536);

        sb.AppendLine("[bold][color=#7ec8ff]=== КАДРЫ И ПЛАВНОСТЬ ===[/color][/bold]");
        sb.AppendLine($"FPS:        [bold]Avg {s.AvgFps:F1}[/bold]  |  1% Low: [color=#ffd166]{s.OnePercentLowFps:F1}[/color]  |  0.1% Low: [color=#ff8a65]{s.PointOnePercentLowFps:F1}[/color]");
        sb.AppendLine($"FrameTime:  Avg {s.AvgFrameMs:F2} ms  |  Min {s.MinFrameMs:F2} ms  |  P95 {s.P95FrameMs:F2} ms  |  P99 {s.P99FrameMs:F2} ms  |  Max [color=#ff6b6b]{s.MaxFrameMs:F2} ms[/color]");
        sb.AppendLine($"Микрофризы: >33.3ms (<30 FPS): [color=#ffd166]{s.Hitches33Ms}[/color]  |  >50ms (<20 FPS): [color=#ff6b6b]{s.Hitches50Ms}[/color]  |  Всего кадров: {s.FrameCount} ({s.DurationSeconds:F1}s)");
        sb.AppendLine();

        sb.AppendLine("[bold][color=#7ec8ff]=== ПАМЯТЬ / АЛЛОКАЦИИ (GC) ===[/color][/bold]");
        sb.AppendLine($"Аллокации:  {s.AvgAllocKbPerFrame:F1} KB / кадр  ({s.AllocMbPerSec:F2} MB / сек)");
        sb.AppendLine();

        sb.AppendLine("[bold][color=#7ec8ff]=== РЕНДЕР (CLYDE / GPU) ===[/color][/bold]");
        sb.AppendLine($"Draw Calls: GL: [bold]{s.AvgGlDrawCalls:F1}[/bold]  |  Clyde: {s.AvgClydeDrawCalls:F1}  |  Batches: {s.AvgBatches:F1}  |  Max Batch Verts: {s.AvgMaxBatchVerts:F0}");
        sb.AppendLine($"Свет:       Lights: {s.AvgLights:F1}  |  Shadowcasting Lights: [color=#ffd166]{s.AvgShadowLights:F1}[/color]  |  Occluders: {s.AvgOccluders:F1}");
        sb.AppendLine();

        sb.AppendLine("[bold][color=#7ec8ff]=== СЦЕНА НА КЛИЕНТЕ ===[/color][/bold]");
        sb.AppendLine($"Сущности:   {s.Scene.TotalEntities}  |  Спрайты: {s.Scene.VisibleSprites} видимых / {s.Scene.TotalSprites} всего");
        sb.AppendLine($"PostShader: [color=#ffd166]{s.Scene.PostShaderSprites}[/color] сущностей ({s.Scene.PostShaderCount} активных пост-шейдеров — каждый рвёт батчинг!)");
        sb.AppendLine($"Декали:     {s.Scene.TotalDecals} шт. ({s.Scene.DecalChunks} чанков)  |  Газ-чанки: {s.Scene.GasChunks}");
        sb.AppendLine($"Оверлеи:    {s.Scene.ActiveOverlays} активных  |  Из них RequestScreenTexture: [color=#ffd166]{s.Scene.ScreenTextureOverlays}[/color]");
        sb.AppendLine($"Экран:      {s.Scene.ScreenSize.X}x{s.Scene.ScreenSize.Y}");
        sb.AppendLine();

        sb.AppendLine("[bold][color=#7ec8ff]=== АКТИВНЫЕ НАСТРОЙКИ ГРАФИКИ (CVars) ===[/color][/bold]");
        sb.AppendLine($"ViewportScaleRender: {s.Config.ViewportScaleRender}  |  VSync: {s.Config.VSync}  |  MaxFPS: {s.Config.MaxFps}");
        sb.AppendLine($"LightScale: {s.Config.LightResScale:0.###}  |  SoftShadows: {s.Config.LightSoftShadows}  |  LightBlur: {s.Config.LightBlur}");
        sb.AppendLine($"MaxShadowLights: {s.Config.MaxShadowLights}  |  MaxLights: {s.Config.MaxLights}  |  MaxOccluders: {s.Config.MaxOccluders}");
        sb.AppendLine($"AmbientOcclusion: {s.Config.AmbientOcclusion}  |  ParticleQuality: {s.Config.ParticleQuality}  |  AudioHRTF: {s.Config.AudioHrtf}");
        sb.AppendLine($"ParallaxEnabled: {s.Config.ParallaxEnabled}  |  ParallaxLowQuality: {s.Config.ParallaxLowQuality}");

        _overviewText.SetMessage(FormattedMessage.FromMarkupPermissive(sb.ToString()));
    }

    private void UpdateStagesTab(TempPerfBenchmarkSnapshot s)
    {
        var sb = new StringBuilder(2048);

        sb.AppendLine("[bold][color=#7ec8ff]=== ОСНОВНЫЕ ЭТАПЫ КАДРА (усреднено по кадрам) ===[/color][/bold]");
        sb.AppendLine($"{"Этап",-36} {"Avg ms",9} {"Max ms",9} {"Alloc KB/f",11}");
        sb.AppendLine(new string('-', 68));

        foreach (var stage in TempPerfProfilerSession.TrackedStageOrder)
        {
            if (!s.KeyStages.TryGetValue(stage, out var st))
                continue;

            var color = st.AvgMs >= 4.0 ? "#ff6b6b" : st.AvgMs >= 1.5 ? "#ffd166" : "#d8dee9";
            sb.AppendLine($"[color={color}]{FormattedMessage.EscapeText(st.Name),-36} {st.AvgMs,9:F3} {st.MaxMs,9:F2} {st.AvgAllocKb,11:F1}[/color]");
        }

        sb.AppendLine();
        sb.AppendLine("[bold][color=#7ec8ff]=== ТОП-15 ТЯЖЁЛЫХ СИСТЕМ (EntitySystem Update / FrameUpdate) ===[/color][/bold]");
        sb.AppendLine($"{"Система",-44} {"Avg ms",9} {"Max ms",9} {"Alloc KB/f",11}");
        sb.AppendLine(new string('-', 76));

        foreach (var leaf in s.TopLeaves)
        {
            var color = leaf.AvgMs >= 1.5 ? "#ff6b6b" : leaf.AvgMs >= 0.4 ? "#ffd166" : "#d8dee9";
            sb.AppendLine($"[color={color}]{FormattedMessage.EscapeText(leaf.Name),-44} {leaf.AvgMs,9:F3} {leaf.MaxMs,9:F2} {leaf.AvgAllocKb,11:F1}[/color]");
        }

        if (s.TopGroups.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("[bold][color=#7ec8ff]=== ПРОЧИЕ ТЯЖЁЛЫЕ ГРУППЫ ===[/color][/bold]");
            sb.AppendLine($"{"Группа",-44} {"Avg ms",9} {"Max ms",9} {"Alloc KB/f",11}");
            sb.AppendLine(new string('-', 76));

            foreach (var grp in s.TopGroups.Take(10))
            {
                sb.AppendLine($"{FormattedMessage.EscapeText(grp.Name),-44} {grp.AvgMs,9:F3} {grp.MaxMs,9:F2} {grp.AvgAllocKb,11:F1}");
            }
        }

        _stagesText.SetMessage(FormattedMessage.FromMarkupPermissive(sb.ToString()));
    }

    private void UpdateCompareTab(TempPerfBenchmarkSnapshot after)
    {
        var before = TempPerfProfilerStore.Baseline;
        if (before == null)
        {
            _compareText.SetMessage(FormattedMessage.FromMarkupPermissive(
                "[color=#ffd166]База (ДО) ещё не сохранена.[/color]\n\n" +
                "Как сравнивать:\n" +
                "1. Встань в тестовую точку на карте (например, бар / холл / при пожаре).\n" +
                "2. Нажми [bold]Замер 5с[/bold] или [bold]Замер 10с[/bold].\n" +
                "3. Нажми [bold]Сохранить как ДО (База)[/bold].\n" +
                "4. Поменяй настройки / примени фиксы, сделай второй замер — здесь сразу появятся дельты и проценты!\n" +
                "5. Если перезапускаешь клиент после правок кода — перед выходом нажми [bold]Копировать отчёт (MD)[/bold] и сохрани."));
            return;
        }

        var sb = new StringBuilder(2048);
        sb.AppendLine($"[bold][color=#7ec8ff]=== СРАВНЕНИЕ: '{FormattedMessage.EscapeText(before.Label)}' -> '{FormattedMessage.EscapeText(after.Label)}' ===[/color][/bold]");
        sb.AppendLine($"Кадров: {before.FrameCount} ({before.DurationSeconds:F1}s) -> {after.FrameCount} ({after.DurationSeconds:F1}s)");
        sb.AppendLine();
        sb.AppendLine($"{"Метрика",-28} {"ДО",10} {"ПОСЛЕ",10} {"Δ",10} {"%",9}");
        sb.AppendLine(new string('-', 71));

        AppendCompareLine(sb, "Avg FPS", before.AvgFps, after.AvgFps, "F1", higherIsBetter: true);
        AppendCompareLine(sb, "1% Low FPS", before.OnePercentLowFps, after.OnePercentLowFps, "F1", higherIsBetter: true);
        AppendCompareLine(sb, "0.1% Low FPS", before.PointOnePercentLowFps, after.PointOnePercentLowFps, "F1", higherIsBetter: true);
        AppendCompareLine(sb, "Avg Frame (ms)", before.AvgFrameMs, after.AvgFrameMs, "F2", higherIsBetter: false);
        AppendCompareLine(sb, "P95 Frame (ms)", before.P95FrameMs, after.P95FrameMs, "F2", higherIsBetter: false);
        AppendCompareLine(sb, "P99 Frame (ms)", before.P99FrameMs, after.P99FrameMs, "F2", higherIsBetter: false);
        AppendCompareLine(sb, "Max Frame (ms)", before.MaxFrameMs, after.MaxFrameMs, "F2", higherIsBetter: false);
        AppendCompareLine(sb, "Hitches >33ms", before.Hitches33Ms, after.Hitches33Ms, "F0", higherIsBetter: false);
        AppendCompareLine(sb, "Alloc (KB/frame)", before.AvgAllocKbPerFrame, after.AvgAllocKbPerFrame, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "GL Draw Calls", before.AvgGlDrawCalls, after.AvgGlDrawCalls, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "Clyde Draw Calls", before.AvgClydeDrawCalls, after.AvgClydeDrawCalls, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "Batches", before.AvgBatches, after.AvgBatches, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "Lights", before.AvgLights, after.AvgLights, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "Shadow Lights", before.AvgShadowLights, after.AvgShadowLights, "F1", higherIsBetter: false);
        AppendCompareLine(sb, "PostShader Sprites", before.Scene.PostShaderSprites, after.Scene.PostShaderSprites, "F0", higherIsBetter: false);

        sb.AppendLine();
        sb.AppendLine("[bold][color=#7ec8ff]=== ЭТАПЫ КАДРА (Avg ms) ===[/color][/bold]");
        sb.AppendLine($"{"Этап",-32} {"ДО",9} {"ПОСЛЕ",9} {"Δ",9} {"%",9}");
        sb.AppendLine(new string('-', 72));

        foreach (var stage in TempPerfProfilerSession.TrackedStageOrder)
        {
            var hasB = before.KeyStages.TryGetValue(stage, out var b);
            var hasA = after.KeyStages.TryGetValue(stage, out var a);
            if (!hasB && !hasA)
                continue;

            AppendCompareLine(sb, stage, hasB ? b.AvgMs : 0, hasA ? a.AvgMs : 0, "F3", higherIsBetter: false, nameWidth: 32, numWidth: 9);
        }

        _compareText.SetMessage(FormattedMessage.FromMarkupPermissive(sb.ToString()));
    }

    private static void AppendCompareLine(
        StringBuilder sb,
        string name,
        double before,
        double after,
        string fmt,
        bool higherIsBetter,
        int nameWidth = 28,
        int numWidth = 10)
    {
        var delta = after - before;
        var pct = Math.Abs(before) > 0.0001 ? (delta / before) * 100.0 : 0.0;

        string color;
        if (Math.Abs(pct) < 2.0)
            color = "#d8dee9";
        else if (higherIsBetter ? delta > 0 : delta < 0)
            color = "#6ee787";
        else
            color = "#ff6b6b";

        var bStr = before.ToString(fmt, CultureInfo.InvariantCulture);
        var aStr = after.ToString(fmt, CultureInfo.InvariantCulture);
        var dStr = (delta >= 0 ? "+" : "") + delta.ToString(fmt, CultureInfo.InvariantCulture);
        var pStr = (pct >= 0 ? "+" : "") + pct.ToString("F1", CultureInfo.InvariantCulture) + "%";

        sb.AppendLine($"[color={color}]{FormattedMessage.EscapeText(name).PadRight(nameWidth)} {bStr.PadLeft(numWidth)} {aStr.PadLeft(numWidth)} {dStr.PadLeft(numWidth)} {pStr.PadLeft(9)}[/color]");
    }

    private void FillHelpText()
    {
        var text =
            "[bold][color=#ffd166]ВРЕМЕННЫЙ ПРОФАЙЛЕР (TEMP)[/color][/bold]\n" +
            "Все файлы лежат строго в одной папке: [color=#7ec8ff]Content.Client/_Suziford/TempPerfProfiler/[/color]\n" +
            "После окончания оптимизации эту папку можно просто удалить — ни один другой файл сборки не затронут.\n\n" +
            "[bold]Команды консоли:[/bold]\n" +
            "  - [color=#7ec8ff]tempprof[/color] (или [color=#7ec8ff]suziprof[/color]) — открыть / закрыть окно профайлера.\n" +
            "  - [color=#7ec8ff]tempprof clear[/color] — сбросить сохранённую базу (ДО).\n\n" +
            "[bold]Как правильно делать честное сравнение:[/bold]\n" +
            "1. Выключи VSync (display.vsync = false) и сними лимит FPS (display.max_fps = 0), иначе упрёшься в потолок 60 FPS и не увидишь прирост.\n" +
            "2. Встань на фиксированной точке (например, центр станции / бар / планета с солнцем / пожар).\n" +
            "3. Нажми [bold]Замер 10с[/bold] -> [bold]Сохранить как ДО (База)[/bold] -> [bold]Копировать отчёт (MD)[/bold] (если будешь перезапускать клиент).\n" +
            "4. После изменений/настроек повтори [bold]Замер 10с[/bold] в той же точке и открой вкладку [bold]Сравнение ДО / ПОСЛЕ[/bold].\n\n" +
            "[color=#aaaaaa]Примечание: при открытии окна автоматически включается prof.enabled = true (чтобы движок отдавал полную статистику Clyde/этапов), а при закрытии возвращается прежнее значение.[/color]";

        _helpText.SetMessage(FormattedMessage.FromMarkupPermissive(text));
    }
}
