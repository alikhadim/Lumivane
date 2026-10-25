using System.Collections.ObjectModel;
using System.Text.Json;

namespace Lumivane.Core;

public enum SetupStep { Welcome, Bridge, Pair, Lights, Area, Permission, Done }
public enum SourceKind { Display, Window, Desktop }
public enum Appearance { System, Light, Dark }
public enum NavSection { Home, Screens, Sound, Lights, Profiles, Settings }

public sealed record SourceItem(string Id, SourceKind Kind, DisplayGeometry? Display, WindowSource? Window, bool Connected, CaptureTarget? Target);

/// <summary>
/// Application state and actions, independent of the UI toolkit. Change notifications are raised on the thread given by <c>post</c>.
/// Profile edits go through <see cref="EditProfile"/> / <see cref="EditSettings"/> so autosave, engine updates and re-planning happen in one place.
/// </summary>
public sealed class AppModel : ObservableObject
{
    readonly IPlatform platform;
    readonly Store store;
    readonly Func<BridgeInfo, Credentials?, IHueBridge> makeBridge;
    readonly Action<Action> post;
    public SyncEngine Engine { get; }
    public LaunchOptions Launch { get; }
    IHueBridge? client;
    CancellationTokenSource? pingCts, replanCts;

    // setup
    SetupStep step; BridgeInfo? bridge; Credentials? credentials;
    bool onboarded, searching, pairing, busy, demo, syncing;
    string? error, notice;
    public ObservableCollection<BridgeInfo> Bridges { get; } = new();

    // Hue data
    IReadOnlyList<HueLight> lights = Array.Empty<HueLight>();
    IReadOnlyList<HueGroup> groups = Array.Empty<HueGroup>();
    IReadOnlyList<HueScene> scenes = Array.Empty<HueScene>();
    IReadOnlyList<EntertainmentArea> areas = Array.Empty<EntertainmentArea>();
    Diagnostics? diagnostics;

    // profiles
    List<Profile> profiles = new();
    Profile profile = new();

    // sources
    IReadOnlyList<DisplayGeometry> displays = Array.Empty<DisplayGeometry>();
    IReadOnlyList<LiveDisplay> liveDisplays = Array.Empty<LiveDisplay>();
    IReadOnlyList<LiveWindow> windows = Array.Empty<LiveWindow>();
    IReadOnlyList<AudioApp> audioApps = Array.Empty<AudioApp>();
    Dictionary<CaptureTarget, Frame> thumbnails = new();
    Dictionary<string, DisplayGeometry> knownDisplays;
    List<Substitution> activeSubstitutions = new();

    // sync
    Metrics metrics = new();
    Dictionary<int, RGB> preview = new();
    Dictionary<ColorStyle, Dictionary<int, RGB>> styleCompare = new();
    double? networkMs;
    Appearance appearance;

    public AppModel(IPlatform platform, Func<BridgeInfo, Credentials?, IHueBridge>? bridgeFactory = null, Action<Action>? post = null, LaunchOptions? launch = null, Store? store = null)
    {
        this.platform = platform;
        this.store = store ?? new Store(platform.DataDir);
        makeBridge = bridgeFactory ?? ((b, c) => new HueClient(b, c));
        this.post = post ?? (a => a());
        Launch = launch ?? new LaunchOptions();
        Engine = new SyncEngine(platform);

        step = Enum.IsDefined((SetupStep)this.store.GetInt("setupStep")) ? (SetupStep)this.store.GetInt("setupStep") : SetupStep.Welcome;
        onboarded = this.store.GetBool("onboarded");
        bridge = this.store.Get<BridgeInfo>("bridge");
        credentials = LoadCredentials();
        knownDisplays = this.store.Get<Dictionary<string, DisplayGeometry>>("knownDisplays") ?? new();
        appearance = Enum.TryParse<Appearance>(this.store.Get<string>("appearance"), out var ap) ? ap : Appearance.System;

        var stored = this.store.Get<List<Profile>>("profiles") is { Count: > 0 } sp ? sp : Profile.Presets();
        if (this.store.GetInt("presetsVersion") < 2)
        {   // built-in profiles pick up the new color styles once
            var fresh = Profile.Presets().ToDictionary(p => p.Name, p => p.Settings);
            foreach (var p in stored) if (fresh.TryGetValue(p.Name, out var f)) { p.Settings.Style = f.Style; p.Settings.Intensity = f.Intensity; p.Settings.Gamma = f.Gamma; }
            this.store.Set("presetsVersion", 2);
        }
        var id = this.store.Get<Guid?>("activeProfile");
        profiles = stored;
        profile = stored.FirstOrDefault(p => p.Id == id) ?? stored[0];

        Engine.OnUpdate = (m, p) => post_(() => { Metrics = m; Preview = p; MetricsLogWrite(m); });
        Engine.OnCompare = c => post_(() => StyleCompare = c);
        Engine.OnError = msg => post_(() => Notice = msg);
        Engine.OnSourceLost = _ => post_(() => ScheduleReplan(0.5));
        platform.DisplaysChanged += () => post_(() => ScheduleReplan(0.8));
        Engine.Update(profile.Settings);
        RefreshDisplays();
        if (Launch.Demo) ApplyDemo(true, remember: false);
        else if (this.store.GetBool("demoMode")) ApplyDemo(true);
    }

