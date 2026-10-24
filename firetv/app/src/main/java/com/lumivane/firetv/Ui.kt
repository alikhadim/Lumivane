package com.lumivane.firetv

import androidx.activity.compose.BackHandler
import android.app.Activity
import android.media.projection.MediaProjectionManager
import androidx.activity.compose.rememberLauncherForActivityResult
import androidx.activity.result.contract.ActivityResultContracts
import androidx.compose.foundation.Canvas
import androidx.compose.foundation.layout.PaddingValues
import androidx.compose.foundation.layout.size
import androidx.compose.foundation.lazy.itemsIndexed
import androidx.compose.foundation.shape.CircleShape
import androidx.compose.ui.geometry.CornerRadius
import androidx.compose.ui.geometry.Offset
import androidx.compose.ui.geometry.Size
import androidx.compose.ui.graphics.Brush
import androidx.compose.ui.graphics.drawscope.Stroke
import androidx.compose.ui.platform.LocalContext
import androidx.compose.ui.text.style.TextOverflow
import androidx.compose.foundation.background
import androidx.compose.foundation.border
import androidx.compose.foundation.clickable
import androidx.compose.foundation.layout.Arrangement
import androidx.compose.foundation.layout.Box
import androidx.compose.foundation.layout.Column
import androidx.compose.foundation.layout.Row
import androidx.compose.foundation.layout.Spacer
import androidx.compose.foundation.layout.fillMaxHeight
import androidx.compose.foundation.layout.fillMaxSize
import androidx.compose.foundation.layout.fillMaxWidth
import androidx.compose.foundation.layout.height
import androidx.compose.foundation.layout.padding
import androidx.compose.foundation.layout.width
import androidx.compose.foundation.lazy.LazyColumn
import androidx.compose.foundation.lazy.items
import androidx.compose.foundation.rememberScrollState
import androidx.compose.foundation.shape.RoundedCornerShape
import androidx.compose.foundation.verticalScroll
import androidx.compose.material3.MaterialTheme
import androidx.compose.material3.Text
import androidx.compose.material3.darkColorScheme
import androidx.compose.runtime.Composable
import androidx.compose.runtime.LaunchedEffect
import androidx.compose.runtime.getValue
import androidx.compose.runtime.mutableStateOf
import androidx.compose.runtime.remember
import androidx.compose.runtime.setValue
import androidx.compose.ui.Alignment
import androidx.compose.ui.Modifier
import androidx.compose.ui.draw.clip
import androidx.compose.ui.draw.scale
import androidx.compose.ui.focus.FocusRequester
import androidx.compose.ui.focus.focusRequester
import androidx.compose.ui.focus.onFocusChanged
import androidx.compose.ui.graphics.Color
import androidx.compose.ui.graphics.Shape
import androidx.compose.ui.input.key.Key
import androidx.compose.ui.input.key.KeyEvent
import androidx.compose.ui.input.key.KeyEventType
import androidx.compose.ui.input.key.key
import androidx.compose.ui.input.key.onPreviewKeyEvent
import androidx.compose.ui.input.key.type
import androidx.compose.ui.text.font.FontWeight
import androidx.compose.ui.unit.dp
import androidx.compose.ui.unit.sp
import androidx.lifecycle.compose.collectAsStateWithLifecycle
import androidx.lifecycle.viewmodel.compose.viewModel

private val Bg = Color(0xFF0E0F14)
private val Surface = Color(0xFF171923)
private val SurfaceHi = Color(0xFF262A3C)
private val Line = Color(0xFF2C3045)
private val Accent = Color(0xFFFFB547)
private val TextMain = Color(0xFFF0F0F5)
private val TextDim = Color(0xFF9A9FB2)
private val Err = Color(0xFFFF7B7B)
private val Card = RoundedCornerShape(14.dp)
private val SidebarBg = Color(0xFF12141C)
private val Good = Color(0xFF4CD27A)

@Composable
fun App(vm: AppModel = viewModel()) {
    val s by vm.state.collectAsStateWithLifecycle()
    BackHandler(enabled = s.screen == Screen.Discover || s.screen == Screen.Manual || s.screen == Screen.Pair) { vm.back() }
    MaterialTheme(colorScheme = darkColorScheme(background = Bg, surface = Surface, primary = Accent)) {
        // 5% inset keeps content inside the TV overscan-safe area.
        Box(Modifier.fillMaxSize().background(Bg).padding(horizontal = 64.dp, vertical = 36.dp)) {
            when (s.screen) {
                Screen.Welcome -> Welcome(vm)
                Screen.Discover -> Discover(vm, s)
                Screen.Manual -> Manual(vm)
                Screen.Pair -> Pair(vm, s)
                Screen.Main -> Main(vm, s)
            }
        }
    }
}

