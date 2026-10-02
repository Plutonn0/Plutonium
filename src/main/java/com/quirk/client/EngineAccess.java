package com.quirk.client;

import net.minecraft.client.Camera;

/** Implemented by the standalone client packager, so protected engine calls are remapped normally. */
public final class EngineAccess {
    public static boolean attack(net.minecraft.client.Minecraft minecraft) { throw new AssertionError("Client integration missing"); }
    public static void position(Camera camera, double x, double y, double z) { throw new AssertionError("Client integration missing"); }
    public static void rotation(Camera camera, float yaw, float pitch) { throw new AssertionError("Client integration missing"); }
    public static float fov(net.minecraft.client.renderer.GameRenderer renderer, Camera camera, float partial) { throw new AssertionError("Client integration missing"); }
}
