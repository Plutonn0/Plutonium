package com.quirk.client.fabric.mixin;
import com.quirk.client.Quirk;
import net.minecraft.client.*;
import net.minecraft.client.renderer.LevelRenderer;
import com.mojang.blaze3d.resource.GraphicsResourceAllocator;
import com.mojang.blaze3d.buffers.GpuBufferSlice;
import org.joml.*;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.*;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
@Mixin(LevelRenderer.class)
abstract class LevelRendererMixin {
    @Inject(method="renderLevel",at=@At("TAIL"))
    private void quirk$world(GraphicsResourceAllocator allocator,DeltaTracker delta,boolean outline,Camera camera,Matrix4f view,Matrix4f projection,Matrix4f culling,GpuBufferSlice fog,Vector4f color,boolean sky,CallbackInfo ci){Quirk.renderWorld(camera,view,projection,delta);}
}
