package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.client.renderer.LightTexture;
import net.minecraft.world.level.dimension.DimensionType;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(LightTexture.class)
abstract class LightTextureMixin {
    @Inject(method="updateLightTexture",at=@At("RETURN"))
    private void quirk$whiteLightmap(float partial,org.spongepowered.asm.mixin.injection.callback.CallbackInfo ci){Quirk.lightmap((LightTexture)(Object)this);}
    @Inject(method = "getBrightness(Lnet/minecraft/world/level/dimension/DimensionType;I)F", at = @At("HEAD"), cancellable = true)
    private static void quirk$fullbright(DimensionType dimension, int lightLevel, CallbackInfoReturnable<Float> cir) {
        if (Quirk.fullbright()) cir.setReturnValue(1.0F);
    }

    @Inject(method = "getBrightness(FI)F", at = @At("HEAD"), cancellable = true)
    private static void quirk$fullbright(float ambientLight, int lightLevel, CallbackInfoReturnable<Float> cir) {
        if (Quirk.fullbright()) cir.setReturnValue(1.0F);
    }

    @ModifyVariable(method = "updateLightTexture(F)V", at = @At("STORE"), index = 12)
    private float quirk$removeDarkness(float darknessScale) {
        return Quirk.lightmapDarkness(darknessScale);
    }

    @ModifyVariable(method = "updateLightTexture(F)V", at = @At("STORE"), index = 16)
    private float quirk$raiseBrightness(float brightnessFactor) {
        return Quirk.lightmapBrightness(brightnessFactor);
    }
}
