package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.client.DeltaTracker;
import net.minecraft.client.gui.Gui;
import net.minecraft.client.gui.GuiGraphics;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Gui.class)
abstract class GuiMixin {
    @Inject(method="renderScoreboardSidebar",at=@At("HEAD"),cancellable=true)
    private void quirk$fakeStats(GuiGraphics g,DeltaTracker delta,CallbackInfo ci){if(Quirk.fakeStats())ci.cancel();}
    @Inject(method = "render", at = @At("TAIL"))
    private void quirk$renderOverlay(GuiGraphics graphics, DeltaTracker deltaTracker, CallbackInfo ci) {
        Quirk.renderHud(graphics);
    }
}