// Building blocks

@Composable
private fun Modifier.autoFocus(): Modifier {
    val r = remember { FocusRequester() }
    LaunchedEffect(Unit) { runCatching { r.requestFocus() } }
    return focusRequester(r)
}

@Composable
private fun Modifier.tvClickable(
    shape: Shape = Card,
    selected: Boolean = false,
    onFocus: (() -> Unit)? = null,
    onKey: ((KeyEvent) -> Boolean)? = null,
    onClick: () -> Unit,
): Modifier {
    var focused by remember { mutableStateOf(false) }
    return this
        .scale(if (focused) 1.03f else 1f)
        .background(if (focused) SurfaceHi else Surface, shape)
        .border(if (focused) 3.dp else if (selected) 2.dp else 1.dp, if (focused || selected) Accent else Line, shape)
        .onFocusChanged { st ->
            focused = st.hasFocus
            if (st.hasFocus) onFocus?.invoke()
        }
        .then(if (onKey != null) Modifier.onPreviewKeyEvent(onKey) else Modifier)
        .clickable(onClick = onClick)
}

@Composable
private fun TvButton(label: String, modifier: Modifier = Modifier, width: Int = 420, onClick: () -> Unit) {
    Box(
        modifier.width(width.dp).tvClickable(onClick = onClick).padding(horizontal = 24.dp, vertical = 16.dp),
        contentAlignment = Alignment.CenterStart,
    ) { Text(label, color = TextMain, fontSize = 22.sp) }
}

@Composable
private fun Title(text: String) = Text(text, color = TextMain, fontSize = 40.sp, fontWeight = FontWeight.Bold)

@Composable
private fun Dim(text: String, size: Int = 18) = Text(text, color = TextDim, fontSize = size.sp)

@Composable
private fun ErrorLine(text: String?) {
    if (text != null) Text(text, color = Err, fontSize = 18.sp, modifier = Modifier.padding(vertical = 8.dp))
}

// Setup screens

@Composable
private fun Welcome(vm: AppModel) {
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center) {
        Text("Lumivane", color = Accent, fontSize = 56.sp, fontWeight = FontWeight.Bold)
        Dim("Control your Philips Hue lights with the Fire TV remote.", 24)
        Spacer(Modifier.height(36.dp))
        TvButton("Find my Hue Bridge", Modifier.autoFocus(), onClick = vm::startDiscover)
        Spacer(Modifier.height(12.dp))
        TvButton("Try demo mode", onClick = vm::startDemo)
        Spacer(Modifier.height(28.dp))
        Dim("The bridge and this TV need to be on the same network. Control stays on your home network.")
    }
}

@Composable
private fun Discover(vm: AppModel, s: UiState) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Title("Find your bridge")
        Spacer(Modifier.height(8.dp))
        Dim(
            when {
                s.scanning || s.lookingUp -> "Searching..."
                s.found.isEmpty() -> "No bridge found. Search again, enter the IP address, or look it up online."
                else -> "Select your bridge."
            },
            20,
        )
        ErrorLine(s.error)
        Spacer(Modifier.height(16.dp))
        s.found.forEachIndexed { i, ip ->
            TvButton("Hue Bridge  $ip", if (i == 0) Modifier.autoFocus() else Modifier) { vm.pair(ip) }
            Spacer(Modifier.height(10.dp))
        }
        TvButton("Search again", if (s.found.isEmpty()) Modifier.autoFocus() else Modifier, onClick = vm::startDiscover)
        Spacer(Modifier.height(10.dp))
        TvButton("Enter IP address", onClick = vm::manualEntry)
        Spacer(Modifier.height(10.dp))
        TvButton("Look up online", onClick = vm::lookupOnline)
        Dim("Asks the Philips discovery service, which sees your public IP address.", 15)
        Spacer(Modifier.height(10.dp))
        TvButton("Back", onClick = vm::back)
    }
}

