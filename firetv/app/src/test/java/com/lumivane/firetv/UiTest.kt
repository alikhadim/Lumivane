package com.lumivane.firetv

import android.content.Context
import androidx.compose.ui.graphics.asAndroidBitmap
import androidx.compose.ui.test.captureToImage
import androidx.compose.ui.test.junit4.createAndroidComposeRule
import androidx.compose.ui.test.onAllNodesWithText
import androidx.compose.ui.test.onNodeWithText
import androidx.compose.ui.test.onRoot
import androidx.compose.ui.test.performClick
import androidx.lifecycle.ViewModelProvider
import androidx.test.core.app.ApplicationProvider
import org.json.JSONArray
import org.json.JSONObject
import org.junit.After
import org.junit.Rule
import org.junit.Test
import org.junit.runner.RunWith
import org.robolectric.RobolectricTestRunner
import org.robolectric.annotation.Config
import org.robolectric.annotation.GraphicsMode
import java.io.File

/** Fake bridge with data shaped like a real Hue v2 bridge: 7 lights, 4 entertainment areas. */
private class FakeBridge : Bridge {
    private val names = listOf("Bedroom light", "Hue light 1", "Hue light 2", "Hue lightstrip plus", "Desk lamp", "Floor lamp", "Bloom")
    private fun light(i: Int) = JSONObject("""{"id":"L$i","id_v1":"/lights/$i","owner":{"rid":"D$i","rtype":"device"},
      "metadata":{"name":"${names[i]}","archetype":"sultan_bulb"},"on":{"on":false},"dimming":{"brightness":100.0,"min_dim_level":0.2},
      "color":{"xy":{"x":0.4,"y":0.4},"gamut":{"red":{"x":0.6915,"y":0.3083},"green":{"x":0.17,"y":0.7},"blue":{"x":0.1532,"y":0.0475}},"gamut_type":"C"}}""")
    private fun device(i: Int) = JSONObject("""{"id":"D$i","services":[{"rid":"L$i","rtype":"light"},{"rid":"E$i","rtype":"entertainment"},{"rid":"Z$i","rtype":"zigbee_connectivity"}],"metadata":{"name":"${names[i]}"}}""")
    private fun area(id: String, name: String, lights: List<Int>) = JSONObject().put("id", id).put("metadata", JSONObject().put("name", name))
        .put("status", "inactive").put("configuration_type", "screen")
        .put("channels", JSONArray(lights.mapIndexed { ch, l ->
            JSONObject("""{"channel_id":$ch,"position":{"x":${-0.8 + ch * 0.5},"y":0.8,"z":${0.3 - ch * 0.4}},"members":[{"service":{"rid":"E$l","rtype":"entertainment"},"index":0}]}""")
        }))

    val calls = mutableListOf<String>()
    override suspend fun call(method: String, path: String, body: String?): String { calls += "$method $path ${body ?: ""}"; return "{}" }
    override suspend fun applicationId() = "app-id"
    override suspend fun data(resource: String): JSONArray = when (resource) {
        "light" -> JSONArray(names.indices.map(::light))
        "device" -> JSONArray(names.indices.map(::device) + JSONObject("""{"id":"BR","services":[{"rid":"X","rtype":"bridge"}]}"""))
        "entertainment_configuration" -> JSONArray(listOf(
            area("00000000-0000-0000-0000-00000000000a", "Bedroom chill", listOf(0, 1)),
            area("00000000-0000-0000-0000-00000000000b", "PC", listOf(4, 5)),
            area("00000000-0000-0000-0000-00000000000c", "Reading", listOf(1, 2, 3)),
            area("00000000-0000-0000-0000-00000000000d", "TV", listOf(3, 4, 5, 6)),
        ))
        else -> JSONArray()
    }
}

