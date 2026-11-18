// TEMPORARY: Remove Content.Client/_Suziford/TempPerfProfiler/ after optimization work is finished.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;
using Content.Shared.Atmos.Components;
using Content.Goobstation.Common.CCVar;
using Content.Shared.CCVar;
using Content.Shared.Decals;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Shared;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Profiling;
using Robust.Shared.Utility;

namespace Content.Client._Suziford.TempPerfProfiler;

/// <summary>
/// TEMPORARY static storage so the saved BEFORE/AFTER benchmark snapshots survive closing and reopening the window.
/// </summary>
internal static class TempPerfProfilerStore
{
    public static TempPerfBenchmarkSnapshot? Baseline;
    public static TempPerfBenchmarkSnapshot? Latest;
}

internal sealed class TempPerfScopeAccumulator
{
    public double TotalMs;
    public double MaxMs;
    public long TotalAllocBytes;
    public int Frames;

    public double AvgMs(int totalFrames) => totalFrames > 0 ? TotalMs / totalFrames : 0;
    public double AvgAllocKb(int totalFrames) => totalFrames > 0 ? TotalAllocBytes / 1024.0 / totalFrames : 0;
}

internal readonly record struct TempPerfScopeSummary(
    string Name,
    double AvgMs,
    double MaxMs,
    double AvgAllocKb);

internal readonly record struct TempPerfSceneStats(
    int TotalEntities,
    int TotalSprites,
    int VisibleSprites,
    int PostShaderSprites,
    int PostShaderCount,
    int DecalChunks,
    int TotalDecals,
    int GasChunks,
    int ActiveOverlays,
    int ScreenTextureOverlays,
    Vector2i ScreenSize);

internal readonly record struct TempPerfGraphicsConfig(
    bool ViewportScaleRender,
    float LightResScale,
    bool LightSoftShadows,
    bool LightBlur,
    int MaxShadowLights,
    int MaxLights,
    int MaxOccluders,
    bool AmbientOcclusion,
    int ParticleQuality,
    bool ParallaxEnabled,
    bool ParallaxLowQuality,
    bool AudioHrtf,
    bool VSync,
    int MaxFps);

internal sealed class TempPerfBenchmarkSnapshot
{
    public string Label = string.Empty;
    public DateTime CapturedAtUtc;
    public double DurationSeconds;
    public int FrameCount;

    public double AvgFps;
    public double OnePercentLowFps;
    public double PointOnePercentLowFps;

    public double AvgFrameMs;
    public double MinFrameMs;
    public double P95FrameMs;
    public double P99FrameMs;
    public double MaxFrameMs;
    public int Hitches33Ms;
    public int Hitches50Ms;

    public double AvgAllocKbPerFrame;
    public double AllocMbPerSec;

    public double AvgGlDrawCalls;
    public double AvgClydeDrawCalls;
    public double AvgBatches;
    public double AvgMaxBatchVerts;
    public double AvgLights;
    public double AvgShadowLights;
    public double AvgOccluders;

    public TempPerfSceneStats Scene;
    public TempPerfGraphicsConfig Config;

    public Dictionary<string, TempPerfScopeSummary> KeyStages = new();
    public List<TempPerfScopeSummary> TopGroups = new();
    public List<TempPerfScopeSummary> TopLeaves = new();