@Composable
private fun Manual(vm: AppModel) {
    var text by remember { mutableStateOf("") }
    val valid = isIPv4(text)
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        Title("Bridge IP address")
        Spacer(Modifier.height(8.dp))
        Text(
            text.ifEmpty { "192.168.1.2" },
            color = if (text.isEmpty()) TextDim else TextMain, fontSize = 36.sp, fontWeight = FontWeight.Medium,
        )
        Dim("Find it in the Hue app under Settings, Bridges.", 16)
        Spacer(Modifier.height(16.dp))
        val keys = listOf("1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", "Del")
        keys.chunked(6).forEachIndexed { r, row ->
            Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
                row.forEachIndexed { c, k ->
                    val m = if (r == 0 && c == 0) Modifier.autoFocus() else Modifier
                    Box(
                        m.width(96.dp).tvClickable(onClick = {
                            text = if (k == "Del") text.dropLast(1) else if (text.length < 15) text + k else text
                        }).padding(vertical = 14.dp),
                        contentAlignment = Alignment.Center,
                    ) { Text(k, color = TextMain, fontSize = 24.sp) }
                }
            }
            Spacer(Modifier.height(10.dp))
        }
        Spacer(Modifier.height(6.dp))
        TvButton(if (valid) "Connect" else "Enter a full address", onClick = { if (valid) vm.pair(text) })
        Spacer(Modifier.height(10.dp))
        TvButton("Back", onClick = vm::back)
    }
}

@Composable
private fun Pair(vm: AppModel, s: UiState) {
    Column(Modifier.fillMaxSize(), verticalArrangement = Arrangement.Center) {
        Title("Press the link button")
        Spacer(Modifier.height(8.dp))
        Dim("Press the large round button on top of your Hue Bridge at ${s.pairIp}.", 22)
        Spacer(Modifier.height(8.dp))
        if (s.pairFailed) ErrorLine(s.error) else Dim("Waiting for the bridge... ${s.pairLeft} s left", 20)
        Spacer(Modifier.height(28.dp))
        if (s.pairFailed) {
            TvButton("Try again", Modifier.autoFocus()) { vm.pair(s.pairIp) }
            Spacer(Modifier.height(10.dp))
            TvButton("Back", onClick = vm::back)
        } else {
            TvButton("Cancel", Modifier.autoFocus(), onClick = vm::back)
        }
    }
}

// Main screen with sidebar

@Composable
internal fun Main(vm: AppModel, s: UiState) {
    val entry = remember { FocusRequester() }
    val nav = remember { Tab.entries.associateWith { FocusRequester() } }
    var contentFocused by remember { mutableStateOf(false) }
    val status by SyncHub.status.collectAsStateWithLifecycle()
    BackHandler(enabled = contentFocused || s.editor != null) {
        if (s.editor != null) vm.closeEditor() else runCatching { nav.getValue(s.tab).requestFocus() }
    }
    LaunchedEffect(Unit) { runCatching { entry.requestFocus() } }
    Row(Modifier.fillMaxSize()) {
        Sidebar(vm, s, status, nav, entry)
        Spacer(Modifier.width(24.dp))
        Box(Modifier.weight(1f).fillMaxHeight().onFocusChanged { contentFocused = it.hasFocus }) {
            val snap = s.snapshot
            when {
                s.tab == Tab.Home -> HomePage(vm, s, entry)
                s.tab == Tab.Areas -> AreasPage(vm, s, entry)
                s.tab == Tab.Settings -> SettingsPage(vm, s, entry)
                snap == null -> Column { ErrorLine(s.error); Dim("Loading...", 22) }
                s.tab == Tab.Rooms -> RoomsPage(vm, s, snap, entry)
                s.tab == Tab.Lights -> LightsPage(vm, s, snap, entry)
                else -> ScenesPage(vm, s, snap, entry)
            }
        }
    }
}

