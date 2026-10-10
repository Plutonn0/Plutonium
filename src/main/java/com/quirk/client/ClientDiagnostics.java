package com.quirk.client;

import java.util.Map;
import java.util.concurrent.ConcurrentHashMap;

/** Local, rate-limited diagnostics. Never include tokens, settings values or server text. */
final class ClientDiagnostics {
    private static final System.Logger LOG = System.getLogger("Plutonium");
    private static final long WARNING_INTERVAL = 60_000_000_000L;
    private static final Map<String, Long> lastWarnings = new ConcurrentHashMap<>();

    private ClientDiagnostics() {}

    static void warn(String operation, Throwable error) {
        long now = System.nanoTime();
        lastWarnings.compute(
                operation,
                (key, previous) -> {
                    if (previous == null || now - previous >= WARNING_INTERVAL) {
                        LOG.log(
                                System.Logger.Level.WARNING,
                                operation + " (" + error.getClass().getSimpleName() + ")");
                        return now;
                    }
                    return previous;
                });
    }
}
