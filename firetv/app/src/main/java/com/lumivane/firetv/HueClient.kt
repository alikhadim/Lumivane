package com.lumivane.firetv

import kotlinx.coroutines.Dispatchers
import kotlinx.coroutines.async
import kotlinx.coroutines.coroutineScope
import kotlinx.coroutines.withContext
import org.json.JSONArray
import org.json.JSONException
import org.json.JSONObject
import java.io.IOException
import java.net.URL
import java.security.MessageDigest
import java.security.cert.CertificateException
import java.security.cert.X509Certificate
import javax.net.ssl.HostnameVerifier
import javax.net.ssl.HttpsURLConnection
import javax.net.ssl.SSLContext
import javax.net.ssl.SSLException
import javax.net.ssl.SSLSocketFactory
import javax.net.ssl.X509TrustManager

/** The bridge uses a self-signed certificate, so trust is pinned to its SHA-256 fingerprint, recorded when pairing. */
object Tls {
    fun fingerprint(cert: X509Certificate): String =
        MessageDigest.getInstance("SHA-256").digest(cert.encoded).joinToString("") { "%02x".format(it) }

    fun factory(expected: String?, onSeen: (String) -> Unit = {}): SSLSocketFactory {
        val tm = object : X509TrustManager {
            override fun checkClientTrusted(chain: Array<X509Certificate>, authType: String) = throw CertificateException()
            override fun checkServerTrusted(chain: Array<X509Certificate>, authType: String) {
                val fp = fingerprint(chain[0])
                onSeen(fp)
                if (expected != null && fp != expected) {
                    throw CertificateException("The bridge certificate changed. Forget the bridge in Settings and pair again.")
                }
            }
            override fun getAcceptedIssuers(): Array<X509Certificate> = emptyArray()
        }
        return SSLContext.getInstance("TLS").apply { init(null, arrayOf(tm), null) }.socketFactory
    }
}

class Reply(val code: Int, val text: String, val headers: Map<String, String>) {
    operator fun component1() = code
    operator fun component2() = text
}

object Http {
    fun request(ip: String, method: String, path: String, body: String?, factory: SSLSocketFactory, key: String?): Reply {
        val c = URL("https://$ip$path").openConnection() as HttpsURLConnection
        try {
            c.sslSocketFactory = factory
            c.hostnameVerifier = HostnameVerifier { host, _ -> host == ip }
            c.connectTimeout = 5_000
            c.readTimeout = 8_000
            c.requestMethod = method
            key?.let { c.setRequestProperty("hue-application-key", it) }
            if (body != null) {
                c.doOutput = true
                c.setRequestProperty("Content-Type", "application/json")
                c.outputStream.use { it.write(body.toByteArray()) }
            }
            val code = c.responseCode
            val text = (if (code < 400) c.inputStream else c.errorStream)?.bufferedReader()?.use { it.readText() }.orEmpty()
            val headers = c.headerFields.filterKeys { it != null }.entries.associate { it.key.lowercase() to (it.value.firstOrNull() ?: "") }
            return Reply(code, text, headers)
        } catch (e: SSLException) {
            val cert = generateSequence<Throwable>(e) { it.cause }.firstOrNull { it is CertificateException }
            throw HueException(cert?.message ?: "Secure connection to the bridge failed.")
        } catch (e: IOException) {
            throw HueException("Cannot reach the bridge at $ip.")
        }
    }
}

/** The part of the bridge connection that sync needs; a seam for tests. */
interface Bridge {
    suspend fun call(method: String, path: String, body: String? = null): String
    suspend fun data(resource: String): JSONArray
    suspend fun applicationId(): String
}

/** The bridge's own explanation from an error reply, as ": text", or empty. */
internal fun bridgeReason(body: String): String = try {
    val d = JSONObject(body).optJSONArray("errors")?.optJSONObject(0)?.optString("description").orEmpty().trim()
    if (d.isEmpty()) "" else ": " + d.take(160)
} catch (e: Exception) { "" }

class HueClient(private val ip: String, internal val key: String, pin: String) : HueApi, Bridge {
    internal val factory = Tls.factory(pin)

    override suspend fun call(method: String, path: String, body: String?): String = withContext(Dispatchers.IO) {
        val (code, text) = Http.request(ip, method, "/clip/v2/resource/$path", body, factory, key)
        when {
            code == 403 -> throw HueException("The bridge rejected the saved key. Forget the bridge in Settings and pair again.")
            code >= 400 -> throw HueException("The bridge returned error $code${bridgeReason(text)}.")
        }
        text
    }

    override suspend fun applicationId(): String = withContext(Dispatchers.IO) {
        Http.request(ip, "GET", "/auth/v1", null, factory, key).headers["hue-application-id"]
            ?: throw HueException("The bridge did not return an application id.")
    }

    override suspend fun data(resource: String): JSONArray = try {
        JSONObject(call("GET", resource)).optJSONArray("data") ?: JSONArray()
    } catch (e: JSONException) {
        throw HueException("Unexpected reply from the bridge.")
    }

    override suspend fun snapshot(): Snapshot = coroutineScope {
        val l = async { data("light") }
        val r = async { data("room") }
        val z = async { data("zone") }
        val s = async { data("scene") }
        val g = async { data("grouped_light") }
        try {
            parseSnapshot(l.await(), r.await(), z.await(), s.await(), g.await())
        } catch (e: JSONException) {
            throw HueException("Unexpected reply from the bridge.")
        }
    }

    override suspend fun setLight(id: String, on: Boolean?, brightness: Int?) {
        call("PUT", "light/$id", hueBody(on, brightness))
    }

    override suspend fun setGroup(groupedLightId: String, on: Boolean?, brightness: Int?) {
        call("PUT", "grouped_light/$groupedLightId", hueBody(on, brightness))
    }

    override suspend fun recall(sceneId: String) {
        call("PUT", "scene/$sceneId", """{"recall":{"action":"active"}}""")
    }
}

object Pairing {
    suspend fun attempt(ip: String): PairResult = withContext(Dispatchers.IO) {
        var seen: String? = null
        try {
            val (_, text) = Http.request(
                ip, "POST", "/api",
                """{"devicetype":"lumivane#firetv","generateclientkey":true}""",
                Tls.factory(null) { seen = it }, null,
            )
            parsePairReply(text, seen)
        } catch (e: HueException) {
            PairResult.Failed(e.message ?: "Pairing failed.")
        }
    }
}