@Composable
private fun Sidebar(vm: AppModel, s: UiState, status: SyncStatus, nav: Map<Tab, FocusRequester>, entry: FocusRequester) {
    val shape = RoundedCornerShape(18.dp)
    Column(Modifier.width(220.dp).fillMaxHeight().background(SidebarBg, shape).padding(14.dp)) {
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(start = 6.dp, top = 6.dp)) {
            Box(Modifier.size(34.dp).background(Accent, RoundedCornerShape(9.dp)), contentAlignment = Alignment.Center) {
                Text("L", color = Bg, fontSize = 22.sp, fontWeight = FontWeight.Bold)
            }
            Spacer(Modifier.width(10.dp))
            Text("Lumivane", color = TextMain, fontSize = 24.sp, fontWeight = FontWeight.Bold)
        }
        Spacer(Modifier.height(22.dp))
        Tab.entries.forEach { t ->
            NavItem(t.label, s.tab == t, nav.getValue(t), onSelect = { vm.selectTab(t) }, entry = entry)
            Spacer(Modifier.height(4.dp))
        }
        Spacer(Modifier.weight(1f))
        val (dot, title, sub) = when (status.state) {
            SyncState.Running -> Triple(Good, "Syncing", status.label)
            SyncState.Starting -> Triple(Accent, "Starting", status.label)
            SyncState.Idle -> Triple(TextDim, "Idle", if (s.demo) "Demo mode" else "Ready")
        }
        Row(verticalAlignment = Alignment.CenterVertically, modifier = Modifier.padding(6.dp)) {
            Box(Modifier.size(10.dp).background(dot, CircleShape))
            Spacer(Modifier.width(10.dp))
            Column {
                Text(title, color = TextMain, fontSize = 18.sp, fontWeight = FontWeight.Medium)
                Text(sub, color = TextDim, fontSize = 14.sp, maxLines = 1, overflow = TextOverflow.Ellipsis)
            }
        }
    }
}

@Composable
private fun NavItem(label: String, selected: Boolean, req: FocusRequester, onSelect: () -> Unit, entry: FocusRequester) {
    var focused by remember { mutableStateOf(false) }
    val shape = RoundedCornerShape(12.dp)
    Box(
        Modifier.fillMaxWidth()
            .focusRequester(req)
            .background(if (selected) Accent else Color.Transparent, shape)
            .then(if (focused) Modifier.border(3.dp, TextMain, shape) else Modifier)
            .onFocusChanged { focused = it.hasFocus; if (it.hasFocus) onSelect() }
            .onPreviewKeyEvent { e ->
                if (e.key == Key.DirectionRight) {
                    e.type != KeyEventType.KeyDown || runCatching { entry.requestFocus() }.isSuccess
                } else false
            }
            .clickable(onClick = onSelect)
                        .padding(horizontal = 16.dp, vertical = 10.dp),
    ) { Text(label, color = if (selected) Bg else TextMain, fontSize = 22.sp, fontWeight = if (selected) FontWeight.Bold else FontWeight.Normal) }
}

private fun dpadDim(onDim: (Int) -> Unit): (KeyEvent) -> Boolean = { e ->
    val dir = when (e.key) { Key.DirectionLeft -> -10; Key.DirectionRight -> 10; else -> 0 }
    if (dir == 0) false else { if (e.type == KeyEventType.KeyDown) onDim(dir); true }
}

@Composable
private fun PageTitle(title: String, hint: String? = null) {
    Text(title, color = TextMain, fontSize = 34.sp, fontWeight = FontWeight.Bold)
    if (hint != null) Dim(hint, 16)
}

// Home

