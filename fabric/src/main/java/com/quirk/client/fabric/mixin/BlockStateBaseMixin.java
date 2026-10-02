package com.quirk.client.fabric.mixin;

import com.quirk.client.Quirk;
import net.minecraft.core.Direction;
import net.minecraft.world.level.block.RenderShape;
import net.minecraft.world.level.block.state.BlockBehaviour;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(BlockBehaviour.BlockStateBase.class)
abstract class BlockStateBaseMixin {
    @Inject(method="isSolidRender",at=@At("HEAD"),cancellable=true)
    private void quirk$solid(CallbackInfoReturnable<Boolean> cir){if(Quirk.xray())cir.setReturnValue(false);}
    @Inject(method = "getRenderShape", at = @At("RETURN"), cancellable = true)
    private void quirk$filterRenderShape(CallbackInfoReturnable<RenderShape> cir) {
        cir.setReturnValue(Quirk.renderShape((BlockBehaviour.BlockStateBase) (Object) this, cir.getReturnValue()));
    }

    @Inject(method = "getFaceOcclusionShape", at = @At("RETURN"), cancellable = true)
    private void quirk$disableFaceOcclusion(Direction direction, CallbackInfoReturnable<VoxelShape> cir) {
        cir.setReturnValue(Quirk.faceOcclusionShape(cir.getReturnValue()));
    }
}
