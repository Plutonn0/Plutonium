package com.quirk.client;

import com.quirk.client.ui.QuirkMenu;
import net.minecraft.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.component.DataComponents;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.inventory.ClickType;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.RenderShape;
import net.minecraft.world.level.block.state.BlockBehaviour.BlockStateBase;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.EntityHitResult;
import net.minecraft.world.phys.HitResult;
import net.minecraft.world.phys.Vec3;
import org.lwjgl.glfw.GLFW;
import java.util.List;
import java.util.Locale;

public final class Quirk {
    private static Settings settings;
    private static ConfigStore store;
    private static final Overlay overlay = new Overlay();
    private static final float FULLBRIGHT_LIGHTMAP_FACTOR = 20;
    private static volatile boolean fullbright;
    private static volatile boolean xray;
    private static Entity owner;
    private static Vec3 cameraPosition;
    private static float yaw, pitch;
    private static long lastCameraTime;
    private static boolean wasFreecam;
    private static Object automationLevel;
    private static int autoEatOriginalSlot = -1, autoEatFoodSlot = -1;
    private static int autoFireworkOriginalSlot = -1, autoFireworkSlot = -1;
    private static long nextFireworkTick,nextQuickExp;
    private static boolean clutchAttempted;
    private static long eatStarted, nextClick;
    private static boolean eatWasUsing, wasFreelook, altHeld;
    private static float lookYaw,lookPitch;
    private static int maceRestore=-1;
    private static final java.util.Map<String,Boolean> enabledStates=new java.util.HashMap<>();
    public static Settings settings() { initialize(); return settings; }
    public static ConfigStore store() { initialize(); return store; }
    public static void initializeClient() { initialize(); }
    private static void initialize() {
        if(settings!=null) return;
        settings=new Settings(); store=new ConfigStore(Minecraft.getInstance().gameDirectory.toPath().resolve("quirk/settings.json"),settings);
        store.load(); fullbright=settings.module("fullbright").on(); xray=settings.module("xray").on();
        for(var module:settings.modules)enabledStates.put(module.id,module.on());
        settings.onChange(m -> {
            store.changed(); overlay.invalidate();
            boolean before=enabledStates.getOrDefault(m.id,m.on());enabledStates.put(m.id,m.on());
            if(before!=m.on())Notifications.show("module-"+m.id,m.name+(m.on()?" enabled":" disabled"),m.description,0);
            if(m.id.equals("freecam")) syncFreecam();
            if(m.id.equals("fullbright")) fullbright=m.on();
            if(m.id.equals("xray")) {
                xray=m.on();
                Minecraft mc=Minecraft.getInstance();
                if(mc.level!=null) mc.levelRenderer.allChanged();
            }
        });
        System.out.println("[Plutonium] Client initialized: Minecraft 1.21.11 / Java 21");
    }
    public static void tick() {
        initialize(); syncFreecam(); syncFreelook(); automate(); aimAssist(); extraAutomation(); store.tick(); overlay.tick(); Notifications.tick(); Entertainment.tick();
    }
    public static java.util.concurrent.CompletableFuture<net.minecraft.client.sounds.AudioStream> radioStream(net.minecraft.resources.Identifier id){
        return id.equals(Entertainment.RADIO_STREAM_PATH)?java.util.concurrent.CompletableFuture.supplyAsync(Entertainment::openRadioAudioStream,net.minecraft.util.Util.nonCriticalIoPool()):null;
    }
    public static boolean fastPlace() {
        if (settings == null || !settings.module("fastplace").on()) return false;
        Minecraft mc = Minecraft.getInstance();
        if (mc.player == null || mc.screen != null || !mc.isWindowActive()) return false;
        return mc.player.getMainHandItem().getItem() instanceof BlockItem
            || mc.player.getOffhandItem().getItem() instanceof BlockItem;
    }
    public static boolean doubleAnchor() {
        if (settings == null || !settings.module("doubleanchor").on()) return false;
        Minecraft mc = Minecraft.getInstance();
        return mc.player != null && mc.level != null && mc.screen == null && mc.isWindowActive()
            && mc.options.keyUse.isDown() && mc.hitResult instanceof BlockHitResult hit
            && mc.level.getBlockState(hit.getBlockPos()).is(Blocks.RESPAWN_ANCHOR);
    }
    private static void automate() {
        Minecraft mc = Minecraft.getInstance();
        if (mc.level != automationLevel) {
            if(autoEatFoodSlot>=0)mc.options.keyUse.setDown(false);
            automationLevel = mc.level;
            autoEatOriginalSlot = autoEatFoodSlot = autoFireworkOriginalSlot = autoFireworkSlot = -1;
            nextFireworkTick = 0;
            nextQuickExp = 0;
            clutchAttempted = false;
            maceRestore=-1; nextClick=0;
        }
        if (mc.level == null || mc.player == null || mc.gameMode == null) return;

        var player = mc.player;
        Inventory inventory = player.getInventory();
        Settings.Module eat = settings.module("autoeat");
        boolean canAct=mc.screen==null&&mc.isWindowActive()&&!freecam()&&!player.isDeadOrDying()&&!player.isSpectator();
        if (autoEatFoodSlot >= 0) {
            eatWasUsing|=player.isUsingItem();
            if(!eat.on()||!canAct||inventory.getSelectedSlot()!=autoEatFoodSlot
                    ||(eatWasUsing&&!player.isUsingItem())||mc.level.getGameTime()-eatStarted>80){
                mc.options.keyUse.setDown(false);
                if(player.isUsingItem())mc.gameMode.releaseUsingItem(player);
                restoreAutoEatSlot(inventory);Notifications.show("ate","Auto Eat finished",2);
            }else mc.options.keyUse.setDown(true);
        }
        if (autoEatFoodSlot<0 && eat.on() && canAct && !player.isUsingItem() && !mc.options.keyUse.isDown()
                && player.getFoodData().getFoodLevel()<20 && player.getFoodData().getFoodLevel() <= eat.number("threshold")) {
            int foodSlot = bestFoodSlot(inventory);
            if (foodSlot >= 0) {
                autoEatOriginalSlot = inventory.getSelectedSlot();
                autoEatFoodSlot = foodSlot;
                eatStarted=mc.level.getGameTime();eatWasUsing=false;
                selectSlot(mc, foodSlot);
                mc.options.keyUse.setDown(true);
                mc.gameMode.useItem(player, InteractionHand.MAIN_HAND);
                Notifications.show("eating","Auto Eat: eating "+inventory.getItem(foodSlot).getHoverName().getString(),3);
            }
        }

        Settings.Module firework = settings.module("autofirework");
        if (!firework.on() || !player.isFallFlying() || player.isUsingItem() || !canAct) {
            restoreFireworkSlot(mc, inventory);
        } else if (mc.level.getGameTime() >= nextFireworkTick) {
            int rocketSlot = hotbarSlot(inventory, Items.FIREWORK_ROCKET);
            if (rocketSlot >= 0) {
                if (autoFireworkOriginalSlot < 0) autoFireworkOriginalSlot = inventory.getSelectedSlot();
                autoFireworkSlot = rocketSlot;
                selectSlot(mc, rocketSlot);
                mc.gameMode.useItem(player, InteractionHand.MAIN_HAND);
                nextFireworkTick = mc.level.getGameTime() + (long)firework.number("interval");
            } else {
                restoreFireworkSlot(mc, inventory);
            }
        }

        if (((settings.module("autototem").on() && canAct) || (settings.module("invtotem").on() && mc.screen instanceof net.minecraft.client.gui.screens.inventory.InventoryScreen))
                && player.containerMenu == player.inventoryMenu
                && player.containerMenu.getCarried().isEmpty()
                && !inventory.getItem(Inventory.SLOT_OFFHAND).is(Items.TOTEM_OF_UNDYING)) {
            for (int slot = 0; slot < inventory.getNonEquipmentItems().size(); slot++) {
                if (!inventory.getItem(slot).is(Items.TOTEM_OF_UNDYING)) continue;
                var menuSlot = player.inventoryMenu.findSlot(inventory, slot);
                if (menuSlot.isPresent()) {
                    mc.gameMode.handleInventoryMouseClick(player.inventoryMenu.containerId, menuSlot.getAsInt(),
                        Inventory.SLOT_OFFHAND, ClickType.SWAP, player);
                    Notifications.show("totem","Totem equipped",2);
                }
                break;
            }
        }
        autoClutch(mc, player, inventory);
        autoMace(mc, player);
    }
    private static void autoMace(Minecraft mc, net.minecraft.client.player.LocalPlayer player) {
        if(maceRestore>=0){if(player.getMainHandItem().is(Items.MACE))selectSlot(mc,maceRestore);maceRestore=-1;}
        if(!settings.module("automace").on()||mc.screen!=null||!mc.isWindowActive()
                ||freecam()||player.isUsingItem()||mc.hitResult==null
                ||!(mc.hitResult instanceof EntityHitResult hit)||!(hit.getEntity() instanceof net.minecraft.world.entity.LivingEntity target)
                ||target==player||!target.isAlive()||target.isSpectator()||(target instanceof Player p&&p.isCreative())
                ||!player.isWithinAttackRange(target.getBoundingBox(),0)
                ||player.getAttackStrengthScale(0.5f)<0.95f) return;
        var module=settings.module("automace");
        if(module.flag("falling")&&(player.fallDistance<1.5||player.getDeltaMovement().y>=0))return;
        if(!player.getMainHandItem().is(Items.MACE)){
            int slot=hotbarSlot(player.getInventory(),Items.MACE);if(!module.flag("select")||slot<0)return;
            maceRestore=player.getInventory().getSelectedSlot();selectSlot(mc,slot);
        }
        mc.gameMode.attack(player,target);
        player.swing(InteractionHand.MAIN_HAND);
        player.resetAttackStrengthTicker();
        Notifications.show("mace","Automace attacked "+target.getName().getString(),2);
    }
    private static int bestFoodSlot(Inventory inventory) {
        int bestSlot = -1, nutrition = -1;
        for (int slot = 0; slot < 9; slot++) {
            ItemStack stack = inventory.getItem(slot);
            var food = stack.get(DataComponents.FOOD);
            if (food != null && food.nutrition() > nutrition) {
                bestSlot = slot;
                nutrition = food.nutrition();
            }
        }
        return bestSlot;
    }
    private static int hotbarSlot(Inventory inventory, net.minecraft.world.item.Item item) {
        for (int slot = 0; slot < 9; slot++) if (inventory.getItem(slot).is(item)) return slot;
        return -1;
    }
    private static void selectSlot(Minecraft mc, int slot) {
        Inventory inventory = mc.player.getInventory();
        if (inventory.getSelectedSlot() == slot) return;
        inventory.setSelectedSlot(slot);
        mc.player.connection.send(new net.minecraft.network.protocol.game.ServerboundSetCarriedItemPacket(slot));
    }
    private static void restoreAutoEatSlot(Inventory inventory) {
        if (inventory.getSelectedSlot() == autoEatFoodSlot && autoEatOriginalSlot >= 0)
            selectSlot(Minecraft.getInstance(), autoEatOriginalSlot);
        autoEatOriginalSlot = autoEatFoodSlot = -1;
    }
    private static void restoreFireworkSlot(Minecraft mc, Inventory inventory) {
        if (autoFireworkSlot >= 0 && inventory.getSelectedSlot() == autoFireworkSlot && autoFireworkOriginalSlot >= 0)
            selectSlot(mc, autoFireworkOriginalSlot);
        autoFireworkOriginalSlot = autoFireworkSlot = -1;
        nextFireworkTick = 0;
    }
    public static boolean key(int action,KeyEvent e) {
        if(e.key()==GLFW.GLFW_KEY_LEFT_ALT||e.key()==GLFW.GLFW_KEY_RIGHT_ALT) {
            if(action==GLFW.GLFW_PRESS) altHeld=true;
            else if(action==GLFW.GLFW_RELEASE) altHeld=false;
            return false;
        }
        if(e.key()!=GLFW.GLFW_KEY_F8) return false;
        Minecraft mc=Minecraft.getInstance();
        if(action!=GLFW.GLFW_PRESS) return true;
        if(mc.screen instanceof QuirkMenu menu) { menu.onClose(); return true; }
        if(mc.level==null || mc.player==null || mc.player.isDeadOrDying()) return false;
        if(mc.screen!=null) { if(!mc.screen.shouldCloseOnEsc()) return false; mc.screen.onClose(); }
        KeyMapping.releaseAll(); mc.setScreen(new QuirkMenu(null)); return true;
    }
    public static boolean freecam() { return settings!=null && wasFreecam && Minecraft.getInstance().player==owner; }
    public static boolean fullbright() { return fullbright; }
    public static float lightmapBrightness(float original) { return fullbright ? FULLBRIGHT_LIGHTMAP_FACTOR : original; }
    public static float lightmapDarkness(float original) { return fullbright ? 0 : original; }
    public static VoxelShape faceOcclusionShape(VoxelShape original) { return xray ? Shapes.empty() : original; }
    public static RenderShape renderShape(BlockStateBase state,RenderShape original) {
        if(!xray||XrayFilter.isValuable(BuiltInRegistries.BLOCK.getKey(state.getBlock()).getPath())) return original;
        return RenderShape.INVISIBLE;
    }
    private static void aimAssist() {
        if(settings==null) return;
        Settings.Module module=settings.module("aimassist");
        Minecraft mc=Minecraft.getInstance();
        Player player=mc.player;
        if(!module.on()) return;
        if(mc.level==null||player==null||mc.screen!=null||freecam()||wasFreelook||!mc.isWindowActive()) return;
        Vec3 eye=player.getEyePosition();
        double range=module.number("range");
        double maxAngle=module.number("angle");
        double prediction=module.number("prediction");
        float currentYaw=player.getYRot(), currentPitch=player.getXRot();
        Player best=null;
        float bestYaw=0,bestPitch=0;
        double bestError=Double.POSITIVE_INFINITY;
        double bestScore=Double.POSITIVE_INFINITY;
        for(Player target:mc.level.players()) {
            if(target==player||!target.isAlive()||target.isSpectator()||target.isCreative()) continue;
            Vec3 lead=AimAssist.predictedOffset(target.getDeltaMovement(),prediction);
            Vec3 targetPoint=target.position().add(lead).add(0,target.getBbHeight()*0.62,0);
            Vec3 toward=targetPoint.subtract(eye);
            double distance=toward.length();
            if(distance>range||distance<0.01) continue;
            double horizontal=Math.hypot(toward.x,toward.z);
            float targetYaw=(float)(Math.toDegrees(Math.atan2(toward.z,toward.x))-90);
            float targetPitch=(float)-Math.toDegrees(Math.atan2(toward.y,horizontal));
            double error=Math.hypot(AimAssist.delta(currentYaw,targetYaw),targetPitch-currentPitch);
            if(error>maxAngle) continue;
            var obstruction=mc.level.clip(new ClipContext(eye,targetPoint,ClipContext.Block.COLLIDER,
                ClipContext.Fluid.NONE,player));
            if(obstruction.getType()!=HitResult.Type.MISS) continue;
            double score=AimAssist.score(error,distance,maxAngle,range);
            if(score<bestScore) {
                best=target; bestYaw=targetYaw; bestPitch=targetPitch; bestError=error; bestScore=score;
            }
        }
        if(best==null) return;
        float fraction=(float)Math.min(.6,module.number("speed")/Math.max(.001,bestError));
        player.setYRot(currentYaw+AimAssist.delta(currentYaw,bestYaw)*fraction);
        player.setXRot(Math.clamp(currentPitch+(bestPitch-currentPitch)*fraction,-90,90));
    }
    private static void autoClutch(Minecraft mc, net.minecraft.client.player.LocalPlayer player, Inventory inventory) {
        Settings.Module module=settings.module("autoclutch");
        double verticalSpeed=player.getDeltaMovement().y;
        if(player.onGround()||player.isInWater()||verticalSpeed>=-0.12) clutchAttempted=false;
        if(!module.on()||clutchAttempted||mc.screen!=null||!mc.isWindowActive()||player.isSpectator()||freecam()||player.isUsingItem()
                ||player.isFallFlying()||player.isPassenger()||player.isInWater()
                ||verticalSpeed>=-0.5||player.fallDistance<module.number("fall")) return;

        Vec3 feet=player.position().add(0,0.1,0);
        double rayLength=Math.min(12,Math.max(module.number("fall")+3,player.fallDistance+1));
        var hit=mc.level.clip(new ClipContext(feet,feet.add(0,-rayLength,0),ClipContext.Block.COLLIDER,
            ClipContext.Fluid.NONE,player));
        if(hit.getType()!=HitResult.Type.BLOCK) return;

        double groundDistance=feet.y-hit.getLocation().y;
        boolean nether=mc.level.dimension().equals(Level.NETHER);
        int water=hotbarSlot(inventory,Items.WATER_BUCKET), web=hotbarSlot(inventory,Items.COBWEB);
        int powder=hotbarSlot(inventory,Items.POWDER_SNOW_BUCKET), hay=hotbarSlot(inventory,Items.HAY_BLOCK);
        int slime=hotbarSlot(inventory,Items.SLIME_BLOCK), honey=hotbarSlot(inventory,Items.HONEY_BLOCK);
        ClutchPlanner.Item choice=ClutchPlanner.choose(water>=0,nether,web>=0,powder>=0,hay>=0,slime>=0,honey>=0,groundDistance);
        int clutchSlot=switch(choice) {
            case NONE -> -1;
            case WATER_BUCKET -> water;
            case COBWEB -> web;
            case POWDER_SNOW_BUCKET -> powder;
            case HAY_BLOCK -> hay;
            case SLIME_BLOCK -> slime;
            case HONEY_BLOCK -> honey;
        };
        if(clutchSlot<0||player.getEyePosition().distanceTo(hit.getLocation())>player.blockInteractionRange()) return;

        int previousSlot=inventory.getSelectedSlot();
        float previousYaw=player.getYRot(),previousPitch=player.getXRot();
        Vec3 toward=hit.getLocation().subtract(player.getEyePosition());
        player.setYRot((float)(Math.toDegrees(Math.atan2(toward.z,toward.x))-90));
        player.setXRot((float)-Math.toDegrees(Math.atan2(toward.y,Math.hypot(toward.x,toward.z))));
        selectSlot(mc,clutchSlot);
        player.connection.send(new net.minecraft.network.protocol.game.ServerboundMovePlayerPacket.Rot(player.getYRot(),player.getXRot(),player.onGround(),player.horizontalCollision));
        var result=(choice==ClutchPlanner.Item.WATER_BUCKET||choice==ClutchPlanner.Item.POWDER_SNOW_BUCKET)
            ?mc.gameMode.useItem(player,InteractionHand.MAIN_HAND):mc.gameMode.useItemOn(player,InteractionHand.MAIN_HAND,hit);
        if(result.consumesAction()){player.swing(InteractionHand.MAIN_HAND);clutchAttempted=true;Notifications.show("clutch","Auto Clutch used "+choice.name().toLowerCase(Locale.ROOT).replace('_',' '),2);}
        selectSlot(mc,previousSlot);
        player.setYRot(previousYaw);player.setXRot(previousPitch);
        player.connection.send(new net.minecraft.network.protocol.game.ServerboundMovePlayerPacket.Rot(previousYaw,previousPitch,player.onGround(),player.horizontalCollision));
    }
    private static void syncFreecam() {
        Minecraft mc=Minecraft.getInstance();
        if(mc.level==null || mc.player==null || (owner!=null && owner!=mc.player) || (mc.player!=null && mc.player.isDeadOrDying())) {
            wasFreecam=false; owner=null; cameraPosition=null;
            if(settings.module("freecam").on()) settings.module("freecam").enabled.set(false);
            return;
        }
        boolean enabled=settings.module("freecam").on();
        if(enabled&&!wasFreecam) {
            owner=mc.player; Camera camera=mc.gameRenderer.getMainCamera(); cameraPosition=camera.position(); yaw=camera.yRot(); pitch=camera.xRot();
            lastCameraTime=System.nanoTime(); KeyMapping.releaseAll();
            if(mc.gameMode!=null) mc.gameMode.stopDestroyBlock();
        }
        if(!enabled&&wasFreecam) { KeyMapping.releaseAll(); owner=null; cameraPosition=null; }
        wasFreecam=enabled;
    }
    public static boolean turn(Entity entity,double dx,double dy) {
        if(wasFreelook&&!freecam()&&entity==Minecraft.getInstance().player){lookYaw+=(float)(dx*.15);lookPitch=Math.clamp(lookPitch+(float)(dy*.15),-90,90);return true;}
        if(!freecam() || entity!=owner) return false;
        yaw+=(float)(dx*0.15); pitch=Math.clamp(pitch+(float)(dy*0.15),-90,90); return true;
    }
    public static void camera(Camera camera) {
        if(wasFreelook&&!freecam()){EngineAccess.rotation(camera,lookYaw,lookPitch);return;}
        if(!freecam() || cameraPosition==null) return;
        Minecraft mc=Minecraft.getInstance(); long now=System.nanoTime(); double dt=Math.min(0.1,(now-lastCameraTime)/1e9); lastCameraTime=now;
        if(mc.screen==null&&mc.isWindowActive()) {
            var o=mc.options; double forward=(o.keyUp.isDown()?1:0)-(o.keyDown.isDown()?1:0), right=(o.keyRight.isDown()?1:0)-(o.keyLeft.isDown()?1:0);
            double speed=settings.module("freecam").number("speed")*2*dt;
            double vertical=((o.keyJump.isDown()?1:0)-(o.keyShift.isDown()?1:0))*settings.module("freecam").number("vertical")*2*dt;
            Vec3 direction=FreecamMovement.direction(yaw,pitch,forward,right);
            cameraPosition=cameraPosition.add(direction.x*speed,vertical,direction.z*speed);
        }
        EngineAccess.position(camera,cameraPosition.x,cameraPosition.y,cameraPosition.z); EngineAccess.rotation(camera,yaw,pitch);
    }
    public static void renderHud(GuiGraphics g) { initialize(); if(Minecraft.getInstance().level!=null) overlay.render(g); }
    private static void syncFreelook(){
        var mc=Minecraft.getInstance();
        if(mc.screen!=null||!mc.isWindowActive()) altHeld=false;
        boolean enabled=altHeld&&mc.player!=null&&!mc.player.isDeadOrDying()&&mc.screen==null&&mc.isWindowActive();
        if(enabled&&!wasFreelook){lookYaw=mc.player.getYRot();lookPitch=mc.player.getXRot();}
        wasFreelook=enabled;
    }
    public static void renderWorld(Camera camera,org.joml.Matrix4f view,org.joml.Matrix4f projection,DeltaTracker delta){initialize();overlay.world(camera,view,projection,delta);}
    public static void lightmap(net.minecraft.client.renderer.LightTexture light){if(fullbright)com.mojang.blaze3d.systems.RenderSystem.getDevice().createCommandEncoder().clearColorTexture(light.getTextureView().texture(),0xffffffff);}
    public static boolean xray(){return xray;}
    public static boolean hideNameTags(){return settings!=null&&!settings.module("nametags").on();}
    public static boolean fakeStats(){return settings!=null&&settings.module("fakestats").on();}
    public static boolean noHitDelay(){return settings!=null&&settings.module("nohitdelay").on()&&Minecraft.getInstance().screen==null;}
    public static boolean quickXpSuppressVanillaUse(){
        if(settings==null||!settings.module("quickexp").on())return false;
        Minecraft mc=Minecraft.getInstance();
        return mc.level!=null&&mc.player!=null&&mc.screen==null&&mc.isWindowActive()&&!freecam()
            &&!mc.player.isDeadOrDying()&&!mc.player.isUsingItem()
            &&GLFW.glfwGetMouseButton(mc.getWindow().handle(),GLFW.GLFW_MOUSE_BUTTON_RIGHT)==GLFW.GLFW_PRESS
            &&(mc.player.getMainHandItem().is(Items.EXPERIENCE_BOTTLE)||mc.player.getOffhandItem().is(Items.EXPERIENCE_BOTTLE));
    }
    public static boolean interceptCommand(String command){
        if(settings==null||!settings.module("fakepay").on()||!isPayCommand(command))return false;
        String[] args=command.strip().replaceFirst("^/", "").split("\\s+");
        String message=args.length>=3?"Paid "+args[2]+" to "+args[1]+" (local preview)":"Fake Pay: /pay <player> <amount>";
        Notifications.show("pay",message,0);
        var player=Minecraft.getInstance().player;if(player!=null)player.displayClientMessage(net.minecraft.network.chat.Component.literal(message),false);
        return true;
    }
    static boolean isPayCommand(String command){String token=command.strip().replaceFirst("^/", "").split("\\s+",2)[0].toLowerCase(Locale.ROOT);return token.equals("pay")||token.endsWith(":pay");}
    private static void extraAutomation(){
        var mc=Minecraft.getInstance();if(mc.level==null||mc.player==null||mc.gameMode==null||mc.screen!=null||!mc.isWindowActive()||freecam()||mc.player.isDeadOrDying())return;
        var player=mc.player;
        if(settings.module("sprint").on()&&mc.options.keyUp.isDown()&&!player.isShiftKeyDown()&&!player.isUsingItem()&&!player.horizontalCollision&&(player.getFoodData().getFoodLevel()>6||player.getAbilities().mayfly))player.setSprinting(true);
        var click=settings.module("autoclicker");long now=System.nanoTime();
        var quickExp=settings.module("quickexp");
        if(quickExp.on()&&!player.isUsingItem()&&now>=nextQuickExp
                &&GLFW.glfwGetMouseButton(mc.getWindow().handle(),GLFW.GLFW_MOUSE_BUTTON_RIGHT)==GLFW.GLFW_PRESS){
            InteractionHand hand=player.getMainHandItem().is(Items.EXPERIENCE_BOTTLE)?InteractionHand.MAIN_HAND
                :player.getOffhandItem().is(Items.EXPERIENCE_BOTTLE)?InteractionHand.OFF_HAND:null;
            if(hand!=null){nextQuickExp=now+100_000_000L;mc.gameMode.useItem(player,hand);}
        }
        boolean held=GLFW.glfwGetMouseButton(mc.getWindow().handle(),GLFW.GLFW_MOUSE_BUTTON_LEFT)==GLFW.GLFW_PRESS;
        if(click.on()&&(!click.flag("hold")||held)&&!player.isUsingItem()&&now>=nextClick){
            nextClick=now+(long)(1e9/click.number("cps"));
            if(!(mc.hitResult instanceof BlockHitResult hit&&hit.getType()==HitResult.Type.BLOCK))EngineAccess.attack(mc);
        }
    }
}
