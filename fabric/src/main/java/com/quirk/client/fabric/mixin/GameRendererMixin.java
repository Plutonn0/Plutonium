package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import org.joml.Matrix4f;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(net.minecraft.client.renderer.GameRenderer.class)
abstract class GameRendererMixin {
    @Inject(method = "renderItemInHand", at = @At("HEAD"), cancellable = true)
    private void quirk$hideHandWhileFreecam(float partialTick, boolean renderHands, Matrix4f projection, CallbackInfo ci) {
        if (Quirk.freecam()) ci.cancel();
    }
}
