package com.lumivane.firetv

import android.content.Context
import android.security.keystore.KeyGenParameterSpec
import android.security.keystore.KeyProperties
import android.util.Base64
import java.security.KeyStore
import javax.crypto.Cipher
import javax.crypto.KeyGenerator
import javax.crypto.SecretKey
import javax.crypto.spec.GCMParameterSpec

/** Bridge address, certificate pin and app key. The key is encrypted with an Android Keystore key when available. */
class Store(ctx: Context) {
    private val p = ctx.getSharedPreferences("lumivane", Context.MODE_PRIVATE)

    var ip: String?
        get() = p.getString("ip", null)
        private set(v) { p.edit().putString("ip", v).apply() }
    var pin: String?
        get() = p.getString("pin", null)
        private set(v) { p.edit().putString("pin", v).apply() }
    var demo: Boolean
        get() = p.getBoolean("demo", false)
        set(v) { p.edit().putBoolean("demo", v).apply() }

    fun save(ip: String, pin: String, key: String, clientKey: String?) {
        this.ip = ip
        this.pin = pin
        p.edit().putString("key", seal(key)).putString("ckey", clientKey?.let(::seal)).putBoolean("demo", false).apply()
    }

    fun loadKey(): String? = p.getString("key", null)?.let { open(it) }
    fun loadClientKey(): String? = p.getString("ckey", null)?.let { open(it) }

    fun getString(k: String): String? = p.getString("pref.$k", null)
    fun putString(k: String, v: String) = p.edit().putString("pref.$k", v).apply()

    fun saveCrash(text: String) { p.edit().putString("crash", text).commit() }
    fun takeCrash(): String? = p.getString("crash", null)
    fun clearCrash() { p.edit().remove("crash").apply() }

    fun clear() = p.edit().clear().apply()

    private fun secret(): SecretKey? = try {
        val ks = KeyStore.getInstance("AndroidKeyStore").apply { load(null) }
        (ks.getKey(ALIAS, null) as? SecretKey) ?: KeyGenerator.getInstance(KeyProperties.KEY_ALGORITHM_AES, "AndroidKeyStore").run {
            init(
                KeyGenParameterSpec.Builder(ALIAS, KeyProperties.PURPOSE_ENCRYPT or KeyProperties.PURPOSE_DECRYPT)
                    .setBlockModes(KeyProperties.BLOCK_MODE_GCM)
                    .setEncryptionPaddings(KeyProperties.ENCRYPTION_PADDING_NONE)
                    .build(),
            )
            generateKey()
        }
    } catch (e: Exception) {
        null
    }

    private fun seal(plain: String): String {
        val k = secret() ?: return "p:$plain"
        return try {
            val c = Cipher.getInstance(TRANSFORM).apply { init(Cipher.ENCRYPT_MODE, k) }
            val ct = c.doFinal(plain.toByteArray())
            "e:" + Base64.encodeToString(c.iv, Base64.NO_WRAP) + ":" + Base64.encodeToString(ct, Base64.NO_WRAP)
        } catch (e: Exception) {
            "p:$plain"
        }
    }

    private fun open(stored: String): String? = try {
        when {
            stored.startsWith("p:") -> stored.removePrefix("p:")
            stored.startsWith("e:") -> {
                val (iv, ct) = stored.removePrefix("e:").split(":")
                val c = Cipher.getInstance(TRANSFORM)
                c.init(Cipher.DECRYPT_MODE, secret(), GCMParameterSpec(128, Base64.decode(iv, Base64.NO_WRAP)))
                String(c.doFinal(Base64.decode(ct, Base64.NO_WRAP)))
            }
            else -> null
        }
    } catch (e: Exception) {
        null
    }

    private companion object {
        const val ALIAS = "lumivane.appkey"
        const val TRANSFORM = "AES/GCM/NoPadding"
    }
}
