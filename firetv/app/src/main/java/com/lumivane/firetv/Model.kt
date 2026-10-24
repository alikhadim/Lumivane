package com.lumivane.firetv

import android.app.Application
import android.os.SystemClock
import androidx.lifecycle.AndroidViewModel
import androidx.lifecycle.viewModelScope
import kotlinx.coroutines.CancellationException
import kotlinx.coroutines.Job
import kotlinx.coroutines.delay
import kotlinx.coroutines.flow.MutableStateFlow
import kotlinx.coroutines.flow.StateFlow
import kotlinx.coroutines.flow.asStateFlow
import kotlinx.coroutines.flow.update
import kotlinx.coroutines.launch
import kotlinx.coroutines.sync.Mutex
import kotlinx.coroutines.sync.withLock
import kotlinx.coroutines.withTimeoutOrNull

enum class Screen { Welcome, Discover, Manual, Pair, Main }
enum class Tab(val label: String) { Home("Home"), Areas("Areas"), Rooms("Rooms"), Lights("Lights"), Scenes("Scenes"), Settings("Settings") }

data class UiState(
    val screen: Screen = Screen.Welcome,
    val tab: Tab = Tab.Home,
    val snapshot: Snapshot? = null,
    val error: String? = null,
    val found: List<String> = emptyList(),
    val scanning: Boolean = false,
    val lookingUp: Boolean = false,
    val pairIp: String = "",
    val pairLeft: Int = 0,
    val pairFailed: Boolean = false,
    val demo: Boolean = false,
    val ip: String? = null,
    val areas: List<Area> = emptyList(),
    val areaId: String? = null,
    val creatingArea: Boolean = false,
    val areasLoaded: Boolean = false,
    val source: SourceKind = SourceKind.Screen,
    val profile: String = Profile.presets[0].name,
    val syncError: String? = null,
    val editor: Editor? = null,
    val crash: String? = null,
) {
    val area: Area? get() = areas.firstOrNull { it.id == areaId } ?: areas.firstOrNull()
}

data class EdLight(val light: EntLight, val member: Boolean, val cell: Int)
data class Editor(
    val areaId: String?,
    val name: String,
    val lights: List<EdLight> = emptyList(),
    val loading: Boolean = true,
    val saving: Boolean = false,
    val message: String? = null,
)

class AppModel(app: Application) : AndroidViewModel(app) {
    private val store = Store(app)
    private val _s = MutableStateFlow(UiState())
    val state: StateFlow<UiState> = _s.asStateFlow()

    private var api: HueApi? = null
    private var sync: SyncBackend? = null
    private val sendLock = Mutex()
    private var lastSend = 0L
    private var scanJob: Job? = null
    private var pairJob: Job? = null

    init {
        val ip = store.ip
        val pin = store.pin
        val key = store.loadKey()
        loadPrefs()
        _s.update { it.copy(crash = store.takeCrash()) }
        when {
            store.demo -> enterDemo()
            ip != null && pin != null && key != null -> {
                connect(ip, key, pin, store.loadClientKey())
                _s.update { it.copy(screen = Screen.Main, ip = ip) }
            }
        }
        viewModelScope.launch {
            while (true) {
                val st = _s.value
                if (st.screen == Screen.Main) {
                    refresh()
                    if (st.tab == Tab.Home && SyncHub.status.value.state == SyncState.Idle) loadAreas()
                }
                delay(5_000)
            }
        }
    }

    // Navigation

    fun back() {
        when (_s.value.screen) {
            Screen.Discover -> { scanJob?.cancel(); _s.update { it.copy(screen = Screen.Welcome, scanning = false, error = null) } }
            Screen.Manual -> _s.update { it.copy(screen = Screen.Discover) }
            Screen.Pair -> { pairJob?.cancel(); _s.update { it.copy(screen = Screen.Discover, error = null) } }
            else -> {}
        }
    }

    fun selectTab(t: Tab) = _s.update { if (it.tab == t) it else it.copy(tab = t) }
    fun manualEntry() { scanJob?.cancel(); _s.update { it.copy(screen = Screen.Manual, scanning = false, error = null) } }

    // Setup

    fun startDemo() {
        store.clear()
        store.demo = true
        enterDemo()
    }

    private fun enterDemo() {
        api = DemoApi()
        sync = DemoSync()
        _s.update { UiState(screen = Screen.Main, demo = true, source = it.source, profile = it.profile, crash = it.crash) }
        viewModelScope.launch { refresh(); loadAreas() }
    }

