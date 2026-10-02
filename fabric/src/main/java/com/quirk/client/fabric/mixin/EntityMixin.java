package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.world.entity.Entity;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Entity.class)
abstract class EntityMixin {
    @Inject(method = "turn(DD)V", at = @At("HEAD"), cancellable = true)
    private void quirk$turnFreecam(double dx, double dy, CallbackInfo ci) {
        if (Quirk.turn((Entity) (Object) this, dx, dy)) ci.cancel();
    }
}