@Composable
private fun HomePage(vm: AppModel, s: UiState, entry: FocusRequester) {
    val ctx = LocalContext.current
    val status by SyncHub.status.collectAsStateWithLifecycle()
    val live by SyncHub.live.collectAsStateWithLifecycle()
    val cfg by SyncHub.settings.collectAsStateWithLifecycle()
    val running = status.state != SyncState.Idle
    val area = s.area

    val launcher = rememberLauncherForActivityResult(ActivityResultContracts.StartActivityForResult()) { r ->
        val data = r.data
        if (r.resultCode == Activity.RESULT_OK && data != null) vm.startSync(ctx, r.resultCode, data)
        else vm.syncProblem("Screen capture was not allowed.")
    }
    fun toggle() {
        if (running) return vm.stopSync()
        if (s.source != SourceKind.Screen) return vm.startSync(ctx)
        try {
            val mpm = ctx.getSystemService(android.content.Context.MEDIA_PROJECTION_SERVICE) as MediaProjectionManager
            launcher.launch(mpm.createScreenCaptureIntent())
        } catch (e: Exception) {
            vm.syncProblem("This Fire TV does not offer screen capture. Use Aurora, Candle or Color loop instead.")
        }
    }

    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 40.dp)) {
        s.crash?.let { c ->
            Column(Modifier.fillMaxWidth().tvClickable(onClick = vm::dismissCrash).padding(12.dp)) {
                Text("The app closed unexpectedly last time. Press OK to dismiss.", color = Err, fontSize = 16.sp, fontWeight = FontWeight.Bold)
                Text(c, color = TextDim, fontSize = 12.sp)
            }
            Spacer(Modifier.height(10.dp))
        }
        Viz(area, live, running)
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(8.dp)) {
            Chip(if (s.demo) "Demo bridge" else "Bridge connected", true)
            Chip(if (area == null) "No sync area" else "${area.channels.size} lights in sync area", area != null)
            Chip(when (status.state) { SyncState.Running -> "Streaming"; SyncState.Starting -> "Connecting"; else -> "Not streaming" }, running)
        }
        listOfNotNull(s.syncError, status.message, s.error).firstOrNull()?.let { ErrorLine(it) }
        status.warning?.let { Text(it, color = Accent, fontSize = 16.sp, modifier = Modifier.padding(vertical = 6.dp)) }
        Spacer(Modifier.height(10.dp))
        if (area == null && s.areasLoaded) {
            TvButton(if (s.creatingArea) "Creating..." else "Create sync area from my lights", Modifier.focusRequester(entry), width = 460) { if (!s.creatingArea) vm.createArea() }
            Dim("Uses every Hue light that supports sync, placed around the screen. Change lights and positions later under Areas.", 15)
        } else {
            Row(verticalAlignment = Alignment.CenterVertically) {
                val shape = RoundedCornerShape(26.dp)
                Box(
                    Modifier.focusRequester(entry).width(200.dp).tvClickable(shape = shape, onClick = ::toggle)
                        .background(if (running) Color(0xFFE5484D) else Accent, shape).padding(vertical = 14.dp),
                    contentAlignment = Alignment.Center,
                ) { Text(if (running) "Stop" else "Start sync", color = if (running) TextMain else Bg, fontSize = 24.sp, fontWeight = FontWeight.Bold) }
                if (s.areas.size > 1) {
                    Spacer(Modifier.width(12.dp))
                    TvButton("Area: ${area?.name.orEmpty()}", width = 300, onClick = vm::nextArea)
                }
            }
        }

        SectionLabel("SOURCE")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            SourceKind.entries.forEach { k ->
                SelectCard(k.title, k.blurb, s.source == k, Modifier.weight(1f)) { vm.selectSource(k) }
            }
        }
        SectionLabel("MODE")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            Profile.presets.forEach { p ->
                SelectCard(p.name, p.blurb, s.profile == p.name, Modifier.weight(1f)) { vm.selectProfile(p) }
            }
        }
        SectionLabel("COLOR STYLE")
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            ColorStyle.entries.forEach { st ->
                SelectCard(st.title, st.summary, cfg.style == st, Modifier.weight(1f)) { vm.setStyle(st) }
            }
        }
        SectionLabel("TUNING")
        LevelCard("Brightness", "Maximum light output", true, (cfg.brightness * 100).toInt(), true, {}, { vm.adjustBrightness(it / 100f) })
        Spacer(Modifier.height(10.dp))
        LevelCard("Smoothness", "Higher is calmer, lower reacts faster", true, (cfg.smoothing * 100).toInt(), true, {}, { vm.adjustSmoothing(it / 100f) })
        Spacer(Modifier.height(14.dp))
        Dim("Sound sync is part of the Mac app. Protected apps such as Netflix or Prime Video typically block screen capture, so Screen stays dark there.", 14)
    }
}

@Composable
private fun SectionLabel(text: String) {
    Spacer(Modifier.height(20.dp))
    Text(text, color = TextDim, fontSize = 14.sp, fontWeight = FontWeight.Bold, letterSpacing = androidx.compose.ui.unit.TextUnit(1.5f, androidx.compose.ui.unit.TextUnitType.Sp))
    Spacer(Modifier.height(8.dp))
}

@Composable
private fun Chip(text: String, ok: Boolean) {
    val c = if (ok) Good else TextDim
    Box(Modifier.background(c.copy(alpha = 0.14f), RoundedCornerShape(16.dp)).padding(horizontal = 12.dp, vertical = 6.dp)) {
        Text(text, color = c, fontSize = 15.sp, maxLines = 1)
    }
}