    /** For tests: replaces the backends and shows the main screen. */
    internal fun injectForTest(a: HueApi, b: SyncBackend) {
        api = a
        sync = b
        _s.update { it.copy(screen = Screen.Main, demo = false, ip = "192.168.0.2") }
        viewModelScope.launch { refresh(force = true); loadAreas() }
    }

    private fun connect(ip: String, key: String, pin: String, clientKey: String?) {
        val client = HueClient(ip, key, pin)
        api = client
        sync = HueSync(client, ip, clientKey)
    }

    fun startDiscover() {
        scanJob?.cancel()
        _s.update { it.copy(screen = Screen.Discover, found = emptyList(), scanning = true, error = null) }
        scanJob = viewModelScope.launch {
            try {
                withTimeoutOrNull(12_000) {
                    Discovery.scan(getApplication()).collect { ip ->
                        _s.update { if (ip in it.found) it else it.copy(found = it.found + ip) }
                    }
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _s.update { it.copy(error = "Network search failed. Enter the IP address instead.") }
            }
            _s.update { it.copy(scanning = false) }
        }
    }

    fun lookupOnline() {
        _s.update { it.copy(lookingUp = true, error = null) }
        viewModelScope.launch {
            val ips = try { Discovery.online() } catch (e: CancellationException) { throw e } catch (e: Exception) { emptyList() }
            _s.update {
                it.copy(
                    lookingUp = false,
                    found = (it.found + ips).distinct(),
                    error = if (ips.isEmpty()) "The online lookup found no bridge." else null,
                )
            }
        }
    }

    fun pair(ip: String) {
        scanJob?.cancel()
        pairJob?.cancel()
        _s.update { it.copy(screen = Screen.Pair, pairIp = ip, pairFailed = false, error = null, pairLeft = PAIR_SECONDS, scanning = false) }
        pairJob = viewModelScope.launch {
            var left = PAIR_SECONDS
            while (left > 0) {
                when (val r = Pairing.attempt(ip)) {
                    is PairResult.Paired -> {
                        store.clear()
                        store.save(ip, r.pin, r.key, r.clientKey)
                        connect(ip, r.key, r.pin, r.clientKey)
                        _s.value = UiState(screen = Screen.Main, ip = ip)
                        loadPrefs()
                        refresh()
                        loadAreas()
                        return@launch
                    }
                    PairResult.NotPressed -> {}
                    is PairResult.Failed -> {
                        _s.update { it.copy(pairFailed = true, error = r.message) }
                        return@launch
                    }
                }
                delay(2_000)
                left -= 2
                _s.update { it.copy(pairLeft = left) }
            }
            _s.update { it.copy(pairFailed = true, error = "The link button was not pressed in time.") }
        }
    }

    fun forget() {
        SyncHub.stop()
        store.clear()
        api = null
        sync = null
        SyncHub.settings.value = Profile.presets[0].settings
        _s.value = UiState()
    }

    // Sync

    private fun loadPrefs() {
        val profile = Profile.presets.firstOrNull { it.name == store.getString("profile") } ?: Profile.presets[0]
        var cfg = profile.settings
        store.getString("style")?.let { n -> ColorStyle.entries.firstOrNull { it.name == n }?.let { cfg = cfg.copy(style = it) } }
        store.getString("brightness")?.toFloatOrNull()?.let { cfg = cfg.copy(brightness = it.coerceIn(0.1f, 1f)) }
        store.getString("smoothing")?.toFloatOrNull()?.let { cfg = cfg.copy(smoothing = it.coerceIn(0f, 1f)) }
        SyncHub.settings.value = cfg
        val src = SourceKind.entries.firstOrNull { it.name == store.getString("source") } ?: SourceKind.Screen
        _s.update { it.copy(profile = profile.name, source = src, areaId = store.getString("area")) }
    }

    fun loadAreas() {
        val b = sync ?: return
        viewModelScope.launch {
            try {
                val list = b.areas()
                _s.update { it.copy(areas = list, areasLoaded = true) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _s.update { it.copy(areasLoaded = true, error = e.message ?: "Something went wrong.") }
            }
        }
    }

    fun createArea() {
        val b = sync ?: return
        _s.update { it.copy(creatingArea = true, syncError = null) }
        viewModelScope.launch {
            try {
                val ls = b.entertainmentLights().take(20)
                if (ls.isEmpty()) {
                    _s.update { it.copy(syncError = "No lights on this bridge support sync. Entertainment-capable Hue lights are required.") }
                } else {
                    b.saveArea(null, "Lumivane", ls.mapIndexed { i, l -> l to Cell.defaultOrder[i % Cell.defaultOrder.size] })
                    loadAreas()
                }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Exception) {
                _s.update { it.copy(syncError = e.message ?: "Could not create the sync area.") }
            }
            _s.update { it.copy(creatingArea = false) }
        }
    }

    // Area editor

    fun openEditor(areaId: String?) {
        val b = sync ?: return
        val st = _s.value
        val area = st.areas.firstOrNull { it.id == areaId }
        _s.update { it.copy(editor = Editor(areaId, area?.name ?: "Area ${st.areas.size + 1}")) }
        viewModelScope.launch {
            try {
                val ls = b.entertainmentLights()
                var next = 0
                val rows = ls.map { l ->
                    val ch = area?.channels?.firstOrNull { it.service == l.service }
                    if (ch != null) EdLight(l, true, Cell.nearest(ch.x, ch.z))
                    else EdLight(l, area == null, Cell.defaultOrder[next++ % Cell.defaultOrder.size])
                }
                _s.update { st2 -> st2.copy(editor = st2.editor?.copy(lights = rows, loading = false, message = if (ls.isEmpty()) "No lights on this bridge support sync." else null)) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Throwable) {
                _s.update { st2 -> st2.copy(editor = st2.editor?.copy(loading = false, message = e.message ?: e.toString())) }
            }
        }
    }

    private fun editLights(f: (List<EdLight>) -> List<EdLight>) =
        _s.update { st -> st.copy(editor = st.editor?.let { it.copy(lights = f(it.lights), message = null) }) }

    fun toggleMember(i: Int) = editLights { l -> l.mapIndexed { k, e -> if (k == i) e.copy(member = !e.member) else e } }
    fun cycleCell(i: Int) = editLights { l -> l.mapIndexed { k, e -> if (k == i) e.copy(cell = (e.cell + 1) % Cell.all.size, member = true) else e } }
    fun closeEditor() = _s.update { it.copy(editor = null) }

    fun saveEditor() {
        val b = sync ?: return
        val ed = _s.value.editor ?: return
        val members = ed.lights.filter { it.member }.map { it.light to it.cell }
        if (members.isEmpty()) return _s.update { it.copy(editor = ed.copy(message = "Select at least one light.")) }
        if (members.size > 20) return _s.update { it.copy(editor = ed.copy(message = "An area holds at most 20 lights.")) }
        _s.update { it.copy(editor = ed.copy(saving = true, message = null)) }
        viewModelScope.launch {
            try {
                b.saveArea(ed.areaId, ed.name, members)
                loadAreas()
                // An area that had to be recreated gets a new id: keep it selected.
                val st = _s.value
                if (ed.areaId != null && st.areaId == ed.areaId && st.areas.none { it.id == ed.areaId }) {
                    st.areas.firstOrNull { it.name == ed.name }?.let { useArea(it.id) }
                }
                _s.update { it.copy(editor = null) }
            } catch (e: CancellationException) {
                throw e
            } catch (e: Throwable) {
                _s.update { st -> st.copy(editor = st.editor?.copy(saving = false, message = e.message ?: e.toString())) }
            }
        }
    }

    fun deleteEditorArea() {
        val b = sync ?: return
        val id = _s.value.editor?.areaId ?: return
        if (SyncHub.status.value.state != SyncState.Idle) return _s.update { st -> st.copy(editor = st.editor?.copy(message = "Stop sync before deleting an area.")) }
        viewModelScope.launch {
            try {
                b.deleteArea(id)
                _s.update { it.copy(editor = null, areas = it.areas.filter { a -> a.id != id }) }
                loadAreas()
            } catch (e: CancellationException) {
                throw e
            } catch (e: Throwable) {
                _s.update { st -> st.copy(editor = st.editor?.copy(message = e.message ?: e.toString())) }
            }
        }
    }

    fun useArea(id: String) {
        store.putString("area", id)
        _s.update { it.copy(areaId = id) }
    }

    fun nextArea() {
        val st = _s.value
        if (st.areas.size < 2) return
        val i = st.areas.indexOfFirst { it.id == st.area?.id }
        val n = st.areas[(i + 1) % st.areas.size]
        store.putString("area", n.id)
        _s.update { it.copy(areaId = n.id) }
    }

    fun selectSource(k: SourceKind) {
        store.putString("source", k.name)
        _s.update { it.copy(source = k) }
    }

    fun selectProfile(p: Profile) {
        store.putString("profile", p.name)
        listOf("style", "brightness", "smoothing").forEach { store.putString(it, "") }
        SyncHub.settings.value = p.settings
        _s.update { it.copy(profile = p.name) }
    }

    fun setStyle(style: ColorStyle) {
        store.putString("style", style.name)
        SyncHub.settings.update { it.copy(style = style) }
    }

    fun adjustBrightness(delta: Float) {
        val v = (SyncHub.settings.value.brightness + delta).coerceIn(0.1f, 1f)
        store.putString("brightness", v.toString())
        SyncHub.settings.update { it.copy(brightness = v) }
    }

    fun adjustSmoothing(delta: Float) {
        val v = (SyncHub.settings.value.smoothing + delta).coerceIn(0f, 1f)
        store.putString("smoothing", v.toString())
        SyncHub.settings.update { it.copy(smoothing = v) }
    }

    fun dismissCrash() {
        store.clearCrash()
        _s.update { it.copy(crash = null) }
    }

    fun syncProblem(message: String) = _s.update { it.copy(syncError = message) }

    fun startSync(ctx: android.content.Context, projectionCode: Int = 0, projectionData: android.content.Intent? = null) {
        val st = _s.value
        val b = sync ?: return
        val a = st.area ?: return syncProblem("Create a sync area first.")
        _s.update { it.copy(syncError = null) }
        SyncHub.start(ctx, SyncRequest(b, a, st.source, projectionCode, projectionData))
    }

    fun stopSync() = SyncHub.stop()

    // Control

    fun toggleLight(id: String) {
        val l = _s.value.snapshot?.lights?.firstOrNull { it.id == id } ?: return
        val on = !l.on
        edit { it.withLight(id, on = on) }
        send { setLight(id, on = on) }
    }

    fun dimLight(id: String, delta: Int) {
        val l = _s.value.snapshot?.lights?.firstOrNull { it.id == id } ?: return
        if (!l.dimmable) return
        val b = stepBrightness(l.brightness, delta)
        edit { it.withLight(id, on = true, brightness = b) }
        send { setLight(id, on = true, brightness = b) }
    }

    fun toggleGroup(id: String) {
        val g = _s.value.snapshot?.groups?.firstOrNull { it.id == id } ?: return
        val on = !g.on
        edit { it.withGroup(id, on = on) }
        send { g.groupedLightId?.let { setGroup(it, on = on) } ?: g.lightIds.forEach { setLight(it, on = on) } }
    }

    fun dimGroup(id: String, delta: Int) {
        val g = _s.value.snapshot?.groups?.firstOrNull { it.id == id } ?: return
        val b = stepBrightness(g.brightness, delta)
        edit { it.withGroup(id, on = true, brightness = b) }
        send { g.groupedLightId?.let { setGroup(it, on = true, brightness = b) } ?: g.lightIds.forEach { setLight(it, on = true, brightness = b) } }
    }

    fun recall(sceneId: String) {
        send { recall(sceneId) }
        viewModelScope.launch { delay(800); refresh(force = true) }
    }

    private fun edit(f: (Snapshot) -> Snapshot) = _s.update { st -> st.copy(snapshot = st.snapshot?.let(f)) }

    private fun send(block: suspend HueApi.() -> Unit) {
        val a = api ?: return
        lastSend = SystemClock.elapsedRealtime()
        viewModelScope.launch {
            sendLock.withLock {
                try {
                    a.block()
                    _s.update { it.copy(error = null) }
                } catch (e: CancellationException) {
                    throw e
                } catch (e: Exception) {
                    _s.update { it.copy(error = e.message ?: "Something went wrong.") }
                }
            }
        }
    }

    private suspend fun refresh(force: Boolean = false) {
        val a = api ?: return
        try {
            val snap = a.snapshot()
            // Skip a poll that raced with a local change, so the screen does not jump back.
            if (force || SystemClock.elapsedRealtime() - lastSend > 1_500) {
                _s.update { it.copy(snapshot = snap, error = null) }
            }
        } catch (e: CancellationException) {
            throw e
        } catch (e: Exception) {
            _s.update { it.copy(error = e.message ?: "Something went wrong.") }
        }
    }

    private companion object {
        const val PAIR_SECONDS = 30
    }
}
