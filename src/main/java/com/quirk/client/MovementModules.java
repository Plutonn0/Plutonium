package com.quirk.client;

import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.*;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.inventory.InventoryScreen;
import net.minecraft.client.player.LocalPlayer;
import org.lwjgl.glfw.GLFW;

/** Inventory movement, permission-aware creative flight, and elytra propulsion. */
public final class MovementModules {
    private static LocalPlayer flightOwner;
    private static boolean previousFlying, previousAllowed, inventoryKeys;
    private static float previousSpeed;
    public static void tick(){
        var mc=Minecraft.getInstance();var player=mc.player;var settings=Quirk.settings();
        var keys=new KeyMapping[]{mc.options.keyUp,mc.options.keyDown,mc.options.keyLeft,mc.options.keyRight,mc.options.keyJump};
        boolean inventory=settings.module("inventorymove").on()&&mc.screen instanceof InventoryScreen&&mc.isWindowActive()&&player!=null&&!player.isDeadOrDying()&&!Quirk.freecam()
            &&!typing(mc.screen);
        if(inventory){
            for(var binding:keys){var key=InputConstants.getKey(binding.saveString());
                binding.setDown(key.getType()==InputConstants.Type.KEYSYM&&key.getValue()>=0&&InputConstants.isKeyDown(mc.getWindow(),key.getValue()));}
        }else if(inventoryKeys){for(var binding:keys)binding.setDown(false);}
        inventoryKeys=inventory;
        var fly=settings.module("fly");
        if(flightOwner!=null&&(flightOwner!=player||!fly.on()||player==null||player.isDeadOrDying())){
            var abilities=flightOwner.getAbilities();abilities.flying=previousFlying;abilities.mayfly=previousAllowed;abilities.setFlyingSpeed(previousSpeed);
            if(flightOwner==player&&mc.hasSingleplayerServer()){var server=mc.getSingleplayerServer();var uuid=flightOwner.getUUID();boolean allowed=previousAllowed,flying=previousFlying;server.execute(()->{var serverPlayer=server.getPlayerList().getPlayer(uuid);if(serverPlayer!=null){serverPlayer.getAbilities().mayfly=allowed;serverPlayer.getAbilities().flying=flying;serverPlayer.onUpdateAbilities();}});}
            if(flightOwner==player)flightOwner.onUpdateAbilities();flightOwner=null;
        }
        if(player==null||mc.level==null||player.isDeadOrDying())return;
        if(fly.on()&&flightOwner==null){
            var abilities=player.getAbilities();
            if(!mc.hasSingleplayerServer()&&!abilities.mayfly){fly.enabled.set(false);Notifications.show("flight-permission","Flight is unavailable","This server has not granted flight permission.",10);}
            else {flightOwner=player;previousFlying=abilities.flying;previousAllowed=abilities.mayfly;previousSpeed=abilities.getFlyingSpeed();abilities.mayfly=true;abilities.flying=true;
                if(mc.hasSingleplayerServer()){var server=mc.getSingleplayerServer();var uuid=player.getUUID();server.execute(()->{var serverPlayer=server.getPlayerList().getPlayer(uuid);if(serverPlayer!=null){serverPlayer.getAbilities().mayfly=true;serverPlayer.getAbilities().flying=true;serverPlayer.onUpdateAbilities();}});}
                player.onUpdateAbilities();}
        }
        if(flightOwner==player){player.getAbilities().setFlyingSpeed((float)(.05*fly.number("speed")));player.getAbilities().flying=true;}
        var glide=settings.module("elytraglide");
        if(glide.on()&&player.isFallFlying()&&mc.screen==null&&mc.isWindowActive()&&!Quirk.freecam())
            player.setDeltaMovement(GlideMotion.accelerate(player.getDeltaMovement(),player.getLookAngle(),glide.number("speed"),glide.number("acceleration")));
    }
    private static boolean typing(net.minecraft.client.gui.components.events.GuiEventListener listener){
        if(listener instanceof EditBox box&&box.isFocused())return true;
        if(listener instanceof net.minecraft.client.gui.components.events.ContainerEventHandler container)
            for(var child:container.children())if(typing(child))return true;
        return false;
    }
}