    public string ToMarkdownReport()
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"## Perf Snapshot: {Label}");
        sb.AppendLine($"- Frames: `{FrameCount}` ({DurationSeconds:F1}s) | Captured: `{CapturedAtUtc:HH:mm:ss} UTC`");
        sb.AppendLine($"- FPS: **Avg `{AvgFps:F1}`** | 1% Low `{OnePercentLowFps:F1}` | 0.1% Low `{PointOnePercentLowFps:F1}`");
        sb.AppendLine($"- Frame Time (ms): Avg `{AvgFrameMs:F2}` | P95 `{P95FrameMs:F2}` | P99 `{P99FrameMs:F2}` | Max `{MaxFrameMs:F2}` | Hitches >33ms: `{Hitches33Ms}`, >50ms: `{Hitches50Ms}`");
        sb.AppendLine($"- Memory Alloc: `{AvgAllocKbPerFrame:F1} KB/frame` (`{AllocMbPerSec:F2} MB/s`)");
        sb.AppendLine($"- Render (Clyde): GL DrawCalls `{AvgGlDrawCalls:F1}` | Clyde Calls `{AvgClydeDrawCalls:F1}` | Batches `{AvgBatches:F1}` | Lights `{AvgLights:F1}` (Shadow `{AvgShadowLights:F1}`) | Occluders `{AvgOccluders:F1}`");
        sb.AppendLine($"- Scene: Entities `{Scene.TotalEntities}` | Sprites `{Scene.VisibleSprites}/{Scene.TotalSprites}` | PostShaders `{Scene.PostShaderSprites}` ents (`{Scene.PostShaderCount}` total) | Decals `{Scene.TotalDecals}` ({Scene.DecalChunks} chunks) | GasChunks `{Scene.GasChunks}` | Overlays `{Scene.ActiveOverlays}` (ScreenTex: `{Scene.ScreenTextureOverlays}`) | Res `{Scene.ScreenSize.X}x{Scene.ScreenSize.Y}`");
        sb.AppendLine($"- Config: ScaleRender=`{Config.ViewportScaleRender}`, LightScale=`{Config.LightResScale:0.###}`, SoftShadows=`{Config.LightSoftShadows}`, LightBlur=`{Config.LightBlur}`, MaxShadow=`{Config.MaxShadowLights}`, AO=`{Config.AmbientOcclusion}`, Particles=`{Config.ParticleQuality}`, Parallax=`{Config.ParallaxEnabled}`(Low=`{Config.ParallaxLowQuality}`), HRTF=`{Config.AudioHrtf}`, VSync=`{Config.VSync}`, MaxFPS=`{Config.MaxFps}`");
        sb.AppendLine();

        sb.AppendLine("### Pipeline Stages");
        sb.AppendLine("| Stage | Avg (ms) | Max (ms) | Alloc (KB/f) |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var stage in TempPerfProfilerSession.TrackedStageOrder)
        {
            if (KeyStages.TryGetValue(stage, out var s))
                sb.AppendLine($"| {s.Name} | {s.AvgMs:F3} | {s.MaxMs:F2} | {s.AvgAllocKb:F1} |");
        }

        sb.AppendLine();
        sb.AppendLine("### Top Systems / Leaf Scopes");
        sb.AppendLine("| System / Scope | Avg (ms) | Max (ms) | Alloc (KB/f) |");
        sb.AppendLine("|---|---:|---:|---:|");
        foreach (var leaf in TopLeaves.Take(12))
        {
            sb.AppendLine($"| {leaf.Name} | {leaf.AvgMs:F3} | {leaf.MaxMs:F2} | {leaf.AvgAllocKb:F1} |");
        }

        return sb.ToString();
    }

    public static string BuildComparisonMarkdown(TempPerfBenchmarkSnapshot before, TempPerfBenchmarkSnapshot after)
    {
        var sb = new StringBuilder(2048);
        sb.AppendLine($"## Perf Comparison: `{before.Label}` -> `{after.Label}`");
        sb.AppendLine($"- Frames: `{before.FrameCount}` ({before.DurationSeconds:F1}s) -> `{after.FrameCount}` ({after.DurationSeconds:F1}s)");
        sb.AppendLine();
        sb.AppendLine("| Metric | BEFORE | AFTER | Delta | % |");
        sb.AppendLine("|---|---:|---:|---:|---:|");

        AppendRow(sb, "Avg FPS", before.AvgFps, after.AvgFps, "F1", higherIsBetter: true);
        AppendRow(sb, "1% Low FPS", before.OnePercentLowFps, after.OnePercentLowFps, "F1", higherIsBetter: true);
        AppendRow(sb, "0.1% Low FPS", before.PointOnePercentLowFps, after.PointOnePercentLowFps, "F1", higherIsBetter: true);
        AppendRow(sb, "Avg Frame (ms)", before.AvgFrameMs, after.AvgFrameMs, "F2", higherIsBetter: false);
        AppendRow(sb, "P95 Frame (ms)", before.P95FrameMs, after.P95FrameMs, "F2", higherIsBetter: false);
        AppendRow(sb, "P99 Frame (ms)", before.P99FrameMs, after.P99FrameMs, "F2", higherIsBetter: false);
        AppendRow(sb, "Max Frame (ms)", before.MaxFrameMs, after.MaxFrameMs, "F2", higherIsBetter: false);
        AppendRow(sb, "Hitches >33ms", before.Hitches33Ms, after.Hitches33Ms, "F0", higherIsBetter: false);
        AppendRow(sb, "Alloc (KB/frame)", before.AvgAllocKbPerFrame, after.AvgAllocKbPerFrame, "F1", higherIsBetter: false);
        AppendRow(sb, "GL Draw Calls", before.AvgGlDrawCalls, after.AvgGlDrawCalls, "F1", higherIsBetter: false);
        AppendRow(sb, "Clyde Draw Calls", before.AvgClydeDrawCalls, after.AvgClydeDrawCalls, "F1", higherIsBetter: false);
        AppendRow(sb, "Batches", before.AvgBatches, after.AvgBatches, "F1", higherIsBetter: false);
        AppendRow(sb, "Lights", before.AvgLights, after.AvgLights, "F1", higherIsBetter: false);
        AppendRow(sb, "Shadow Lights", before.AvgShadowLights, after.AvgShadowLights, "F1", higherIsBetter: false);
        AppendRow(sb, "PostShader Sprites", before.Scene.PostShaderSprites, after.Scene.PostShaderSprites, "F0", higherIsBetter: false);

        sb.AppendLine();
        sb.AppendLine("### Stage Breakdown (Avg ms)");
        sb.AppendLine("| Stage | BEFORE (ms) | AFTER (ms) | Delta (ms) | % |");
        sb.AppendLine("|---|---:|---:|---:|---:|");

        foreach (var stage in TempPerfProfilerSession.TrackedStageOrder)
        {
            var hasB = before.KeyStages.TryGetValue(stage, out var b);
            var hasA = after.KeyStages.TryGetValue(stage, out var a);
            if (!hasB && !hasA)
                continue;

            AppendRow(sb, stage, hasB ? b.AvgMs : 0, hasA ? a.AvgMs : 0, "F3", higherIsBetter: false);
        }

        return sb.ToString();
    }

    private static void AppendRow(StringBuilder sb, string name, double before, double after, string fmt, bool higherIsBetter)
    {
        var delta = after - before;
        var pct = Math.Abs(before) > 0.0001 ? (delta / before) * 100.0 : 0.0;
        var sign = delta >= 0 ? "+" : "";
        var pctSign = pct >= 0 ? "+" : "";
        var marker = Math.Abs(pct) < 1.5
            ? ""
            : (higherIsBetter ? (delta > 0 ? " OK" : " WARN") : (delta < 0 ? " OK" : " WARN"));

        sb.Append("| ");
        sb.Append(name);
        sb.Append(" | ");
        sb.Append(before.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" | ");
        sb.Append(after.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" | ");
        sb.Append(sign);
        sb.Append(delta.ToString(fmt, CultureInfo.InvariantCulture));
        sb.Append(" | ");
        sb.Append(pctSign);
        sb.Append(pct.ToString("F1", CultureInfo.InvariantCulture));
        sb.Append('%');
        sb.Append(marker);
        sb.AppendLine(" |");
    }
}

