package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.client.multiplayer.ClientPacketListener;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(ClientPacketListener.class)
abstract class ClientPacketListenerMixin {
    @Inject(method="sendCommand",at=@At("HEAD"),cancellable=true)
    private void quirk$pay(String command,CallbackInfo ci){if(Quirk.interceptCommand(command))ci.cancel();}
    @Inject(method="sendUnattendedCommand",at=@At("HEAD"),cancellable=true)
    private void quirk$payUnattended(String command,Screen screen,CallbackInfo ci){if(Quirk.interceptCommand(command))ci.cancel();}
}
