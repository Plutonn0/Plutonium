package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.client.renderer.entity.EntityRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(EntityRenderer.class)
abstract class EntityRendererMixin {
    @Inject(method="submitNameTag",at=@At("HEAD"),cancellable=true)
    private void quirk$names(CallbackInfo ci){if(Quirk.hideNameTags())ci.cancel();}
}