/// <summary>
/// TEMPORARY zero-clone incremental reader for <see cref="ProfManager.Buffer"/>.
/// Tracks frame times, allocations, Clyde stats, pipeline stages, and top heavy systems/groups.
/// </summary>
internal sealed class TempPerfProfilerSession
{
    public static readonly string[] TrackedStageOrder =
    [
        "Clyde",
        "Viewport",
        "Lights",
        "LightsToRender",
        "Draw shadow depth",
        "Draw Lights",
        "BlurOntoWalls",
        "MergeWallLayer",
        "DrawFov",
        "UpdateOcclusionGeometry",
        "Overlays WSBW",
        "Grids",
        "Entities",
        "Gather Sprites",
        "Draw",
        "Overlays WSBFOV",
        "Overlays WS",
        "Overlays SS ScreenSpaceBelowWorld",
        "Overlays SS ScreenSpace",
        "UI",
        "Swap buffers",
        "Entity",
        "Systems",
        "Overlay",
        "Network",
        "Game state",
        "Prediction",
        "Events",
        "QueueDel",
    ];

    private static readonly HashSet<string> TrackedStageSet = new(TrackedStageOrder, StringComparer.Ordinal);

    private readonly ProfManager _prof;
    private readonly IEntityManager _entMan;
    private readonly IOverlayManager _overlayMan;
    private readonly IConfigurationManager _cfg;
    private readonly IClyde _clyde;