    void post_(Action a) => post(a);

    Credentials? LoadCredentials()
    {
        var s = platform.Secrets.Load("creds");
        if (s == null) return null;
        try { return Json.Read<Credentials>(s); } catch (JsonException) { return null; }
    }

    // ---- observable state ----

    public SetupStep Step { get => step; set { if (Set(ref step, value)) store.Set("setupStep", (int)value); } }
    public bool Onboarded { get => onboarded; private set => Set(ref onboarded, value); }
    public BridgeInfo? Bridge { get => bridge; private set => Set(ref bridge, value); }
    public Credentials? Credentials { get => credentials; private set => Set(ref credentials, value); }
    public bool Searching { get => searching; private set => Set(ref searching, value); }
    public bool Pairing { get => pairing; private set => Set(ref pairing, value); }
    public bool Busy { get => busy; private set => Set(ref busy, value); }
    public string? Error { get => error; set => Set(ref error, value); }
    public string? Notice { get => notice; set => Set(ref notice, value); }
    public bool Demo { get => demo; private set => Set(ref demo, value); }
    public bool Syncing { get => syncing; private set => Set(ref syncing, value); }
    public IReadOnlyList<HueLight> Lights { get => lights; private set => Set(ref lights, value); }
    public IReadOnlyList<HueGroup> Groups { get => groups; private set => Set(ref groups, value); }
    public IReadOnlyList<HueScene> Scenes { get => scenes; private set => Set(ref scenes, value); }
    public IReadOnlyList<EntertainmentArea> Areas { get => areas; private set { if (Set(ref areas, value)) Raise(nameof(SelectedArea)); } }
    public Diagnostics? Diagnostics { get => diagnostics; private set => Set(ref diagnostics, value); }
    public IReadOnlyList<Profile> Profiles => profiles;
    public Profile Profile { get => profile; private set { profile = value; Raise(); Raise(nameof(Settings)); Raise(nameof(SelectedArea)); Raise(nameof(SourceItems)); Raise(nameof(EffectivePrimary)); } }
    public SyncSettings Settings => profile.Settings;
    public IReadOnlyList<DisplayGeometry> Displays { get => displays; private set { if (Set(ref displays, value)) { Raise(nameof(SourceItems)); Raise(nameof(EffectivePrimary)); } } }
    public IReadOnlyList<LiveDisplay> LiveDisplays { get => liveDisplays; private set => Set(ref liveDisplays, value); }
    public IReadOnlyList<LiveWindow> Windows { get => windows; private set { if (Set(ref windows, value)) Raise(nameof(SourceItems)); } }
    public IReadOnlyList<AudioApp> AudioApps { get => audioApps; private set => Set(ref audioApps, value); }
    public IReadOnlyDictionary<CaptureTarget, Frame> Thumbnails { get => thumbnails; private set { thumbnails = new(value); Raise(); } }
    public Metrics Metrics { get => metrics; private set => Set(ref metrics, value); }
    public IReadOnlyDictionary<int, RGB> Preview { get => preview; private set { preview = new(value); Raise(); } }
    public IReadOnlyDictionary<ColorStyle, Dictionary<int, RGB>> StyleCompare { get => styleCompare; private set { styleCompare = new(value); Raise(); } }
    public double? NetworkMs { get => networkMs; private set => Set(ref networkMs, value); }
    public Appearance Appearance { get => appearance; set { if (Set(ref appearance, value)) store.Set("appearance", value.ToString()); } }
    public IPlatform Platform => platform;

    public bool HasSetup => bridge != null && credentials != null;
    public EntertainmentArea? SelectedArea => areas.FirstOrDefault(a => a.Id == profile.AreaId) ?? areas.FirstOrDefault();
    public bool ScreenAllowed => demo || platform.ScreenAllowed;
    public bool LaunchAtLogin
    {
        get => platform.LaunchAtLogin;
        set { try { platform.LaunchAtLogin = value; } catch (Exception e) { Error = e.Message; } Raise(); }
    }

    // ---- demo mode ----

    public async Task SetDemoAsync(bool on)
    {
        if (on == demo) return;
        if (syncing) await StopAsync();
        ApplyDemo(on);
        if (!on) await BootAsync();
    }

