package com.lumivane.firetv

import org.junit.Assert.assertTrue
import org.junit.Assume.assumeTrue
import org.junit.Test

/** Runs only when DTLS_INTEROP_PORT is set: connects to a real DTLS-PSK server (openssl s_server) over UDP. */
class InteropTest {
    @Test fun clientHandshakesWithOpenSsl() {
        val port = System.getenv("DTLS_INTEROP_PORT")?.toIntOrNull()
        assumeTrue(port != null)
        val key = hexToBytes("000102030405060708090a0b0c0d0e0f")!!
        val link = HueDtls.connectUdp("127.0.0.1", port!!, "hue-app-id", key)
        val pkt = StreamPacket.build("12345678-1234-1234-1234-123456789abc", 1, mapOf(0 to RGB(1f, 0f, 0f)))!!
        repeat(5) { link.send(pkt, 0, pkt.size); Thread.sleep(20) }
        link.close()
        assertTrue(true)
    }
}