    private long _lastProcessedIndex = -1;

    // Rolling graph buffer (last 240 frames)
    public const int GraphCapacity = 240;
    private readonly float[] _graphMs = new float[GraphCapacity];
    private int _graphWritePos;
    private int _graphCount;

    // Session accumulators
    private readonly List<float> _frameTimesMs = new(1024);
    private double _totalFrameMs;
    private double _minFrameMs = double.MaxValue;
    private double _maxFrameMs;
    private int _hitches33;
    private int _hitches50;
    private long _totalAllocBytes;

    // Clyde integer stats accumulated per frame
    private long _sumGlDrawCalls;
    private long _sumClydeDrawCalls;
    private long _sumBatches;
    private long _sumMaxBatchVerts;
    private long _sumLights;
    private long _sumShadowLights;
    private long _sumOccluders;
    private int _clydeStatsFrames;

    // Scopes accumulated across frames
    private readonly Dictionary<string, TempPerfScopeAccumulator> _groups = new(128, StringComparer.Ordinal);
    private readonly Dictionary<string, TempPerfScopeAccumulator> _leaves = new(256, StringComparer.Ordinal);

    // Scratch per-frame maps (reused to avoid GC overhead)
    private readonly Dictionary<string, (double ms, long alloc)> _frameGroupScratch = new(64, StringComparer.Ordinal);
    private readonly Dictionary<string, (double ms, long alloc)> _frameLeafScratch = new(128, StringComparer.Ordinal);

    public int FrameCount => _frameTimesMs.Count;
    public double ElapsedSeconds => _totalFrameMs / 1000.0;

    public TempPerfProfilerSession(
        ProfManager prof,
        IEntityManager entMan,
        IOverlayManager overlayMan,
        IConfigurationManager cfg,
        IClyde clyde)
    {
        _prof = prof;
        _entMan = entMan;
        _overlayMan = overlayMan;
        _cfg = cfg;
        _clyde = clyde;
    }

    public void Reset(bool clearGraph = false)
    {
        _frameTimesMs.Clear();
        _totalFrameMs = 0;
        _minFrameMs = double.MaxValue;
        _maxFrameMs = 0;
        _hitches33 = 0;
        _hitches50 = 0;
        _totalAllocBytes = 0;

        _sumGlDrawCalls = 0;
        _sumClydeDrawCalls = 0;
        _sumBatches = 0;
        _sumMaxBatchVerts = 0;
        _sumLights = 0;
        _sumShadowLights = 0;
        _sumOccluders = 0;
        _clydeStatsFrames = 0;

        _groups.Clear();
        _leaves.Clear();

        if (clearGraph)
        {
            Array.Clear(_graphMs);
            _graphWritePos = 0;
            _graphCount = 0;
        }

        // Start from the latest completed index so stale frames aren't mixed into a fresh run.
        ref var buf = ref _prof.Buffer;
        _lastProcessedIndex = buf.IndexWriteOffset - 1;
    }

