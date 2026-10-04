package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Minecraft.class)
abstract class MinecraftMixin {
    @Inject(method="stop",at=@At("HEAD"))
    private void plutonium$saveVideoSettings(CallbackInfo ci){Quirk.saveVideoSettings();}
    @Shadow private int rightClickDelay;
    @Shadow private int missTime;
    @Inject(method="startAttack",at=@At("HEAD"))
    private void quirk$noHitDelay(CallbackInfoReturnable<Boolean> cir){if(Quirk.noHitDelay())missTime=0;}

    @Inject(method="startUseItem",at=@At("HEAD"),cancellable=true)
    private void quirk$handleQuickXp(CallbackInfo ci){if(Quirk.quickXpSuppressVanillaUse())ci.cancel();}

    @Inject(method = "createTitle", at = @At("HEAD"), cancellable = true)
    private void quirk$setWindowTitle(CallbackInfoReturnable<String> cir) {
        cir.setReturnValue("Plutonium | Minecraft 1.21.11");
    }

    @Inject(method = "tick", at = @At("HEAD"))
    private void quirk$tick(CallbackInfo ci) {
        Quirk.tick(); com.quirk.client.SmokeTest.tick();
        if (Quirk.fastPlace() || Quirk.doubleAnchor()) rightClickDelay = 0;
    }

    @Inject(method = "handleKeybinds", at = @At("HEAD"), cancellable = true)
    private void quirk$blockWorldInput(CallbackInfo ci) {
        if (Quirk.freecam()) ci.cancel();
    }
}