@Composable
private fun SelectCard(title: String, blurb: String, selected: Boolean, modifier: Modifier, onClick: () -> Unit) {
    Column(modifier.fillMaxWidth().tvClickable(selected = selected, onClick = onClick).padding(12.dp)) {
        Text(title, color = TextMain, fontSize = 19.sp, fontWeight = FontWeight.Bold, maxLines = 1)
        Spacer(Modifier.height(4.dp))
        Text(blurb, color = TextDim, fontSize = 13.sp, maxLines = 3, overflow = TextOverflow.Ellipsis)
    }
}

@Composable
private fun Viz(area: Area?, live: Map<Int, RGB>, running: Boolean) {
    Box(Modifier.fillMaxWidth().height(190.dp).clip(Card).background(Surface)) {
        Canvas(Modifier.fillMaxSize()) {
            val w = size.width
            val h = size.height
            val sw = w * 0.34f
            val sh = h * 0.42f
            val tl = Offset((w - sw) / 2, h * 0.18f)
            val r = CornerRadius(10.dp.toPx())
            drawRoundRect(Color(0xFF05060A), tl, Size(sw, sh), r)
            drawRoundRect(Line, tl, Size(sw, sh), r, style = Stroke(2.dp.toPx()))
            area?.channels?.forEach { c ->
                val p = Offset(w / 2 + c.x.toFloat() * 0.44f * w, (h * 0.72f - c.z.toFloat() * 0.4f * h).coerceIn(h * 0.1f, h * 0.9f))
                val rgb = live[c.id]
                if (running && rgb != null) {
                    val col = Color(rgb.r.coerceIn(0f, 1f), rgb.g.coerceIn(0f, 1f), rgb.b.coerceIn(0f, 1f))
                    val gr = 40.dp.toPx()
                    drawCircle(Brush.radialGradient(listOf(col.copy(alpha = 0.55f), Color.Transparent), center = p, radius = gr), gr, p)
                    drawCircle(col, 9.dp.toPx(), p)
                    drawCircle(TextMain.copy(alpha = 0.8f), 9.dp.toPx(), p, style = Stroke(1.5.dp.toPx()))
                } else {
                    drawCircle(Line, 9.dp.toPx(), p, style = Stroke(2.dp.toPx()))
                }
            }
        }
        if (area == null) Box(Modifier.fillMaxSize(), contentAlignment = Alignment.BottomCenter) {
            Dim("Lights appear here once a sync area exists.", 14)
        }
    }
}

// Rooms, lights, scenes, settings

@Composable
private fun RoomsPage(vm: AppModel, s: UiState, snap: Snapshot, entry: FocusRequester) {
    PageTitle("Rooms", "Left and right dim, OK switches on or off. Back returns to the menu.")
    ErrorLine(s.error)
    if (snap.groups.isEmpty()) return Dim("No rooms found on this bridge.", 22)
    Spacer(Modifier.height(8.dp))
    LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(bottom = 40.dp, top = 6.dp)) {
        itemsIndexed(snap.groups, key = { _, g -> g.id }) { i, g ->
            val n = g.lightIds.size
            LevelCard(
                title = g.name,
                subtitle = (if (g.zone) "Zone, " else "") + if (n == 1) "1 light" else "$n lights",
                on = g.on, level = g.brightness, dimmable = true,
                onToggle = { vm.toggleGroup(g.id) }, onDim = { vm.dimGroup(g.id, it) },
                modifier = if (i == 0) Modifier.focusRequester(entry) else Modifier,
            )
        }
    }
}

@Composable
private fun LightsPage(vm: AppModel, s: UiState, snap: Snapshot, entry: FocusRequester) {
    PageTitle("Lights", "Left and right dim, OK switches on or off. Back returns to the menu.")
    ErrorLine(s.error)
    if (snap.lights.isEmpty()) return Dim("No lights found on this bridge.", 22)
    Spacer(Modifier.height(8.dp))
    LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(bottom = 40.dp, top = 6.dp)) {
        itemsIndexed(snap.lights, key = { _, l -> l.id }) { i, l ->
            LevelCard(
                title = l.name, subtitle = if (l.dimmable) "Light" else "On/off only",
                on = l.on, level = l.brightness, dimmable = l.dimmable,
                onToggle = { vm.toggleLight(l.id) }, onDim = { vm.dimLight(l.id, it) },
                modifier = if (i == 0) Modifier.focusRequester(entry) else Modifier,
            )
        }
    }
}