    void ApplyDemo(bool on, bool remember = true)
    {
        Demo = on;
        if (remember) store.Set("demoMode", on);
        store.Persist = !on;
        Error = null; Notice = null; Thumbnails = new Dictionary<CaptureTarget, Frame>(); Windows = Array.Empty<LiveWindow>(); AudioApps = Array.Empty<AudioApp>(); StyleCompare = new Dictionary<ColorStyle, Dictionary<int, RGB>>(); Preview = new Dictionary<int, RGB>();
        client = null;
        if (on)
        {
            Bridge = DemoData.Bridge; Credentials = DemoData.Credentials; Onboarded = true;
            Lights = DemoData.Lights; Groups = DemoData.Groups(); Scenes = DemoData.Scenes(); Areas = DemoData.Areas();
            Diagnostics = DemoData.Diagnostics(Areas);
            profiles = DemoData.Profiles(); Profile = profiles[0];
            AudioApps = DemoData.AudioApps; Windows = DemoData.Windows;
        }
        else
        {
            store.Persist = true;
            Bridge = store.Get<BridgeInfo>("bridge"); Credentials = LoadCredentials(); Onboarded = store.GetBool("onboarded");
            Lights = Array.Empty<HueLight>(); Groups = Array.Empty<HueGroup>(); Scenes = Array.Empty<HueScene>(); Areas = Array.Empty<EntertainmentArea>(); Diagnostics = null;
            var stored = store.Get<List<Profile>>("profiles") is { Count: > 0 } sp ? sp : Profile.Presets();
            var id = store.Get<Guid?>("activeProfile");
            profiles = stored; Profile = stored.FirstOrDefault(p => p.Id == id) ?? stored[0];
        }
        Raise(nameof(Profiles));
        RefreshDisplays();
    }

    public void SetCompare(bool on) { Engine.CompareEnabled = on; if (!on) StyleCompare = new Dictionary<ColorStyle, Dictionary<int, RGB>>(); }

    // ---- boot ----

    bool booted;
    public async Task BootOnceAsync() { if (booted) return; booted = true; await BootAsync(); }

    public async Task BootAsync()
    {
        if (demo) { if (Launch.AutoSync && !syncing) await StartAsync(); return; }
        if (onboarded && !ScreenAllowed) { RequestScreenAccess(); }
        if (!HasSetup || bridge == null) return;
        client = makeBridge(bridge, credentials);
        await RefreshAsync();
        RefreshDisplays();
        if (Launch.AutoSync && !syncing) await StartAsync();
    }

    // ---- profiles ----

    void SaveProfiles() { store.Set("profiles", profiles); store.Set("activeProfile", profile.Id); }

    /// <summary>Applies an edit to the active profile, then autosaves, updates the engine and re-plans.</summary>
    public void EditProfile(Action<Profile> edit)
    {
        var old = profile.Clone();
        edit(profile);
        ProfileChanged(old);
        Raise(nameof(Profile)); Raise(nameof(Settings)); Raise(nameof(SourceItems)); Raise(nameof(EffectivePrimary)); Raise(nameof(SelectedArea));
    }

    public void EditSettings(Action<SyncSettings> edit) => EditProfile(p => edit(p.Settings));

    void ProfileChanged(Profile old)
    {
        int i = profiles.FindIndex(p => p.Id == profile.Id);
        if (i >= 0) profiles[i] = profile; else profiles.Add(profile);
        SaveProfiles();
        Engine.Update(profile.Settings);
        if (!syncing) return;
        if (old.AreaId != profile.AreaId) _ = Task.Run(async () => { await StopAsync(); await StartAsync(); });
        else ScheduleReplan(0.25);
    }

    public void Load(Profile p)
    {
        p = p.Clone();
        if ((areas.FirstOrDefault(a => a.Id == p.AreaId) ?? SelectedArea) is { } a) { p.AreaId = a.Id; p.EnsureRoutes(a.Channels); }
        var old = profile.Clone();
        Profile = p; Notice = null;
        int i = profiles.FindIndex(x => x.Id == p.Id); if (i >= 0) profiles[i] = p;
        SaveProfiles(); Engine.Update(p.Settings);
        if (syncing) { if (old.AreaId != p.AreaId) _ = Task.Run(async () => { await StopAsync(); await StartAsync(); }); else ScheduleReplan(0.25); }
    }

    public void SaveCurrentAsNew(string name)
    {
        var p = profile.Clone(); p.Id = Guid.NewGuid(); p.Name = string.IsNullOrWhiteSpace(name) ? "My profile" : name;
        profiles.Add(p); Profile = p; SaveProfiles(); Raise(nameof(Profiles));
    }

