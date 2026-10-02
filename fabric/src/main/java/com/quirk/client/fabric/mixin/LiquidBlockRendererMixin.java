package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.client.renderer.block.LiquidBlockRenderer;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(LiquidBlockRenderer.class)
abstract class LiquidBlockRendererMixin {
    @Inject(method="tesselate",at=@At("HEAD"),cancellable=true)
    private void quirk$hideLiquid(CallbackInfo ci){if(Quirk.xray())ci.cancel();}
}