@Composable
private fun LevelCard(
    title: String, subtitle: String, on: Boolean, level: Int, dimmable: Boolean,
    onToggle: () -> Unit, onDim: (Int) -> Unit, modifier: Modifier = Modifier,
) {
    Row(
        modifier.fillMaxWidth().tvClickable(onKey = dpadDim(onDim), onClick = onToggle).padding(horizontal = 20.dp, vertical = 14.dp),
        verticalAlignment = Alignment.CenterVertically,
    ) {
        Column(Modifier.weight(1f)) {
            Text(title, color = TextMain, fontSize = 22.sp, fontWeight = FontWeight.Medium, maxLines = 1)
            Dim(subtitle, 14)
        }
        if (dimmable) {
            Box(Modifier.width(180.dp).height(8.dp).clip(RoundedCornerShape(4.dp)).background(Line)) {
                Box(Modifier.fillMaxHeight().fillMaxWidth(if (on) level / 100f else 0f).background(Accent))
            }
        }
        Spacer(Modifier.width(14.dp))
        Text(if (on) (if (dimmable) "$level%" else "On") else "Off", color = if (on) Accent else TextDim, fontSize = 20.sp, modifier = Modifier.width(60.dp))
    }
}

@Composable
private fun ScenesPage(vm: AppModel, s: UiState, snap: Snapshot, entry: FocusRequester) {
    PageTitle("Scenes", "OK activates a scene.")
    ErrorLine(s.error)
    if (snap.scenes.isEmpty()) return Dim("No scenes found on this bridge.", 22)
    val names = snap.groups.associate { it.id to it.name }
    val sorted = snap.scenes.sortedWith(compareBy({ names[it.groupId].orEmpty().lowercase() }, { it.name.lowercase() }))
    Spacer(Modifier.height(8.dp))
    LazyColumn(verticalArrangement = Arrangement.spacedBy(10.dp), contentPadding = PaddingValues(bottom = 40.dp, top = 6.dp)) {
        itemsIndexed(sorted, key = { _, sc -> sc.id }) { i, sc ->
            Row(
                (if (i == 0) Modifier.focusRequester(entry) else Modifier).fillMaxWidth()
                    .tvClickable(onClick = { vm.recall(sc.id) }).padding(horizontal = 20.dp, vertical = 14.dp),
                verticalAlignment = Alignment.CenterVertically,
            ) {
                Text(sc.name, color = TextMain, fontSize = 22.sp, fontWeight = FontWeight.Medium, modifier = Modifier.weight(1f))
                Dim(names[sc.groupId].orEmpty(), 16)
            }
        }
    }
}

@Composable
private fun SettingsPage(vm: AppModel, s: UiState, entry: FocusRequester) {
    var confirm by remember { mutableStateOf(false) }
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState())) {
        PageTitle("Settings")
        Spacer(Modifier.height(12.dp))
        Text(if (s.demo) "Demo mode" else "Bridge ${s.ip.orEmpty()}", color = TextMain, fontSize = 24.sp, fontWeight = FontWeight.Medium)
        Spacer(Modifier.height(14.dp))
        TvButton(
            when { confirm -> "Press again to confirm"; s.demo -> "Leave demo mode"; else -> "Forget this bridge" },
            Modifier.focusRequester(entry), width = 420,
        ) { if (confirm) vm.forget() else confirm = true }
        Spacer(Modifier.height(24.dp))
        Dim("Lumivane for Fire TV ${BuildConfig.VERSION_NAME}", 18)
        Spacer(Modifier.height(8.dp))
        Dim("Privacy: the app talks only to your Hue Bridge, over HTTPS pinned to the bridge certificate, and streams colors to it over DTLS. The access keys are stored encrypted on this TV. No accounts, no analytics. The screen is captured at low resolution and processed on this TV only; no picture is stored or sent. The optional online lookup is the only request that leaves your network.", 15)
    }
}


// Areas

@Composable
internal fun AreasPage(vm: AppModel, s: UiState, entry: FocusRequester) {
    val ed = s.editor
    if (ed != null) EditorView(vm, ed, entry) else AreaList(vm, s, entry)
}

