package com.lumivane.firetv

import org.json.JSONArray
import org.json.JSONObject
import kotlin.math.roundToInt

data class Light(
    val id: String,
    val name: String,
    val on: Boolean,
    val brightness: Int,
    val dimmable: Boolean = true,
    val owner: String? = null,
)

data class Group(
    val id: String,
    val name: String,
    val zone: Boolean,
    val groupedLightId: String?,
    val lightIds: List<String>,
    val on: Boolean,
    val brightness: Int,
)

data class SceneInfo(val id: String, val name: String, val groupId: String?)

data class Snapshot(val lights: List<Light>, val groups: List<Group>, val scenes: List<SceneInfo>) {

    fun normalized(): Snapshot = copy(groups = groups.map { g ->
        g.copy(on = lights.any { it.id in g.lightIds && it.on })
    })

    fun withLight(id: String, on: Boolean? = null, brightness: Int? = null): Snapshot =
        copy(lights = lights.map {
            if (it.id == id) it.copy(on = on ?: it.on, brightness = brightness ?: it.brightness) else it
        }).normalized()

    fun withGroup(id: String, on: Boolean? = null, brightness: Int? = null): Snapshot {
        val g = groups.firstOrNull { it.id == id } ?: return this
        return copy(
            lights = lights.map {
                if (it.id in g.lightIds) it.copy(on = on ?: it.on, brightness = brightness ?: it.brightness) else it
            },
            groups = groups.map { if (it.id == id) it.copy(brightness = brightness ?: it.brightness) else it },
        ).normalized()
    }
}

interface HueApi {
    suspend fun snapshot(): Snapshot
    suspend fun setLight(id: String, on: Boolean? = null, brightness: Int? = null)
    suspend fun setGroup(groupedLightId: String, on: Boolean? = null, brightness: Int? = null)
    suspend fun recall(sceneId: String)
}

class HueException(message: String) : Exception(message)

fun stepBrightness(current: Int, delta: Int): Int = (current + delta).coerceIn(1, 100)

private val IPV4 = Regex("""^((25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)\.){3}(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)$""")
fun isIPv4(s: String): Boolean = IPV4.matches(s)

fun hueBody(on: Boolean?, brightness: Int?): String = JSONObject().apply {
    on?.let { put("on", JSONObject().put("on", it)) }
    brightness?.let { put("dimming", JSONObject().put("brightness", it.toDouble())) }
}.toString()

internal fun JSONArray.objects(): List<JSONObject> = (0 until length()).mapNotNull { optJSONObject(it) }

private fun JSONObject.rid(): String = optString("rid")

fun parseSnapshot(
    lights: JSONArray,
    rooms: JSONArray,
    zones: JSONArray,
    scenes: JSONArray,
    grouped: JSONArray,
): Snapshot {
    val ls = lights.objects().map { o ->
        val dim = o.optJSONObject("dimming")
        Light(
            id = o.getString("id"),
            name = o.optJSONObject("metadata")?.optString("name").orEmpty().ifBlank { "Light" },
            on = o.optJSONObject("on")?.optBoolean("on") ?: false,
            brightness = dim?.optDouble("brightness", 100.0)?.roundToInt() ?: 100,
            dimmable = dim != null,
            owner = o.optJSONObject("owner")?.rid(),
        )
    }.sortedBy { it.name.lowercase() }

    val byId = ls.associateBy { it.id }
    val byOwner = ls.filter { it.owner != null }.associateBy { it.owner!! }
    val glById = grouped.objects().associateBy { it.getString("id") }

    fun group(o: JSONObject, zone: Boolean): Group {
        val ids = o.optJSONArray("children")?.objects().orEmpty().mapNotNull { c ->
            when (c.optString("rtype")) {
                "light" -> byId[c.rid()]?.id
                "device" -> byOwner[c.rid()]?.id
                else -> null
            }
        }.distinct()
        val glId = o.optJSONArray("services")?.objects().orEmpty()
            .firstOrNull { it.optString("rtype") == "grouped_light" }?.rid()
        val gl = glId?.let { glById[it] }
        val members = ids.mapNotNull { byId[it] }
        val level = gl?.optJSONObject("dimming")?.optDouble("brightness", Double.NaN)
            ?.takeUnless { it.isNaN() }?.roundToInt()
            ?: members.filter { it.on }.maxOfOrNull { it.brightness }
            ?: members.maxOfOrNull { it.brightness } ?: 100
        return Group(
            id = o.getString("id"),
            name = o.optJSONObject("metadata")?.optString("name").orEmpty().ifBlank { "Room" },
            zone = zone,
            groupedLightId = glId,
            lightIds = ids,
            on = members.any { it.on },
            brightness = level,
        )
    }

    val groups = rooms.objects().map { group(it, false) }.sortedBy { it.name.lowercase() } +
        zones.objects().map { group(it, true) }.sortedBy { it.name.lowercase() }

    val sc = scenes.objects().map {
        SceneInfo(
            id = it.getString("id"),
            name = it.optJSONObject("metadata")?.optString("name").orEmpty().ifBlank { "Scene" },
            groupId = it.optJSONObject("group")?.rid(),
        )
    }
    return Snapshot(ls, groups.filter { it.lightIds.isNotEmpty() }, sc)
}

sealed interface PairResult {
    data class Paired(val key: String, val pin: String, val clientKey: String?) : PairResult
    data object NotPressed : PairResult
    data class Failed(val message: String) : PairResult
}

fun parsePairReply(text: String, pin: String?): PairResult = try {
    val first = JSONArray(text).optJSONObject(0)
    val ok = first?.optJSONObject("success")
    val err = first?.optJSONObject("error")
    when {
        ok != null && pin != null -> PairResult.Paired(ok.getString("username"), pin, ok.optString("clientkey").ifBlank { null })
        ok != null -> PairResult.Failed("The bridge did not present a certificate.")
        err?.optInt("type") == 101 -> PairResult.NotPressed
        else -> PairResult.Failed(err?.optString("description")?.ifBlank { null } ?: "Unexpected reply from the bridge.")
    }
} catch (e: org.json.JSONException) {
    PairResult.Failed("Unexpected reply from the bridge.")
}
