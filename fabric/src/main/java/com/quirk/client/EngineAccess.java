package com.quirk.client;

import com.quirk.client.fabric.mixin.CameraAccess;
import com.quirk.client.fabric.mixin.GameRendererAccessor;
import net.minecraft.client.Camera;
import net.minecraft.client.renderer.GameRenderer;

public final class EngineAccess {
    public static boolean attack(net.minecraft.client.Minecraft minecraft) { return ((com.quirk.client.fabric.mixin.MinecraftAccess)minecraft).quirk$attack(); }
    private EngineAccess() {}

    public static void position(Camera camera, double x, double y, double z) {
        ((CameraAccess) camera).quirk$setPosition(x, y, z);
    }

    public static void rotation(Camera camera, float yaw, float pitch) {
        ((CameraAccess) camera).quirk$setRotation(yaw, pitch);
    }

    public static float fov(GameRenderer renderer, Camera camera, float partial) {
        return ((GameRendererAccessor) renderer).quirk$getFov(camera, partial, true);
    }
}