    /// <summary>
    /// Reads newly completed frames from <see cref="ProfManager.Buffer"/> without cloning the buffer.
    /// </summary>
    public void PollFrames(bool recordIntoSession)
    {
        if (!_prof.IsEnabled)
            return;

        ref var buf = ref _prof.Buffer;
        if (buf.IndexBuffer == null || buf.LogBuffer == null || buf.IndexWriteOffset <= 0)
            return;

        var newestIndex = buf.IndexWriteOffset - 1;
        if (_lastProcessedIndex < 0)
        {
            _lastProcessedIndex = newestIndex;
            return;
        }

        if (newestIndex <= _lastProcessedIndex)
            return;

        var minValidIndex = Math.Max(0, buf.IndexWriteOffset - buf.IndexBuffer.LongLength);
        var minValidLog = buf.LogWriteOffset - buf.LogBuffer.LongLength;

        var startIndex = Math.Max(_lastProcessedIndex + 1, minValidIndex);
        // Never spend too long catching up if stalled.
        var maxToProcess = Math.Max(startIndex, newestIndex - 64);
        if (startIndex < maxToProcess)
            startIndex = maxToProcess;

        for (var idx = startIndex; idx <= newestIndex; idx++)
        {
            ref var index = ref buf.Index(idx);
            if (index.Type != ProfIndexType.Frame)
                continue;

            if (index.StartPos < minValidLog || index.EndPos <= index.StartPos || index.EndPos > buf.LogWriteOffset)
                continue;

            ProcessSingleFrame(in buf, in index, recordIntoSession);
        }

        _lastProcessedIndex = newestIndex;
    }

    private void ProcessSingleFrame(in ProfBuffer buf, in ProfIndex index, bool recordIntoSession)
    {
        ref var outerEnd = ref buf.Log(index.EndPos - 1);
        if (outerEnd.Type != ProfLogType.GroupEnd || outerEnd.GroupEnd.Value.Type != ProfValueType.TimeAllocSample)
            return;

        var frameSample = outerEnd.GroupEnd.Value.TimeAllocSample;
        var frameMs = frameSample.Time * 1000.0;
        if (frameMs <= 0 || double.IsNaN(frameMs) || double.IsInfinity(frameMs))
            return;

        // Always update live graph
        _graphMs[_graphWritePos] = (float) frameMs;
        _graphWritePos = (_graphWritePos + 1) % GraphCapacity;
        if (_graphCount < GraphCapacity)
            _graphCount++;

        if (!recordIntoSession)
            return;

        _frameTimesMs.Add((float) frameMs);
        _totalFrameMs += frameMs;
        if (frameMs < _minFrameMs) _minFrameMs = frameMs;
        if (frameMs > _maxFrameMs) _maxFrameMs = frameMs;
        if (frameMs > 33.333) _hitches33++;
        if (frameMs > 50.0) _hitches50++;
        _totalAllocBytes += Math.Max(0, frameSample.Alloc);

        _frameGroupScratch.Clear();
        _frameLeafScratch.Clear();

        var sawClydeStats = false;
        var endExclusive = index.EndPos - 1; // exclude outer frame wrapper itself

        for (var pos = index.StartPos; pos < endExclusive; pos++)
        {
            ref var log = ref buf.Log(pos);
            if (log.Type == ProfLogType.GroupEnd)
            {
                if (log.GroupEnd.Value.Type != ProfValueType.TimeAllocSample)
                    continue;

                var name = _prof.GetString(log.GroupEnd.StringId);
                var s = log.GroupEnd.Value.TimeAllocSample;
                var ms = s.Time * 1000.0;
                var alloc = Math.Max(0, s.Alloc);

                if (_frameGroupScratch.TryGetValue(name, out var prev))
                    _frameGroupScratch[name] = (prev.ms + ms, prev.alloc + alloc);
                else
                    _frameGroupScratch[name] = (ms, alloc);
            }
            else if (log.Type == ProfLogType.Value)
            {
                var name = _prof.GetString(log.Value.StringId);
                if (log.Value.Value.Type == ProfValueType.TimeAllocSample)
                {
                    var s = log.Value.Value.TimeAllocSample;
                    var ms = s.Time * 1000.0;
                    var alloc = Math.Max(0, s.Alloc);

                    if (_frameLeafScratch.TryGetValue(name, out var prev))
                        _frameLeafScratch[name] = (prev.ms + ms, prev.alloc + alloc);
                    else
                        _frameLeafScratch[name] = (ms, alloc);
                }
                else if (log.Value.Value.Type == ProfValueType.Int32)
                {
                    var v = log.Value.Value.Int32;
                    switch (name)
                    {
                        case "GL Draw Calls":
                            _sumGlDrawCalls += v;
                            sawClydeStats = true;
                            break;
                        case "Clyde Draw Calls":
                            _sumClydeDrawCalls += v;
                            break;
                        case "Batches":
                            _sumBatches += v;
                            break;
                        case "Max Batch Verts":
                            _sumMaxBatchVerts += v;
                            break;
                        case "Lights":
                            _sumLights += v;
                            break;
                        case "Shadow Lights":
                            _sumShadowLights += v;
                            break;
                        case "Occluders":
                            _sumOccluders += v;
                            break;
                    }
                }
            }
        }

        if (sawClydeStats)
            _clydeStatsFrames++;

        MergeScratch(_frameGroupScratch, _groups);
        MergeScratch(_frameLeafScratch, _leaves);
    }

