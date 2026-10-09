package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.LevelChunk;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;
@Mixin(LevelChunk.class)
abstract class LevelChunkMixin {
    @Inject(method="setBlockState",at=@At("RETURN"))
    private void quirk$changed(BlockPos pos,BlockState state,int flags,CallbackInfoReturnable<BlockState> cir){
        Quirk.blockChanged(cir.getReturnValue(),(LevelChunk)(Object)this,pos);
    }
}
