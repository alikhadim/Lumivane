package com.lumivane.firetv

import android.app.Application

/** Records the last uncaught exception so the next launch can show it. */
class LumivaneApp : Application() {
    override fun onCreate() {
        super.onCreate()
        val prev = Thread.getDefaultUncaughtExceptionHandler()
        Thread.setDefaultUncaughtExceptionHandler { t, e ->
            runCatching { Store(this).saveCrash(formatCrash(t.name, e)) }
            prev?.uncaughtException(t, e)
        }
    }
}

fun formatCrash(thread: String, e: Throwable): String {
    val ours = e.stackTrace.filter { it.className.startsWith("com.lumivane") }.take(4)
    val top = e.stackTrace.take(2)
    val frames = (top + ours).distinct().joinToString("\n") { "  at ${it.className.substringAfterLast('.')}.${it.methodName}:${it.lineNumber}" }
    val cause = generateSequence(e.cause) { it.cause }.lastOrNull()
    return buildString {
        append("[$thread] ${e.javaClass.name}: ${e.message}\n$frames")
        if (cause != null) append("\nCaused by ${cause.javaClass.name}: ${cause.message}")
    }.take(900)
}