    private static void MergeScratch(
        Dictionary<string, (double ms, long alloc)> scratch,
        Dictionary<string, TempPerfScopeAccumulator> target)
    {
        foreach (var (name, val) in scratch)
        {
            if (!target.TryGetValue(name, out var acc))
            {
                acc = new TempPerfScopeAccumulator();
                target[name] = acc;
            }

            acc.TotalMs += val.ms;
            if (val.ms > acc.MaxMs)
                acc.MaxMs = val.ms;
            acc.TotalAllocBytes += val.alloc;
            acc.Frames++;
        }
    }

    public int CopyGraphChronological(Span<float> destination)
    {
        var count = Math.Min(_graphCount, destination.Length);
        var start = (_graphWritePos - count + GraphCapacity) % GraphCapacity;
        for (var i = 0; i < count; i++)
        {
            destination[i] = _graphMs[(start + i) % GraphCapacity];
        }

        return count;
    }

    public TempPerfSceneStats CaptureSceneStats()
    {
        var totalEntities = _entMan.EntityCount;

        var totalSprites = 0;
        var visibleSprites = 0;
        var postShaderSprites = 0;
        var postShaderCount = 0;

        if (_entMan.TrySystem<SpriteSystem>(out var spriteSys))
        {
            var spriteQuery = _entMan.AllEntityQueryEnumerator<SpriteComponent>();
            while (spriteQuery.MoveNext(out _, out var sprite))
            {
                totalSprites++;
                if (sprite.Visible)
                    visibleSprites++;

                var postShaders = spriteSys.GetPostShaders(sprite);
                if (postShaders.Count > 0)
                {
                    postShaderSprites++;
                    postShaderCount += postShaders.Count;
                }
            }
        }

        var decalChunks = 0;
        var totalDecals = 0;
        var decalQuery = _entMan.AllEntityQueryEnumerator<DecalGridComponent>();
        while (decalQuery.MoveNext(out _, out var decalGrid))
        {
            var chunks = decalGrid.ChunkCollection.ChunkCollection;
            decalChunks += chunks.Count;
            foreach (var chunk in chunks.Values)
                totalDecals += chunk.Decals.Count;
        }

        var gasChunks = 0;
        var gasQuery = _entMan.AllEntityQueryEnumerator<GasTileOverlayComponent>();
        while (gasQuery.MoveNext(out _, out var gas))
        {
            gasChunks += gas.Chunks.Count;
        }

        var activeOverlays = 0;
        var screenTexOverlays = 0;
        foreach (var overlay in _overlayMan.AllOverlays)
        {
            activeOverlays++;
            if (overlay.RequestScreenTexture)
                screenTexOverlays++;
        }

        return new TempPerfSceneStats(
            totalEntities,
            totalSprites,
            visibleSprites,
            postShaderSprites,
            postShaderCount,
            decalChunks,
            totalDecals,
            gasChunks,
            activeOverlays,
            screenTexOverlays,
            _clyde.ScreenSize);
    }