    public void Delete(Profile p)
    {
        if (profiles.Count <= 1) return;
        profiles.RemoveAll(x => x.Id == p.Id);
        if (profile.Id == p.Id) { Profile = profiles[0]; Engine.Update(profile.Settings); }
        SaveProfiles(); Raise(nameof(Profiles));
    }

    public void Rename(Profile p, string name)
    {
        if (p.Id == profile.Id) EditProfile(x => x.Name = name);
        else if (profiles.FirstOrDefault(x => x.Id == p.Id) is { } t) { t.Name = name; SaveProfiles(); }
        Raise(nameof(Profiles));
    }

    // ---- displays, windows, sources ----

    public void RefreshDisplays()
    {
        if (demo) { LiveDisplays = DemoData.LiveDisplays; Displays = LiveDisplays.Select(d => d.Geometry).ToList(); return; }
        var live = platform.Displays().ToList();
        LiveDisplays = live;
        foreach (var d in live) knownDisplays[d.Geometry.Key] = d.Geometry;
        store.Set("knownDisplays", knownDisplays);
        var liveKeys = live.Select(d => d.Geometry.Key).ToHashSet();
        var remembered = knownDisplays.Values.Where(d => !liveKeys.Contains(d.Key)).Select(d => { var c = Json.Clone(d); c.IsConnected = false; return c; }).OrderBy(d => d.Name, StringComparer.Ordinal);
        Displays = live.Select(d => d.Geometry).Concat(remembered).ToList();
    }

    public async Task RefreshWindowsAsync() => Windows = demo ? DemoData.Windows : await platform.WindowsAsync();
    public async Task RefreshAudioAppsAsync() => AudioApps = demo ? DemoData.AudioApps : await platform.AudioAppsAsync();

    public IReadOnlyList<SourceItem> SourceItems
    {
        get
        {
            var items = new List<SourceItem>();
            foreach (var d in displays)
                items.Add(new SourceItem(d.Id, SourceKind.Display, d, null, d.IsConnected,
                    liveDisplays.FirstOrDefault(l => l.Geometry.Key == d.Key) is { } ld ? CaptureTarget.Display(ld.NativeId) : null));
            if (displays.Any(d => d.IsConnected)) items.Add(new SourceItem(SourceID.Desktop, SourceKind.Desktop, null, null, true, null));
            foreach (var w in profile.WindowSources)
            {
                var live = windows.FirstOrDefault(x => x.Id == w.Id);
                items.Add(new SourceItem(w.Id, SourceKind.Window, null, w, live != null, live != null ? CaptureTarget.Window(live.NativeId) : null));
            }
            return items;
        }
    }

    public string SourceName(string id)
    {
        if (id == SourceID.Desktop) return "Entire desktop";
        if (SourceID.DisplayKey(id) is { } k) return displays.FirstOrDefault(d => d.Key == k)?.Name ?? "Display";
        return profile.WindowSources.FirstOrDefault(w => w.Id == id) is { } w ? $"{w.AppName}: {w.Title}" : "Window";
    }

    public bool IsAvailable(string id) => SourceItems.FirstOrDefault(s => s.Id == id)?.Connected ?? false;

    public async Task RefreshThumbnailsAsync()
    {
        if (demo)
        {
            float t = (float)(DateTime.Now.TimeOfDay.TotalSeconds % 3600);
            var d = new Dictionary<CaptureTarget, Frame>();
            foreach (var l in DemoData.LiveDisplays) d[CaptureTarget.Display(l.NativeId)] = DemoData.Thumbnail(DemoData.SceneFor(l.Geometry.Id), t, l.Geometry.Height / l.Geometry.Width);
            foreach (var w in DemoData.Windows) d[CaptureTarget.Window(w.NativeId)] = DemoData.Thumbnail(DemoData.SceneFor(w.Id), t, 9.0 / 16.0);
            Thumbnails = d; return;
        }
        var targets = SourceItems.Where(s => s.Target != null).Select(s => s.Target!.Value).ToList();
        var shots = await platform.ThumbnailsAsync(targets, 360);
        if (shots.Count > 0) { var merged = new Dictionary<CaptureTarget, Frame>(thumbnails); foreach (var kv in shots) merged[kv.Key] = kv.Value; Thumbnails = merged; }
    }

    public void AddWindow(LiveWindow w)
    {
        if (profile.WindowSources.Any(x => x.Id == w.Id)) return;
        EditProfile(p => p.WindowSources.Add(new WindowSource(w.Id, w.Title, w.AppName)));
    }

    public void RemoveWindow(string id) => EditProfile(p =>
    {
        p.WindowSources.RemoveAll(w => w.Id == id);
        p.MirrorSources.RemoveAll(s => s == id);
        if (p.PrimarySource == id) p.PrimarySource = null;
        foreach (var r in p.Routes) if (r.SourceId == id) r.SourceId = null;
    });

