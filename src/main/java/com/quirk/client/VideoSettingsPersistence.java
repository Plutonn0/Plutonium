package com.quirk.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;

/**
 * Save changed video preferences even if a settings screen or third-party UI omits its normal save.
 */
public final class VideoSettingsPersistence {
    private static Options owner;
    private static Object session;
    private static int render, simulation, fps;
    private static long changedAt;

    private VideoSettingsPersistence() {}

    public static void tick() {
        var mc = Minecraft.getInstance();
        Options options = mc.options;
        if (options == null) return;
        var connection = mc.getConnection();
        boolean started = owner != options;
        boolean joined = mc.level != null && connection != null && connection != session;
        if (connection == null) session = null;
        if (started || joined) {
            owner = options;
            if (joined) session = connection;
            // Apply once per launch/connection, not every frame or dimension change.
            options.renderDistance().set(32);
            options.simulationDistance().set(24);
            options.save();
            capture(options);
            changedAt = 0;
            return;
        }
        if (render != options.renderDistance().get()
                || simulation != options.simulationDistance().get()
                || fps != options.framerateLimit().get()) {
            capture(options);
            changedAt = System.nanoTime();
        }
        if (changedAt != 0 && System.nanoTime() - changedAt >= 500_000_000L) {
            options.save();
            changedAt = 0;
        }
    }

    public static void saveOnExit() {
        Options options = Minecraft.getInstance().options;
        if (options != null) {
            options.save();
            capture(options);
            changedAt = 0;
        }
    }

    private static void capture(Options options) {
        render = options.renderDistance().get();
        simulation = options.simulationDistance().get();
        fps = options.framerateLimit().get();
    }
}