    public TempPerfGraphicsConfig CaptureGraphicsConfig()
    {
        return new TempPerfGraphicsConfig(
            ViewportScaleRender: _cfg.GetCVar(CCVars.ViewportScaleRender),
            LightResScale: _cfg.GetCVar(CVars.LightResolutionScale),
            LightSoftShadows: _cfg.GetCVar(CVars.LightSoftShadows),
            LightBlur: _cfg.GetCVar(CVars.LightBlur),
            MaxShadowLights: _cfg.GetCVar(CVars.MaxShadowcastingLights),
            MaxLights: _cfg.GetCVar(CVars.MaxLightCount),
            MaxOccluders: _cfg.GetCVar(CVars.MaxOccluderCount),
            AmbientOcclusion: _cfg.GetCVar(CCVars.AmbientOcclusion),
            ParticleQuality: _cfg.GetCVar(GoobCVars.ParticleQuality),
            ParallaxEnabled: _cfg.GetCVar(CCVars.ParallaxEnabled),
            ParallaxLowQuality: _cfg.GetCVar(CCVars.ParallaxLowQuality),
            AudioHrtf: _cfg.GetCVar(CVars.AudioHrtf),
            VSync: _cfg.GetCVar(CVars.DisplayVSync),
            MaxFps: _cfg.GetCVar(CVars.DisplayMaxFPS));
    }

    public TempPerfBenchmarkSnapshot BuildSnapshot(string label)
    {
        var count = _frameTimesMs.Count;
        var snap = new TempPerfBenchmarkSnapshot
        {
            Label = string.IsNullOrWhiteSpace(label) ? "Snapshot" : label.Trim(),
            CapturedAtUtc = DateTime.UtcNow,
            DurationSeconds = _totalFrameMs / 1000.0,
            FrameCount = count,
            Scene = CaptureSceneStats(),
            Config = CaptureGraphicsConfig(),
        };

        if (count == 0)
            return snap;

        var sorted = _frameTimesMs.ToArray();
        Array.Sort(sorted);

        var avgMs = _totalFrameMs / count;
        snap.AvgFrameMs = avgMs;
        snap.MinFrameMs = _minFrameMs == double.MaxValue ? 0 : _minFrameMs;
        snap.MaxFrameMs = _maxFrameMs;
        snap.P95FrameMs = Percentile(sorted, 0.95);
        snap.P99FrameMs = Percentile(sorted, 0.99);
        snap.Hitches33Ms = _hitches33;
        snap.Hitches50Ms = _hitches50;

        snap.AvgFps = avgMs > 0.0001 ? 1000.0 / avgMs : 0;
        snap.OnePercentLowFps = ComputeLowPercentFps(sorted, 0.01);
        snap.PointOnePercentLowFps = ComputeLowPercentFps(sorted, 0.001);

        snap.AvgAllocKbPerFrame = _totalAllocBytes / 1024.0 / count;
        snap.AllocMbPerSec = snap.DurationSeconds > 0.001
            ? (_totalAllocBytes / (1024.0 * 1024.0)) / snap.DurationSeconds
            : 0;

        if (_clydeStatsFrames > 0)
        {
            var d = (double) _clydeStatsFrames;
            snap.AvgGlDrawCalls = _sumGlDrawCalls / d;
            snap.AvgClydeDrawCalls = _sumClydeDrawCalls / d;
            snap.AvgBatches = _sumBatches / d;
            snap.AvgMaxBatchVerts = _sumMaxBatchVerts / d;
            snap.AvgLights = _sumLights / d;
            snap.AvgShadowLights = _sumShadowLights / d;
            snap.AvgOccluders = _sumOccluders / d;
        }

        foreach (var stage in TrackedStageOrder)
        {
            if (_groups.TryGetValue(stage, out var acc))
            {
                snap.KeyStages[stage] = new TempPerfScopeSummary(
                    stage,
                    acc.AvgMs(count),
                    acc.MaxMs,
                    acc.AvgAllocKb(count));
            }
        }

        snap.TopGroups = _groups
            .Where(kv => !TrackedStageSet.Contains(kv.Key))
            .Select(kv => new TempPerfScopeSummary(kv.Key, kv.Value.AvgMs(count), kv.Value.MaxMs, kv.Value.AvgAllocKb(count)))
            .OrderByDescending(s => s.AvgMs)
            .Take(15)
            .ToList();

        snap.TopLeaves = _leaves
            .Select(kv => new TempPerfScopeSummary(kv.Key, kv.Value.AvgMs(count), kv.Value.MaxMs, kv.Value.AvgAllocKb(count)))
            .OrderByDescending(s => s.AvgMs)
            .Take(15)
            .ToList();

        return snap;
    }

