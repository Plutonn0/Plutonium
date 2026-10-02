package com.quirk.client.fabric.mixin;

import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;

@Mixin(Camera.class)
public interface CameraAccess {
    @Invoker("setPosition")
    void quirk$setPosition(double x, double y, double z);

    @Invoker("setRotation")
    void quirk$setRotation(float yaw, float pitch);
}