    // ---- assignments ----

    public void SetMode(SyncMode m) => EditProfile(p => p.Mode = m);
    public void SetPrimary(string id) => EditProfile(p => p.PrimarySource = id);
    public void ToggleMirror(string id) => EditProfile(p => { if (!p.MirrorSources.Remove(id)) p.MirrorSources.Add(id); });

    public bool IsSelected(string id) => profile.Mode switch
    {
        SyncMode.Single => EffectivePrimary == id,
        SyncMode.Mirror => profile.MirrorSources.Contains(id),
        _ => profile.Routes.Any(r => r.SourceId == id && r.Enabled),
    };

    /// <summary>What single mode will actually use right now (includes the fallback when nothing was chosen).</summary>
    public string? EffectivePrimary => profile.PrimarySource ?? (displays.FirstOrDefault(d => d.IsConnected && d.IsBuiltIn) ?? displays.FirstOrDefault(d => d.IsConnected))?.Id;

    public LightRoute? Route(int ch) => profile.Routes.FirstOrDefault(r => r.ChannelId == ch);
    public void UpdateRoute(int ch, Action<LightRoute> f) => EditProfile(p => { if (p.Routes.FirstOrDefault(r => r.ChannelId == ch) is { } r) f(r); });
    public void Assign(int ch, string? source) => UpdateRoute(ch, r => { r.SourceId = source; if (source != null) r.Enabled = true; });

    /// <summary>Drag-and-drop payloads: <c>ch:&lt;channel&gt;</c> for one light, <c>grp:&lt;id&gt;</c> for a Hue room or zone.</summary>
    public bool Drop(string payload, string source)
    {
        var chans = new List<int>();
        if (payload.StartsWith("ch:") && int.TryParse(payload[3..], out var c)) chans.Add(c);
        else if (payload.StartsWith("grp:") && groups.FirstOrDefault(g => g.Id == payload[4..]) is { } g && SelectedArea is { } area)
            chans.AddRange(area.Channels.Where(ch => lights.Any(l => g.LightIds.Contains(l.Id) && l.EntertainmentServiceId != null && ch.Services.Contains(l.EntertainmentServiceId))).Select(ch => ch.Id));
        if (chans.Count == 0) return false;
        bool switched = profile.Mode != SyncMode.Extend;
        EditProfile(p =>
        {
            p.Mode = SyncMode.Extend;
            foreach (var ch in chans) if (p.Routes.FirstOrDefault(r => r.ChannelId == ch) is { } r) { r.SourceId = source; r.Enabled = true; }
        });
        if (switched) Notice = "Switched to Extend mode so each light can follow its own display.";
        return true;
    }

    public HueLight? LightFor(EntertainmentChannel ch) => lights.FirstOrDefault(l => l.EntertainmentServiceId != null && ch.Services.Contains(l.EntertainmentServiceId));
    public string LightName(EntertainmentChannel ch) => LightFor(ch)?.Name ?? $"Light {ch.Id + 1}";

    // ---- planning and hot-plug ----

    (RoutePlan Plan, Dictionary<string, CaptureTarget> Targets, List<int> AudioOnly) BuildPlan()
    {
        if (SelectedArea is not { } area) return (new RoutePlan(), new(), new());
        var ids = area.Channels.Select(c => c.Id).ToList();
        var plan = RoutePlanner.Plan(profile, ids, displays, windows.Select(w => w.Id).ToHashSet());
        var targets = new Dictionary<string, CaptureTarget>();
        foreach (var src in plan.PhysicalSources)
        {
            if (SourceID.DisplayKey(src) is { } k && liveDisplays.FirstOrDefault(d => d.Geometry.Key == k) is { } d) targets[src] = CaptureTarget.Display(d.NativeId);
            else if (windows.FirstOrDefault(w => w.Id == src) is { } w) targets[src] = CaptureTarget.Window(w.NativeId);
        }
        var a = profile.Settings.Audio;
        var audioOnly = a.Enabled && a.Influence >= 0.99f ? profile.Routes.Where(r => r.Enabled && ids.Contains(r.ChannelId)).Select(r => r.ChannelId).ToList() : new List<int>();
        return (plan, targets, audioOnly);
    }

