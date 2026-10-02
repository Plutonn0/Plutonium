package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.client.renderer.entity.player.AvatarRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(AvatarRenderer.class)
abstract class AvatarRendererMixin {
    @Inject(method="submitNameTag(Lnet/minecraft/client/renderer/entity/state/AvatarRenderState;Lcom/mojang/blaze3d/vertex/PoseStack;Lnet/minecraft/client/renderer/SubmitNodeCollector;Lnet/minecraft/client/renderer/state/CameraRenderState;)V",at=@At("HEAD"),cancellable=true)
    private void quirk$playerNames(CallbackInfo ci){if(Quirk.hideNameTags())ci.cancel();}
}
