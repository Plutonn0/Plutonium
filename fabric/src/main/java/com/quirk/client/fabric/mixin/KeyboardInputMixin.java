package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.client.player.KeyboardInput;
import net.minecraft.world.entity.player.Input;
import net.minecraft.world.phys.Vec2;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(KeyboardInput.class)
abstract class KeyboardInputMixin {
    @Inject(method = "tick", at = @At("HEAD"), cancellable = true)
    private void quirk$blockPlayerInput(CallbackInfo ci) {
        if (Quirk.freecam()) {
            ((ClientInputAccessor) this).quirk$setKeyPresses(Input.EMPTY);
            ((ClientInputAccessor) this).quirk$setMoveVector(Vec2.ZERO);
            ci.cancel();
        }
    }
}
