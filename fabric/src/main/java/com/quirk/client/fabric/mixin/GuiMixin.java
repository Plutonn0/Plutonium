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
    @Inject(method = "displayScoreboardSidebar", at = @At("HEAD"))
    private void quirk$prepareSidebar(
            GuiGraphics g, net.minecraft.world.scores.Objective objective, CallbackInfo ci) {
        com.quirk.client.FakeStats.prepare();
    }

    @org.spongepowered.asm.mixin.injection.Redirect(
            method = "*",
            at =
                    @At(
                            value = "INVOKE",
                            target =
                                    "Lnet/minecraft/world/scores/PlayerTeam;formatNameForTeam(Lnet/minecraft/world/scores/Team;Lnet/minecraft/network/chat/Component;)Lnet/minecraft/network/chat/MutableComponent;"))
    private net.minecraft.network.chat.MutableComponent quirk$name(
            net.minecraft.world.scores.Team team, net.minecraft.network.chat.Component name) {
        return com.quirk.client.FakeStats.formatName(team, name);
    }

    @org.spongepowered.asm.mixin.injection.Redirect(
            method = "*",
            at =
                    @At(
                            value = "INVOKE",
                            target =
                                    "Lnet/minecraft/world/scores/PlayerScoreEntry;formatValue(Lnet/minecraft/network/chat/numbers/NumberFormat;)Lnet/minecraft/network/chat/MutableComponent;"))
    private net.minecraft.network.chat.MutableComponent quirk$value(
            net.minecraft.world.scores.PlayerScoreEntry entry,
            net.minecraft.network.chat.numbers.NumberFormat format) {
        return com.quirk.client.FakeStats.formatValue(entry, format);
    }

    @org.spongepowered.asm.mixin.injection.Redirect(
            method = "displayScoreboardSidebar",
            at =
                    @At(
                            value = "INVOKE",
                            target =
                                    "Lnet/minecraft/world/scores/Objective;getDisplayName()Lnet/minecraft/network/chat/Component;"))
    private net.minecraft.network.chat.Component quirk$title(
            net.minecraft.world.scores.Objective objective) {
        return com.quirk.client.FakeStats.formatTitle(objective);
    }

    @Inject(method = "render", at = @At("TAIL"))
    private void quirk$renderOverlay(
            GuiGraphics graphics, DeltaTracker deltaTracker, CallbackInfo ci) {
        Quirk.renderHud(graphics);
    }
}