@RunWith(RobolectricTestRunner::class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@Config(sdk = [34], qualifiers = "w960dp-h540dp-xhdpi")
class UiTest {
    @get:Rule val rule = createAndroidComposeRule<MainActivity>()

    private val out = File(System.getProperty("shots") ?: "/tmp/shots").apply { mkdirs() }

    private fun shot(name: String) {
        rule.waitForIdle()
        runCatching {
            val bmp = rule.onRoot().captureToImage().asAndroidBitmap()
            File(out, "$name.png").outputStream().use { bmp.compress(android.graphics.Bitmap.CompressFormat.PNG, 100, it) }
        }
    }

    @After fun stopSync() { SyncHub.stop(); Thread.sleep(300); SyncHub.status.value = SyncStatus() }

    private fun settle() { Thread.sleep(400); rule.waitForIdle() }

    private fun startRealisticApp(): FakeBridge {
        val vm = ViewModelProvider(rule.activity)[AppModel::class.java]
        val bridge = FakeBridge()
        rule.runOnUiThread { vm.injectForTest(DemoApi(), HueSync(bridge, "192.168.0.2", "00112233445566778899aabbccddeeff")) }
        settle()
        return bridge
    }

    @Test fun everyAreaOpensInTheEditor() {
        val bridge = startRealisticApp()
        shot("1-home")
        rule.onNodeWithText("Areas").performClick(); settle()
        shot("2-areas")
        for (name in listOf("Bedroom chill", "PC", "Reading", "TV")) {
            val idx = listOf("Bedroom chill", "PC", "Reading", "TV").indexOf(name)
            rule.onAllNodesWithText("Edit")[idx].performClick(); settle()
            rule.onNodeWithText("Save").assertExists()
            rule.onNodeWithText("Bloom").assertExists()
            if (name == "PC") shot("3-editor-pc")
            rule.onNodeWithText("Cancel").performClick(); settle()
        }
        rule.onAllNodesWithText("Edit")[1].performClick(); settle()
        rule.onNodeWithText("Hue light 1").performClick(); settle()
        rule.onNodeWithText("Save").performClick(); settle()
        rule.onNodeWithText("New area").assertExists()
        val put = bridge.calls.first { it.startsWith("PUT entertainment_configuration/00000000-0000-0000-0000-00000000000b") }
        check("service_locations" in put && "light_services" !in put && "\"channels\"" !in put) { put }
        // The fake bridge ignores the update, so the app falls back to replacing the area.
        check(bridge.calls.any { it.startsWith("DELETE entertainment_configuration/00000000-0000-0000-0000-00000000000b") }) { bridge.calls.toString() }
        check(bridge.calls.any { it.startsWith("POST entertainment_configuration") }) { bridge.calls.toString() }
    }

    @Test fun editWhileSyncRunsWithLiveColors() {
        startRealisticApp()
        val vm = ViewModelProvider(rule.activity)[AppModel::class.java]
        rule.runOnUiThread { vm.useArea("00000000-0000-0000-0000-00000000000b") }
        SyncHub.status.value = SyncStatus(SyncState.Running, label = "Screen")
        val ticker = Thread {
            var i = 0
            while (!Thread.currentThread().isInterrupted) {
                SyncHub.live.value = (0..3).associateWith { RGB((i * 7 + it * 40) % 255 / 255f, 0.3f, 0.6f) }
                i++
                runCatching { Thread.sleep(80) }.onFailure { return@Thread }
            }
        }.apply { start() }
        try {
            rule.onNodeWithText("Areas").performClick(); settle()
            for (k in 0 until 4) {
                rule.onAllNodesWithText("Edit")[k].performClick(); settle()
                rule.onNodeWithText("Save").assertExists()
                rule.onNodeWithText("Hue light 2").performClick(); settle()
                rule.onNodeWithText("Cancel").performClick(); settle()
                rule.onNodeWithText("New area").assertExists()
            }
        } finally { ticker.interrupt() }
    }

    @Test fun newAreaAndDelete() {
        val bridge = startRealisticApp()
        rule.onNodeWithText("Areas").performClick(); settle()
        rule.onNodeWithText("New area").performClick(); settle()
        rule.onNodeWithText("Save").performClick(); settle()
        check(bridge.calls.any { it.startsWith("POST entertainment_configuration") }) { bridge.calls.toString() }
        rule.onAllNodesWithText("Edit")[0].performClick(); settle()
        rule.onNodeWithText("Delete").performClick(); settle()
        rule.onNodeWithText("Press again").performClick(); settle()
        check(bridge.calls.any { it.startsWith("DELETE entertainment_configuration/") }) { bridge.calls.toString() }
    }

    @Test fun homeStartsEffectSync() {
        startRealisticApp()
        rule.onNodeWithText("Aurora").performClick(); settle()
        rule.onNodeWithText("Start sync").performClick(); Thread.sleep(1500); rule.waitForIdle()
        shot("4-home-running")
        rule.onNodeWithText("Stop").performClick(); Thread.sleep(500); rule.waitForIdle()
    }
}