    public void ScheduleReplan(double delay)
    {
        replanCts?.Cancel();
        var cts = replanCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            try { await Task.Delay(TimeSpan.FromSeconds(delay), cts.Token); } catch (OperationCanceledException) { return; }
            if (cts.IsCancellationRequested) return;
            var done = new TaskCompletionSource();
            post_(async () => { try { await ReconfigureAsync(); } finally { done.TrySetResult(); } });
            await done.Task;
        });
    }

    public async Task ReconfigureAsync()
    {
        RefreshDisplays();
        if (profile.WindowSources.Count > 0) await RefreshWindowsAsync();
        var (plan, targets, audioOnly) = BuildPlan();
        Announce(plan);
        if (!syncing) return;
        await Engine.ApplyAsync(plan, targets, audioOnly);
        await Engine.ApplyAudioAsync(profile.Settings.Audio);
    }

    /// <summary>Tells the user when a display went away (and what took over) or came back. Mappings are never rewritten.</summary>
    void Announce(RoutePlan plan)
    {
        var now = plan.Substitutions.ToHashSet(); var before = activeSubstitutions.ToHashSet();
        foreach (var s in now.Except(before))
        {
            var msg = $"{SourceName(s.Requested)} is not available. Using {SourceName(s.Actual)} instead.";
            Notice = msg; platform.Notify("Display disconnected", msg);
        }
        foreach (var s in before.Except(now).Where(s => IsAvailable(s.Requested)))
        {
            var msg = $"{SourceName(s.Requested)} is back. Your lights follow it again.";
            Notice = msg; platform.Notify("Display reconnected", msg);
        }
        activeSubstitutions = plan.Substitutions.ToList();
    }

    // ---- discovery and pairing ----

    public async Task FindBridgesAsync(bool online = false)
    {
        Searching = true; Error = null;
        var found = await BridgeDiscovery.DiscoverAsync(online);
        Bridges.Clear(); foreach (var b in found) Bridges.Add(b);
        Searching = false;
        if (found.Count == 1) Select(found[0]);
    }

    public async Task AddManualAsync(string ip)
    {
        Searching = true; Error = null;
        if (await HueClient.ProbeAsync(ip.Trim()) is { } p) { Bridges.Clear(); Bridges.Add(p.Info); Select(p.Info); }
        else Error = "No Hue Bridge answered at that address.";
        Searching = false;
    }

    public void Select(BridgeInfo b)
    {
        Bridge = b; store.Set("bridge", b);
        client = makeBridge(b, null);
    }

    public async Task PairAsync(CancellationToken ct = default)
    {
        if (bridge == null) return;
        Pairing = true; Error = null;
        var c = new HueClient(bridge);
        try
        {
            var creds = await c.PairAsync(ct: ct);
            Credentials = creds;
            if (!demo) platform.Secrets.Save("creds", Json.Write(creds));
            client = makeBridge(bridge, creds);
            await RefreshAsync();
            Pairing = false;
            Step = SetupStep.Lights;
        }
        catch (OperationCanceledException) { Pairing = false; }
        catch (Exception e) { Pairing = false; Error = e.Message; }
        finally { c.Dispose(); }
    }

    public async Task ForgetBridgeAsync()
    {
        if (demo) { await SetDemoAsync(false); return; }
        if (syncing) await StopAsync();
        platform.Secrets.Delete("creds"); Credentials = null; Bridge = null; store.Set<BridgeInfo>("bridge", null);
        Lights = Array.Empty<HueLight>(); Groups = Array.Empty<HueGroup>(); Scenes = Array.Empty<HueScene>(); Areas = Array.Empty<EntertainmentArea>();
        Onboarded = false; Step = SetupStep.Welcome; store.Set("onboarded", false);
    }

    public void FinishOnboarding() { Onboarded = true; store.Set("onboarded", true); }

    // ---- Hue data ----

    public async Task RefreshAsync()
    {
        if (demo) return;
        var c = client; if (c == null) return;
        async Task<T?> Try<T>(Func<Task<T>> f) where T : class { try { return await f(); } catch (Exception) { return null; } }
        var lt = Try(() => c.LightsAsync()); var gt = Try(() => c.GroupsAsync()); var st = Try(() => c.ScenesAsync()); var at = Try(() => c.AreasAsync());
        var (ll, gg, ss, aa) = (await lt, await gt, await st, await at);
        if (ll != null) Lights = ll; if (gg != null) Groups = gg; if (ss != null) Scenes = ss; if (aa != null) Areas = aa;
        if (ll == null && gg == null) Error = HueException.Unreachable("no answer").Message;
        if (profile.AreaId == null || !areas.Any(a => a.Id == profile.AreaId))
            EditProfile(p => p.AreaId = (areas.FirstOrDefault(a => a.Type == "screen") ?? areas.FirstOrDefault())?.Id);
        if (SelectedArea is { } area)
        {
            var before = Json.Write(profile.Routes);
            var copy = profile.Clone(); copy.EnsureRoutes(area.Channels);
            if (Json.Write(copy.Routes) != before) EditProfile(p => p.EnsureRoutes(area.Channels));
        }
        Diagnostics = await c.DiagnosticsAsync();
    }

    public void RequestScreenAccess() { if (platform.ScreenAccessHint is { } h) Notice = h; }

    // ---- sync ----

    public async Task ToggleSyncAsync() { if (syncing) await StopAsync(); else await StartAsync(); }

    public async Task StartAsync()
    {
        Error = null; Notice = null;
        if (bridge == null || credentials == null) { Error = "Set up your bridge first."; return; }
        if (SelectedArea is not { } area) { Error = "Pick or create a sync area under Sync Lights."; return; }
        RefreshDisplays();
        if (profile.WindowSources.Count > 0) await RefreshWindowsAsync();
        var (plan, targets, audioOnly) = BuildPlan();
        if (!demo && plan.PhysicalSources.Count > 0 && !ScreenAllowed)
        {
            Notice = platform.ScreenAccessHint ?? "Screen capture is not allowed on this system.";
            return;
        }
        if (plan.PhysicalSources.Count == 0 && audioOnly.Count == 0 && profile.Mode == SyncMode.Extend)
        {
            Error = "No light is assigned to a display yet. Drag lights onto a display under Sync Source.";
            return;
        }
        activeSubstitutions = new();
        Announce(plan);
        try
        {
            await Engine.StartAsync(new SyncEngine.Config { Bridge = bridge, Creds = credentials, Area = area, Lights = lights, Settings = profile.Settings, Demo = demo, Client = client });
            Syncing = true;
            await Engine.ApplyAsync(plan, targets, audioOnly);
            await Engine.ApplyAudioAsync(profile.Settings.Audio);
            StartPing();
        }
        catch (Exception e)
        {
            await Engine.StopAsync();
            Syncing = false;
            Error = e.Message;
        }
    }

    public async Task StopAsync()
    {
        pingCts?.Cancel();
        await Engine.StopAsync();
        Syncing = false; Metrics = new Metrics(); Preview = new Dictionary<int, RGB>();
    }

    void StartPing()
    {
        pingCts?.Cancel();
        var cts = pingCts = new CancellationTokenSource();
        _ = Task.Run(async () =>
        {
            while (!cts.IsCancellationRequested)
            {
                try { if (client != null) { var r = await client.ConfigAsync(cts.Token); post_(() => NetworkMs = r.LatencyMs); } } catch (Exception) { }
                try { await Task.Delay(5000, cts.Token); } catch (OperationCanceledException) { return; }
            }
        });
    }

    // ---- lights, scenes, rooms, zones ----

    void Act(Func<IHueBridge, Task> work)
    {
        var c = client; if (c == null) return;
        _ = Task.Run(async () => { try { await work(c); } catch (Exception e) { post_(() => Error = e.Message); } });
    }

    public void SetLight(HueLight l, bool? on = null, double? brightness = null, RGB? rgb = null)
    {
        int i = IndexOf(l.Id);
        if (i >= 0)
        {
            var list = lights.ToList();
            if (on is bool o) list[i] = list[i] with { IsOn = o };
            if (brightness is double b) list[i] = list[i] with { Brightness = b, IsOn = true };
            Lights = list;
        }
        Act(c => c.SetLightAsync(l.Id, on ?? (brightness != null || rgb != null ? true : null), brightness, rgb, l.Gamut));
    }

    int IndexOf(string id) { for (int i = 0; i < lights.Count; i++) if (lights[i].Id == id) return i; return -1; }

    public void Flash(HueLight l) => Act(async c =>
    {
        await c.SetLightAsync(l.Id, true, 100, new RGB(1, 1, 1), l.Gamut, 0);
        await Task.Delay(400);
        await c.SetLightAsync(l.Id, l.IsOn, l.Brightness, null, null, 200);
    });

    public void SetGroup(HueGroup g, bool? on = null, double? brightness = null)
    {
        if (g.GroupedLightId is not { } gl) return;
        var list = lights.ToList();
        foreach (var id in g.LightIds)
        {
            int i = list.FindIndex(x => x.Id == id);
            if (i < 0) continue;
            if (on is bool o) list[i] = list[i] with { IsOn = o };
            if (brightness is double b) list[i] = list[i] with { Brightness = b };
        }
        Lights = list;
        Act(c => c.SetGroupAsync(gl, on, brightness));
    }

    public bool IsOn(HueGroup g) => lights.Any(l => g.LightIds.Contains(l.Id) && l.IsOn);
    public double Brightness(HueGroup g)
    {
        var ls = lights.Where(l => g.LightIds.Contains(l.Id) && l.IsOn).ToList();
        return ls.Count == 0 ? 100 : ls.Average(l => l.Brightness);
    }

    public void Recall(HueScene s) => Act(async c => { await c.RecallAsync(s.Id); await Task.Delay(600); post_(async () => await RefreshAsync()); });

    public void SaveZone(string? id, string name, IReadOnlyList<string> lightIds)
    {
        if (demo)
        {
            var g = groups.ToList();
            if (id != null && g.FindIndex(x => x.Id == id) is var i && i >= 0) g[i] = g[i] with { Name = name, LightIds = lightIds };
            else g.Add(new HueGroup($"demo-zone-{Guid.NewGuid().ToString()[..6]}", name, GroupKind.Zone, null, lightIds, null));
            Groups = g; return;
        }
        Act(async c =>
        {
            if (id != null) await c.UpdateZoneAsync(id, name, lightIds); else await c.CreateZoneAsync(name, lightIds);
            post_(async () => await RefreshAsync());
        });
    }

    public void DeleteZone(HueGroup g)
    {
        if (demo) { Groups = groups.Where(x => x.Id != g.Id).ToList(); return; }
        Act(async c => { await c.DeleteZoneAsync(g.Id); post_(async () => await RefreshAsync()); });
    }

    /// <summary>The set of lights that can follow screens. The bridge streams one area at a time, so every light used by any
    /// display must live in this area; which display drives which light is decided by the profile, not by the area.</summary>
    public void SaveArea(string? id, string name, IReadOnlyList<string> lightIds)
    {
        var chosen = lights.Where(l => lightIds.Contains(l.Id) && l.SupportsEntertainment).ToList();
        if (chosen.Count == 0) { Error = "Pick at least one light that supports Hue Entertainment."; return; }
        if (demo)
        {
            var chans = chosen.Select((l, i) => new EntertainmentChannel(i, i / (double)Math.Max(chosen.Count - 1, 1) * 1.6 - 0.8, 0.8, 0, new[] { l.EntertainmentServiceId ?? "" })).ToList();
            var list = areas.ToList();
            if (id != null && list.FindIndex(a => a.Id == id) is var k && k >= 0) list[k] = list[k] with { Name = name, Channels = chans };
            else { var a = new EntertainmentArea($"demo-area-{Guid.NewGuid().ToString()[..6]}", name, "screen", false, chans); list.Add(a); EditProfile(p => p.AreaId = a.Id); }
            Areas = list; return;
        }
        Act(async c =>
        {
            if (id != null) await c.UpdateAreaAsync(id, name, chosen); else await c.CreateAreaAsync(name, chosen);
            post_(async () =>
            {
                await RefreshAsync();
                if (id == null && areas.FirstOrDefault(a => a.Name == name) is { } a) EditProfile(p => p.AreaId = a.Id);
            });
        });
    }

    public async Task CreateRecommendedAreaAsync()
    {
        if (demo) return;
        var capable = lights.Where(l => l.SupportsEntertainment).ToList();
        if (capable.Count == 0 || client == null)
        {
            Error = "None of your lights support Hue Entertainment. Bulbs with the Entertainment logo or Hue Play bars work best.";
            return;
        }
        Busy = true;
        try
        {
            await client.CreateAreaAsync(Brand.AreaName, capable);
            await RefreshAsync();
            if (areas.FirstOrDefault(a => a.Name == Brand.AreaName) is { } a) EditProfile(p => p.AreaId = a.Id);
        }
        catch (Exception e) { Error = $"{e.Message} You can also create an Entertainment area in the Hue app and pick it here."; }
        Busy = false;
    }

    // ---- metrics log (developer option) ----

    StreamWriter? metricsLog; DateTime metricsStart;
    void MetricsLogWrite(Metrics m)
    {
        if (Launch.MetricsLog is not { } path) return;
        if (metricsLog == null)
        {
            try { metricsLog = new StreamWriter(path, false) { AutoFlush = true }; metricsLog.WriteLine("t_s,capture_fps,capture_ms,reduce_ms,gpu_ms,end_to_end_ms,audio_to_light_ms,send_hz,cpu_percent,network_ms,sessions,mode"); metricsStart = DateTime.UtcNow; }
            catch (IOException) { return; }
        }
        var c = System.Globalization.CultureInfo.InvariantCulture;
        metricsLog.WriteLine(string.Join(",", (DateTime.UtcNow - metricsStart).TotalSeconds.ToString("F2", c), m.CaptureFps.ToString("F1", c), m.CaptureMs.ToString("F2", c),
            m.ReduceMs.ToString("F3", c), "0", m.EndToEndMs.ToString("F2", c), m.AudioToLightMs.ToString("F2", c), m.SendHz.ToString("F1", c), m.CpuPercent.ToString("F1", c),
            networkMs?.ToString("F2", c) ?? "", m.CaptureSessions, $"\"{m.Mode}\""));
    }
}
