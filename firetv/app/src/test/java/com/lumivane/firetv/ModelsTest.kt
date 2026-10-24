package com.lumivane.firetv

import kotlinx.coroutines.runBlocking
import org.json.JSONArray
import org.json.JSONObject
import org.junit.Assert.assertEquals
import org.junit.Assert.assertFalse
import org.junit.Assert.assertNull
import org.junit.Assert.assertTrue
import org.junit.Test

class ModelsTest {
    private val lights = JSONArray("""[
      {"id":"L1","owner":{"rid":"D1","rtype":"device"},"metadata":{"name":"Bar Left"},"on":{"on":true},"dimming":{"brightness":80.4}},
      {"id":"L2","owner":{"rid":"D2","rtype":"device"},"metadata":{"name":"Desk"},"on":{"on":false},"dimming":{"brightness":30.0}},
      {"id":"L3","owner":{"rid":"D3","rtype":"device"},"metadata":{"name":"Plug"},"on":{"on":true}}
    ]""")
    private val rooms = JSONArray("""[
      {"id":"R1","metadata":{"name":"Living"},"children":[{"rid":"D1","rtype":"device"},{"rid":"D3","rtype":"device"}],"services":[{"rid":"G1","rtype":"grouped_light"}]},
      {"id":"R2","metadata":{"name":"Empty"},"children":[],"services":[]}
    ]""")
    private val zones = JSONArray("""[
      {"id":"Z1","metadata":{"name":"Desk zone"},"children":[{"rid":"L2","rtype":"light"}],"services":[{"rid":"G2","rtype":"grouped_light"}]}
    ]""")
    private val scenes = JSONArray("""[{"id":"S1","metadata":{"name":"Relax"},"group":{"rid":"R1","rtype":"room"}}]""")
    private val grouped = JSONArray("""[
      {"id":"G1","on":{"on":true},"dimming":{"brightness":55.0}},
      {"id":"G2","on":{"on":false}}
    ]""")

    private val snap get() = parseSnapshot(lights, rooms, zones, scenes, grouped)

    @Test fun parsesLights() {
        val l = snap.lights.associateBy { it.id }
        assertEquals(80, l.getValue("L1").brightness)
        assertTrue(l.getValue("L1").on)
        assertFalse(l.getValue("L3").dimmable)
        assertEquals(3, l.size)
    }

    @Test fun mapsRoomsThroughDevices() {
        val g = snap.groups
        assertEquals(listOf("Living", "Desk zone"), g.map { it.name })
        assertEquals(listOf("L1", "L3"), g[0].lightIds)
        assertEquals("G1", g[0].groupedLightId)
        assertEquals(55, g[0].brightness)
        assertFalse(g[0].zone)
        assertTrue(g[1].zone)
    }

    @Test fun zoneWithoutGroupLevelFallsBackToMembers() {
        val z = snap.groups[1]
        assertFalse(z.on)
        assertEquals(30, z.brightness)
    }

    @Test fun dropsEmptyRoomsAndKeepsScenes() {
        assertTrue(snap.groups.none { it.id == "R2" })
        assertEquals("R1", snap.scenes.single().groupId)
    }

    @Test fun withGroupUpdatesMembersAndOtherGroups() {
        val s = snap.withGroup("R1", on = false)
        assertTrue(s.lights.filter { it.id in listOf("L1", "L3") }.none { it.on })
        assertFalse(s.groups[0].on)
    }

    @Test fun withLightRecomputesGroupState() {
        val s = snap.withLight("L2", on = true, brightness = 90)
        assertTrue(s.groups[1].on)
        assertEquals(90, s.lights.first { it.id == "L2" }.brightness)
    }

    @Test fun brightnessIsClamped() {
        assertEquals(1, stepBrightness(5, -10))
        assertEquals(100, stepBrightness(95, 10))
        assertEquals(50, stepBrightness(40, 10))
    }

    @Test fun validatesIPv4() {
        assertTrue(isIPv4("192.168.1.20"))
        assertTrue(isIPv4("192.168.0.1"))
        assertFalse(isIPv4("192.168.1"))
        assertFalse(isIPv4("256.1.1.1"))
        assertFalse(isIPv4("192.168.01.1"))
        assertFalse(isIPv4("a.b.c.d"))
    }

    @Test fun buildsRequestBodies() {
        val b = JSONObject(hueBody(true, 40))
        assertTrue(b.getJSONObject("on").getBoolean("on"))
        assertEquals(40.0, b.getJSONObject("dimming").getDouble("brightness"), 0.0)
        assertFalse(JSONObject(hueBody(false, null)).has("dimming"))
        assertEquals("{}", hueBody(null, null))
    }

    @Test fun parsesPairReplies() {
        assertEquals(PairResult.Paired("abc", "ff", "k"), parsePairReply("""[{"success":{"username":"abc","clientkey":"k"}}]""", "ff"))
        assertEquals(PairResult.Paired("abc", "ff", null), parsePairReply("""[{"success":{"username":"abc"}}]""", "ff"))
        assertEquals(PairResult.NotPressed, parsePairReply("""[{"error":{"type":101,"description":"link button not pressed"}}]""", "ff"))
        assertTrue(parsePairReply("""[{"error":{"type":7,"description":"bad"}}]""", null) is PairResult.Failed)
        assertTrue(parsePairReply("not json", null) is PairResult.Failed)
        assertTrue(parsePairReply("""[{"success":{"username":"abc"}}]""", null) is PairResult.Failed)
    }

    @Test fun demoApiControlsLightsAndScenes() = runBlocking {
        val api = DemoApi()
        val s0 = api.snapshot()
        assertTrue(s0.groups.first { it.name == "Living Room" }.on)
        api.setGroup("gl-g1", on = false)
        assertTrue(api.snapshot().lights.filter { it.id in listOf("l1", "l2", "l3") }.none { it.on })
        api.recall("s-g3-Movie Night")
        val bed = api.snapshot()
        assertTrue(bed.groups.first { it.name == "Bedroom" }.on)
        assertEquals(15, bed.lights.first { it.id == "l6" }.brightness)
        api.setLight("l4", brightness = 20)
        assertEquals(20, api.snapshot().lights.first { it.id == "l4" }.brightness)
        assertNull(api.snapshot().groups.firstOrNull { it.name == "Missing" })
    }

    @Test fun bridgeReasonReadsDescription() {
        assertEquals(": invalid value", bridgeReason("""{"errors":[{"description":"invalid value"}],"data":[]}"""))
        assertEquals("", bridgeReason("not json"))
        assertEquals("", bridgeReason("""{"errors":[],"data":[]}"""))
    }
}
