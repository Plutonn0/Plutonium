package com.quirk.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.Options;

/** Save changed video preferences even if a settings screen or third-party UI omits its normal save. */
public final class VideoSettingsPersistence {
    private static Options owner;
    private static int render,simulation,fps;
    private static long changedAt;
    private VideoSettingsPersistence() {}
    public static void tick() {
        Options options=Minecraft.getInstance().options;
        if(options==null)return;
        if(owner!=options){owner=options;capture(options);return;}
        if(render!=options.renderDistance().get()||simulation!=options.simulationDistance().get()||fps!=options.framerateLimit().get()) {
            capture(options);changedAt=System.nanoTime();
        }
        if(changedAt!=0&&System.nanoTime()-changedAt>=500_000_000L){options.save();changedAt=0;}
    }
    public static void saveOnExit() {
        Options options=Minecraft.getInstance().options;
        if(options!=null){options.save();capture(options);changedAt=0;}
    }
    private static void capture(Options options){render=options.renderDistance().get();simulation=options.simulationDistance().get();fps=options.framerateLimit().get();}
}