    private static double Percentile(float[] sortedAsc, double p)
    {
        if (sortedAsc.Length == 0)
            return 0;

        var idx = (int) Math.Clamp(Math.Ceiling(sortedAsc.Length * p) - 1, 0, sortedAsc.Length - 1);
        return sortedAsc[idx];
    }

    private static double ComputeLowPercentFps(float[] sortedAsc, double worstFraction)
    {
        if (sortedAsc.Length == 0)
            return 0;

        var worstCount = Math.Max(1, (int) Math.Ceiling(sortedAsc.Length * worstFraction));
        var start = sortedAsc.Length - worstCount;
        double sum = 0;
        for (var i = start; i < sortedAsc.Length; i++)
            sum += sortedAsc[i];

        var worstAvgMs = sum / worstCount;
        return worstAvgMs > 0.0001 ? 1000.0 / worstAvgMs : 0;
    }
}

/// <summary>
/// TEMPORARY live frame-time bar graph (green <= 16.7ms, yellow <= 33.3ms, red > 33.3ms).
/// </summary>
internal sealed class TempPerfFrameGraphControl : Control
{
    private readonly TempPerfProfilerSession _session;
    private readonly float[] _scratch = new float[TempPerfProfilerSession.GraphCapacity];

    private const float MaxDisplayMs = 50f;

    public TempPerfFrameGraphControl(TempPerfProfilerSession session)
    {
        _session = session;
        MinHeight = 84;
        HorizontalExpand = true;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        var w = PixelWidth;
        var h = PixelHeight;
        if (w <= 2 || h <= 2)
            return;

        // Background
        handle.DrawRect(new UIBox2(0, 0, w, h), new Color(18, 20, 26, 235));

        // 60 FPS (16.67ms) & 30 FPS (33.33ms) horizontal guide lines
        var y60 = h - (16.667f / MaxDisplayMs) * h;
        var y30 = h - (33.333f / MaxDisplayMs) * h;
        handle.DrawLine(new Vector2(0, y60), new Vector2(w, y60), new Color(70, 180, 90, 120));
        handle.DrawLine(new Vector2(0, y30), new Vector2(w, y30), new Color(220, 180, 60, 120));

        var count = _session.CopyGraphChronological(_scratch);
        if (count <= 0)
            return;

        var barW = (float) w / TempPerfProfilerSession.GraphCapacity;
        var xStart = w - count * barW;

        for (var i = 0; i < count; i++)
        {
            var ms = _scratch[i];
            var clamped = Math.Clamp(ms, 0f, MaxDisplayMs);
            var barH = MathF.Max(1f, (clamped / MaxDisplayMs) * h);
            var x = xStart + i * barW;

            var color = ms <= 16.667f
                ? new Color(80, 215, 110, 220)
                : ms <= 33.333f
                    ? new Color(240, 200, 65, 230)
                    : new Color(235, 75, 75, 240);

            handle.DrawRect(new UIBox2(x, h - barH, x + MathF.Max(1f, barW - 0.5f), h), color);
        }

        // Border
        handle.DrawRect(new UIBox2(0, 0, w, h), new Color(90, 100, 120, 180), filled: false);
    }
}
