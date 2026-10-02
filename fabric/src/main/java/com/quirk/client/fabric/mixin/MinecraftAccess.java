package com.quirk.client.fabric.mixin;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Invoker;
@Mixin(Minecraft.class)
public interface MinecraftAccess {
    @Invoker("startAttack") boolean quirk$attack();
}