@Composable
private fun AreaList(vm: AppModel, s: UiState, entry: FocusRequester) {
    Column(Modifier.fillMaxSize().verticalScroll(rememberScrollState()).padding(bottom = 40.dp)) {
        PageTitle("Sync areas", "An area is the set of lights that follow the picture, and where each sits around the screen.")
        ErrorLine(s.syncError ?: s.error)
        Spacer(Modifier.height(12.dp))
        s.areas.forEachIndexed { i, a ->
            val inUse = s.area?.id == a.id
            Row(verticalAlignment = Alignment.CenterVertically) {
                Row(
                    (if (i == 0) Modifier.focusRequester(entry) else Modifier).weight(1f)
                        .tvClickable(selected = inUse, onClick = { vm.useArea(a.id) }).padding(horizontal = 18.dp, vertical = 14.dp),
                    verticalAlignment = Alignment.CenterVertically,
                ) {
                    Column(Modifier.weight(1f)) {
                        Text(a.name, color = TextMain, fontSize = 22.sp, fontWeight = FontWeight.Medium, maxLines = 1)
                        Dim("${a.channels.size} lights", 14)
                    }
                    if (inUse) Text("In use", color = Accent, fontSize = 18.sp)
                }
                Spacer(Modifier.width(10.dp))
                Box(Modifier.width(110.dp).tvClickable(onClick = { vm.openEditor(a.id) }).padding(vertical = 18.dp), contentAlignment = Alignment.Center) {
                    Text("Edit", color = TextMain, fontSize = 20.sp)
                }
            }
            Spacer(Modifier.height(10.dp))
        }
        TvButton("New area", if (s.areas.isEmpty()) Modifier.focusRequester(entry) else Modifier, width = 300) { vm.openEditor(null) }
        Spacer(Modifier.height(12.dp))
        Dim("Press OK on an area to use it for sync.", 15)
    }
}

@Composable
internal fun EditorView(vm: AppModel, ed: Editor, entry: FocusRequester) {
    var confirm by remember { mutableStateOf(false) }
    LaunchedEffect(Unit) { runCatching { entry.requestFocus() } }
    Column(Modifier.fillMaxSize()) {
        PageTitle(if (ed.areaId == null) "New area" else ed.name, "OK adds or removes a light. Right moves to its position. Back cancels.")
        ed.message?.let { ErrorLine(it) }
        Spacer(Modifier.height(10.dp))
        Row(horizontalArrangement = Arrangement.spacedBy(10.dp)) {
            TvButton(if (ed.saving) "Saving..." else "Save", Modifier.focusRequester(entry), width = 150) { if (!ed.saving) vm.saveEditor() }
            if (ed.areaId != null) {
                TvButton(if (confirm) "Press again" else "Delete", width = 170) { if (confirm) vm.deleteEditorArea() else confirm = true }
            }
            TvButton("Cancel", width = 150, onClick = vm::closeEditor)
        }
        Spacer(Modifier.height(10.dp))
        if (ed.loading) Dim("Loading lights...", 20) else LazyColumn(verticalArrangement = Arrangement.spacedBy(8.dp), contentPadding = PaddingValues(bottom = 40.dp)) {
            itemsIndexed(ed.lights, key = { _, l -> l.light.lightId }) { i, l ->
                Row(verticalAlignment = Alignment.CenterVertically) {
                    Row(
                        Modifier.weight(1f).tvClickable(selected = l.member, onClick = { vm.toggleMember(i) }).padding(horizontal = 16.dp, vertical = 12.dp),
                        verticalAlignment = Alignment.CenterVertically,
                    ) {
                        Box(Modifier.size(22.dp).border(2.dp, if (l.member) Accent else TextDim, RoundedCornerShape(5.dp)).background(if (l.member) Accent else Color.Transparent, RoundedCornerShape(5.dp)))
                        Spacer(Modifier.width(14.dp))
                        Text(l.light.name, color = if (l.member) TextMain else TextDim, fontSize = 20.sp, maxLines = 1)
                    }
                    Spacer(Modifier.width(8.dp))
                    Box(Modifier.width(190.dp).tvClickable(onClick = { vm.cycleCell(i) }).padding(vertical = 12.dp), contentAlignment = Alignment.Center) {
                        Text(Cell.all[l.cell].label, color = if (l.member) Accent else TextDim, fontSize = 18.sp, maxLines = 1)
                    }
                }
            }
        }
    }
}
