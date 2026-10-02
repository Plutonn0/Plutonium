package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.client.Camera;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Camera.class)
abstract class CameraMixin {
    @Inject(method = "setup", at = @At("TAIL"))
    private void quirk$moveFreecam(CallbackInfo ci) {
        Quirk.camera((Camera) (Object) this);
    }

    @Inject(method = "isDetached", at = @At("HEAD"), cancellable = true)
    private void quirk$reportDetached(CallbackInfoReturnable<Boolean> cir) {
        if (Quirk.freecam()) cir.setReturnValue(true);
    }
}
