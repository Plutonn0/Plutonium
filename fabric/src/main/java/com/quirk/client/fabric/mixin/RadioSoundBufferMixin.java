package com.quirk.client.fabric.mixin;

import com.quirk.client.Entertainment;
import net.minecraft.client.sounds.AudioStream;
import net.minecraft.client.sounds.SoundBufferLibrary;
import net.minecraft.resources.Identifier;
import net.minecraft.util.Util;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
import java.util.concurrent.CompletableFuture;

@Mixin(SoundBufferLibrary.class)
public abstract class RadioSoundBufferMixin {
    @Inject(method="getStream",at=@At("HEAD"),cancellable=true)
    private void quirk$openRadioStream(Identifier location,boolean looping,CallbackInfoReturnable<CompletableFuture<AudioStream>> cir){
        if(location.equals(Entertainment.RADIO_STREAM_PATH))
            cir.setReturnValue(CompletableFuture.supplyAsync(Entertainment::openRadioAudioStream,Util.nonCriticalIoPool()));
    }
}