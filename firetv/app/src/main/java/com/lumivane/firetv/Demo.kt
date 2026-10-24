package com.lumivane.firetv

class DemoApi : HueApi {
    private val presets = mapOf("Relax" to 40, "Concentrate" to 100, "Movie Night" to 15, "Energize" to 90)

    private var snap: Snapshot = run {
        val lights = listOf(
            Light("l1", "Bar Left", true, 80), Light("l2", "Bar Right", true, 80),
            Light("l3", "Floor Lamp", false, 60), Light("l4", "Desk Lamp", true, 100),
            Light("l5", "Shelf Strip", true, 45), Light("l6", "Bedside Left", false, 30),
            Light("l7", "Bedside Right", false, 30),
        )
        val rooms = listOf(
            Triple("g1", "Living Room", listOf("l1", "l2", "l3")),
            Triple("g2", "Office", listOf("l4", "l5")),
            Triple("g3", "Bedroom", listOf("l6", "l7")),
        )
        val groups = rooms.map { (id, name, ids) ->
            Group(id, name, false, "gl-$id", ids, false, ids.mapNotNull { i -> lights.find { it.id == i }?.brightness }.max())
        }
        val scenes = rooms.flatMap { (id, _, _) -> presets.keys.map { SceneInfo("s-$id-$it", it, id) } }
        Snapshot(lights, groups, scenes).normalized()
    }

    private val lock = Any()

    override suspend fun snapshot(): Snapshot = synchronized(lock) { snap }

    override suspend fun setLight(id: String, on: Boolean?, brightness: Int?) = synchronized(lock) {
        snap = snap.withLight(id, on, brightness)
    }

    override suspend fun setGroup(groupedLightId: String, on: Boolean?, brightness: Int?) = synchronized(lock) {
        val g = snap.groups.firstOrNull { it.groupedLightId == groupedLightId } ?: return@synchronized
        snap = snap.withGroup(g.id, on, brightness)
    }

    override suspend fun recall(sceneId: String) = synchronized(lock) {
        val s = snap.scenes.firstOrNull { it.id == sceneId } ?: return@synchronized
        val g = snap.groups.firstOrNull { it.id == s.groupId } ?: return@synchronized
        snap = snap.withGroup(g.id, on = true, brightness = presets[s.name] ?: 100)
    }
}
