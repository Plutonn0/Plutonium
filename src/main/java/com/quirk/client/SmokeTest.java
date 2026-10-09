package com.quirk.client;

import com.quirk.client.ui.QuirkMenu;
import net.minecraft.client.*;
import net.minecraft.client.gui.screens.TitleScreen;
import net.minecraft.client.input.*;
import net.minecraft.core.BlockPos;
import net.minecraft.world.level.*;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.WorldOptions;
import net.minecraft.world.level.levelgen.presets.WorldPresets;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.Difficulty;
import net.minecraft.world.phys.Vec3;
import java.nio.file.*;
import java.lang.reflect.Field;
import static org.lwjgl.glfw.GLFW.*;

/** Opt-in, isolated integration run. It creates its own flat world and never touches normal saves. */
public final class SmokeTest {
    private static int ticks, stage, step;
    private static long worldTime;
    private static Vec3 playerPosition;
    private static Vec3 initialCamera;
    private static float initialAimYaw;
    private static net.minecraft.client.player.RemotePlayer aimTarget;
    private static int initialColor;
    private static int maceTargetId;
    private static float maceTargetHealth;
    private static float minimumClutchHealth=20;
    public static void tick() {
        if (!Boolean.getBoolean("quirk.smoke")) return;
        Minecraft mc=Minecraft.getInstance(); ticks++;
        try {
            if(stage==0&&ticks%100==0)System.out.println("[Plutonium smoke] Waiting at "+(mc.screen==null?"no screen":mc.screen.getClass().getName()));
            if(stage==0 && ticks>60 && mc.screen instanceof TitleScreen) {
                Path videoProbe=mc.gameDirectory.toPath().resolve("video-settings-probe.txt");
                if(Files.exists(videoProbe))check(mc.options.renderDistance().get()==7&&mc.options.simulationDistance().get()==8&&mc.options.framerateLimit().get()==160,"Video settings persist across a full Minecraft restart");
                check(mc.getResourceManager().getResource(net.minecraft.resources.Identifier.fromNamespaceAndPath("minecraft", "font/quirk/ui.ttf")).isPresent(), "Bundled high-resolution UI font loads");
                mc.options.pauseOnLostFocus=false; mc.options.renderDistance().set(6); mc.options.guiScale().set(2);
                mc.options.renderDistance().set(7);mc.options.simulationDistance().set(8);mc.options.framerateLimit().set(160);
                VideoSettingsPersistence.saveOnExit();
                mc.options.renderDistance().set(4);mc.options.simulationDistance().set(12);mc.options.framerateLimit().set(120);
                mc.options.load();
                check(mc.options.renderDistance().get()==7&&mc.options.simulationDistance().get()==8&&mc.options.framerateLimit().get()==160,"Video settings survive save and reload");
                Files.writeString(videoProbe,"7,8,160");
                // Render a real 4K framebuffer even when this desktop caps the OS window size.
                mc.getWindow().setWidth(3840);mc.getWindow().setHeight(2160);mc.resizeDisplay();
                mc.createWorldOpenFlows().createFreshLevel("Plutonium-smoke-"+System.currentTimeMillis(),
                    new LevelSettings("Plutonium integration test",GameType.CREATIVE,false,Difficulty.PEACEFUL,true,
                        new GameRules(FeatureFlags.DEFAULT_FLAGS),WorldDataConfiguration.DEFAULT),
                    new WorldOptions(42L,false,false),WorldPresets::createFlatWorldDimensions,new TitleScreen());
                stage=1; System.out.println("[Plutonium smoke] Creating isolated flat test world.");
            } else if(stage==1 && mc.level!=null && mc.player!=null && mc.screen==null) {
                if(++step<60) return; stage=2; step=0;
                int wide = com.quirk.client.ui.Paint.width("WWW"), narrow = com.quirk.client.ui.Paint.width("iii");
                check(wide > narrow * 2, "UI font resolves distinct letter metrics instead of missing-glyph rectangles (" + wide + "/" + narrow + ")");
                for(var module:Quirk.settings().modules) Quirk.settings().reset(module);
                mc.getConnection().sendCommand("gamerule send_command_feedback false");
                mc.getConnection().sendCommand("tp @s 0 -60 -9 0 12");
                mc.getConnection().sendCommand("setblock -3 -60 2 chest");
                mc.getConnection().sendCommand("setblock 0 -60 2 barrel");
                mc.getConnection().sendCommand("setblock 3 -60 2 spawner{SpawnData:{entity:{id:\"minecraft:zombie\"}}}");
                mc.getConnection().sendCommand("setblock 5 -60 2 ender_chest");
                mc.getConnection().sendCommand("setblock -5 -60 2 purple_shulker_box");
                mc.getConnection().sendCommand("setblock -1 -60 2 stone");
                mc.getConnection().sendCommand("setblock 1 -60 2 diamond_ore");
                mc.getConnection().sendCommand("setblock 7 -60 2 ancient_debris");
                mc.getConnection().sendCommand("summon cow 9 -60 2 {NoAI:1b,PersistenceRequired:1b}");
                mc.getConnection().sendCommand("time set midnight");
                Quirk.settings().module("netherite").enabled.set(true);
                Quirk.settings().module("mobs").enabled.set(true);
                Quirk.settings().module("storage").enabled.set(true); Quirk.settings().module("spawners").enabled.set(true);
                Quirk.settings().module("tracers").enabled.set(true); Quirk.settings().module("players").enabled.set(true);
                Quirk.settings().module("xray").enabled.set(true); Quirk.settings().module("fullbright").enabled.set(true);
                aimTarget=new net.minecraft.client.player.RemotePlayer(mc.level,new com.mojang.authlib.GameProfile(java.util.UUID.fromString("00000000-0000-0000-0000-000000000099"),"ESP fixture"));
                aimTarget.setId(-9001); mc.level.addEntity(aimTarget);
            } else if(stage==2) {
                step++;
                if(step==60) {
                    var probe=new BlockPos(12,-60,12);
                    var original=mc.level.getBlockState(probe);
                    int observed=Quirk.changedBlocksForTest(0,0);
                    mc.level.setBlock(probe,net.minecraft.world.level.block.Blocks.STONE.defaultBlockState(),3);
                    check(Quirk.changedBlocksForTest(0,0)==observed+1,"SusChunk observes placed blocks through the engine hook without another player");
                    mc.level.setBlock(probe,original,3);
                    check(Quirk.changedBlocksForTest(0,0)==observed,"SusChunk removes restored/prediction-rolled-back changes");
                    var ground=new BlockPos(12,-61,12);var groundState=mc.level.getBlockState(ground);
                    check(!groundState.isAir(),"SusChunk mining fixture starts with solid terrain");
                    mc.level.setBlock(ground,net.minecraft.world.level.block.Blocks.AIR.defaultBlockState(),3);
                    check(Quirk.changedBlocksForTest(0,0)==observed+1,"SusChunk observes missing blocks from mining");
                    mc.level.setBlock(ground,groundState,3);
                    screenshot("01-overlay.png");
                    check(Quirk.fullbright(),"Fullbright toggle reaches the lightmap hook");
                    check(net.minecraft.client.renderer.LightTexture.getBrightness(mc.level.dimensionType(),0)==1,
                        "Fullbright overrides dimension light brightness");
                    check(net.minecraft.client.renderer.LightTexture.getBrightness(0,0)==1,
                        "Fullbright overrides gamma light brightness");
                    check(Quirk.lightmapBrightness(0)==20&&Quirk.lightmapDarkness(0.5f)==0,
                        "Fullbright overrides the brightness and darkness values actually uploaded to the lightmap");
                    var stone=mc.level.getBlockState(new BlockPos(-1,-60,2));
                    var ore=mc.level.getBlockState(new BlockPos(1,-60,2));
                    check(stone.getRenderShape()==net.minecraft.world.level.block.RenderShape.INVISIBLE,"X-ray hides ordinary terrain");
                    check(ore.getRenderShape()==net.minecraft.world.level.block.RenderShape.MODEL,"X-ray preserves ore visibility");
                    for(var direction:net.minecraft.core.Direction.values())
                        check(ore.getFaceOcclusionShape(direction).isEmpty(),"X-ray disables ore face occlusion for "+direction);
                    var chestPos=new BlockPos(-3,-60,2);
                    var chest=mc.level.getBlockState(chestPos);
                    var chestShape=chest.getShape(mc.level,chestPos);
                    var chestBounds=Overlay.blockBounds(chestShape,chestPos);
                    check(chestBounds.equals(chestShape.bounds().move(chestPos))&&chestBounds.getYsize()<1,
                        "Storage ESP follows the chest's actual block shape");
                    mc.setWindowActive(true);
                    double targetYaw=Math.toRadians(mc.player.getYRot()+8),targetPitch=Math.toRadians(mc.player.getXRot());
                    Vec3 targetPoint=mc.player.getEyePosition().add(new Vec3(-Math.sin(targetYaw)*Math.cos(targetPitch),
                        -Math.sin(targetPitch),Math.cos(targetYaw)*Math.cos(targetPitch)).scale(10)).add(0,3,0);
                    double targetYOffset=aimTarget.getBbHeight()*0.62;
                    aimTarget.absSnapTo(targetPoint.x,targetPoint.y-targetYOffset,targetPoint.z,180,0);
                    Quirk.settings().module("aimassist").enabled.set(true);
                    initialAimYaw=mc.player.getYRot();
                }
                if(step==61) {
                    float corrected=AimAssist.delta(initialAimYaw,mc.player.getYRot());
                    Vec3 targetEye=aimTarget.position().add(AimAssist.predictedOffset(aimTarget.getDeltaMovement(),Quirk.settings().module("aimassist").number("prediction")))
                        .add(0,aimTarget.getBbHeight()*0.62,0);
                    var visibility=mc.level.clip(new net.minecraft.world.level.ClipContext(mc.player.getEyePosition(),targetEye,
                        net.minecraft.world.level.ClipContext.Block.COLLIDER,net.minecraft.world.level.ClipContext.Fluid.NONE,mc.player));
                    check(Math.abs(corrected)>0.01f&&Math.abs(corrected)<=Quirk.settings().module("aimassist").number("speed")+0.01,
                        "Aim assist makes a gradual, bounded correction (observed "+corrected+" degrees, target "+targetEye+", motion "+aimTarget.getDeltaMovement()+", LOS "+visibility.getType()+")");
                    Quirk.settings().module("aimassist").enabled.set(false);
                    f8(); check(mc.screen instanceof QuirkMenu,"F8 opens menu"); worldTime=mc.level.getGameTime();
                }
                if(step==90) { screenshot("02-menu.png"); check(!mc.isPaused(),"Menu does not pause client"); check(mc.level.getGameTime()>worldTime+5,"World keeps ticking underneath menu"); boolean playersWasOn=Quirk.settings().module("players").on(); click(380,62); check(Quirk.settings().module("players").on()!=playersWasOn,"Module column toggles immediately"); }
                if(step==100) { rightClick(380,86); }
                if(step==130) {
                    screenshot("03-storage-settings.png");
                    clickOption("chests",.5,12);
                    check(!Quirk.settings().module("storage").flag("chests"),"Nested toggle changes immediately");
                }
                if(step==140) {
                    mc.screen.mouseScrolled(500,400,0,-1);
                }
                if(step==150) {
                    clickOption("distance",.8,25);
                    double distance=Quirk.settings().module("storage").number("distance");
                    check(distance>128,"Compact settings slider responds to mouse position (observed "+distance+" m)");
                    initialColor=Quirk.settings().module("storage").color();
                    mc.screen.mouseScrolled(1000,400,0,0);
                }
                if(step==170) { clickOption("color",.2,33); check(Quirk.settings().module("storage").color()!=initialColor,"RGB color slider changes color"); } if(step==195) { screenshot("04-expanded-colors.png"); }
                if(step==200) { screenshot("04-expanded-colors.png"); mc.screen.keyPressed(new KeyEvent(GLFW_KEY_ESCAPE,0,0)); check(mc.screen==null,"ESC closes menu"); }
                if(step==210) { f8(); }
                if(step==220) { f8(); check(mc.screen==null,"F8 toggles menu closed"); Quirk.settings().module("freecam").enabled.set(true); check(Quirk.freecam(),"Freecam activates"); playerPosition=mc.player.position(); initialCamera=mc.gameRenderer.getMainCamera().position(); Quirk.turn(mc.player,60,-15); mc.setWindowActive(true); mc.options.keyUp.setDown(true); }
                if(step==240) { check(mc.player.position().distanceTo(playerPosition)<0.02,"Freecam keeps player position fixed"); check(mc.gameRenderer.getMainCamera().position().distanceTo(initialCamera)>0.5,"Freecam camera moves independently"); screenshot("05-freecam.png"); Quirk.settings().module("freecam").enabled.set(false); check(!Quirk.freecam(),"Freecam deactivates"); }
                if(step==250) f8();
                if(step==260) { click(230,62); check(Quirk.settings().module("freecam").on(),"Movement module toggles immediately"); }
                if(step==280) { screenshot("06-hud-category.png"); Quirk.store().flush(); check(Files.isRegularFile(mc.gameDirectory.toPath().resolve("quirk/settings.json")),"Settings persisted"); mc.screen.onClose(); mc.setScreen(new net.minecraft.client.gui.screens.inventory.InventoryScreen(mc.player)); f8(); check(mc.screen instanceof QuirkMenu,"F8 safely replaces inventory"); }
                if(step==290) {
                    mc.screen.onClose();Quirk.settings().module("freecam").enabled.set(false);
                    Quirk.settings().module("xray").enabled.set(false);
                    Quirk.settings().module("freelook").enabled.set(true);mc.setWindowActive(true);mc.options.setCameraType(net.minecraft.client.CameraType.FIRST_PERSON);Quirk.key(GLFW_PRESS,new KeyEvent(GLFW_KEY_LEFT_ALT,0,0));stage=4;step=0;
                }
            } else if(stage==4) {
                step++;mc.setWindowActive(true);
                if(step==5){check(mc.options.getCameraType()==net.minecraft.client.CameraType.THIRD_PERSON_BACK,"Freelook enters third person");float yaw=mc.player.getYRot();Quirk.turn(mc.player,100,30);check(mc.player.getYRot()==yaw,"Freelook rotates only the camera");}
                if(step==10){Quirk.key(GLFW_RELEASE,new KeyEvent(GLFW_KEY_LEFT_ALT,0,0));Quirk.settings().module("freelook").enabled.set(false);screenshot("07-night-fullbright.png");Quirk.settings().module("fullbright").enabled.set(false);}
                if(step==12)check(mc.options.getCameraType()==net.minecraft.client.CameraType.FIRST_PERSON,"Freelook restores previous perspective");
                if(step==25){screenshot("08-night-normal.png");Quirk.settings().module("fullbright").enabled.set(true);
                    Quirk.settings().module("fakepay").enabled.set(true);check(Quirk.interceptCommand("pay TestPlayer 100"),"Fake Pay intercepts payment before sending");check(!Quirk.interceptCommand("say pay"),"Fake Pay preserves unrelated commands");
                    Quirk.settings().module("fakestats").enabled.set(true);Quirk.settings().module("fakestats").get("title").set("LOCAL PREVIEW");
                    mc.getConnection().sendCommand("difficulty normal");mc.getConnection().sendCommand("gamemode survival");
                    mc.getConnection().sendCommand("item replace entity @s hotbar.2 with cooked_beef 1");
                    mc.getConnection().sendCommand("item replace entity @s inventory.0 with totem_of_undying");
                }
                if(step==45){
                    mc.getSingleplayerServer().execute(()->{var p=mc.getSingleplayerServer().getPlayerList().getPlayer(mc.player.getUUID());p.getFoodData().setFoodLevel(8);p.getFoodData().setSaturation(0);});
                    Quirk.settings().module("autoeat").enabled.set(true);
                }
                if(step==115){
                    check(mc.player.getFoodData().getFoodLevel()>8,"Auto Eat actually restores hunger ("+mc.player.getFoodData().getFoodLevel()+")");
                    check(mc.player.getInventory().getItem(2).isEmpty(),"Auto Eat consumes the food on the server");
                    check(mc.player.getInventory().getSelectedSlot()==0&&!mc.options.keyUse.isDown(),"Auto Eat releases use and restores the original slot");
                    Quirk.settings().module("autoeat").enabled.set(false);
                    Quirk.settings().module("invtotem").enabled.set(true);mc.setScreen(new net.minecraft.client.gui.screens.inventory.InventoryScreen(mc.player));
                }
                if(step==135){check(mc.player.getOffhandItem().is(net.minecraft.world.item.Items.TOTEM_OF_UNDYING),"Auto Inv Totem equips while inventory is open");mc.screen.onClose();Quirk.settings().module("invtotem").enabled.set(false);
                    mc.getConnection().sendCommand("difficulty peaceful");
                    mc.getConnection().sendCommand("item replace entity @s hotbar.3 with water_bucket");
                    mc.getConnection().sendCommand("effect give @s instant_health 1 10 true");
                    Quirk.settings().module("autoclutch").enabled.set(true);
                }
                if(step==155){check(mc.player.getHealth()>=19.9f,"Clutch fixture starts at full health");minimumClutchHealth=mc.player.getHealth();mc.getConnection().sendCommand("tp @s 20.5 -45 20.5 0 0");}
                if(step>155&&step<=215)minimumClutchHealth=Math.min(minimumClutchHealth,mc.player.getHealth());
                if(step==215){
                    check(mc.level.getBlockState(new BlockPos(20,-60,20)).is(net.minecraft.world.level.block.Blocks.WATER),"Auto Clutch places water at the landing block");
                    check(minimumClutchHealth>=19.9f,"Auto Clutch prevents fall damage (minimum health "+minimumClutchHealth+")");
                    check(mc.player.getInventory().getItem(3).is(net.minecraft.world.item.Items.BUCKET),"Auto Clutch consumes the water bucket contents");
                    Quirk.settings().module("autoclutch").enabled.set(false);screenshot("09-clutch.png");
                    mc.getConnection().sendCommand("item replace entity @s hotbar.0 with ender_pearl");
                    mc.getConnection().sendCommand("tp @s -20.25 -60 -20.75 0 -10");Quirk.settings().module("trajectory").enabled.set(true);
                }
                if(step==235){check(mc.player.blockPosition().getX()==-21&&mc.player.blockPosition().getZ()==-21,"Coordinates floor negative positions correctly");screenshot("10-pearl-trajectory.png");}
                if(step==245){
                    mc.getConnection().sendCommand("tp @s 9.5 -60 0.5 0 0");
                    mc.getConnection().sendCommand("item replace entity @s hotbar.4 with mace");
                    mc.getConnection().sendCommand("item replace entity @s hotbar.0 with air");
                    Quirk.settings().module("nametags").enabled.set(false);stage=5;step=0;
                }
            } else if(stage==5){
                step++;mc.setWindowActive(true);
                if(step==25){
                    net.minecraft.world.entity.LivingEntity target=null;
                    for(var entity:mc.level.entitiesForRendering())if(entity instanceof net.minecraft.world.entity.animal.cow.Cow cow&&cow.distanceTo(mc.player)<4){target=cow;break;}
                    check(target!=null,"Automace target fixture is in reach");maceTargetId=target.getId();maceTargetHealth=target.getHealth();
                    Quirk.settings().module("automace").get("falling").set(false);Quirk.settings().module("automace").enabled.set(true);
                    mc.hitResult=new net.minecraft.world.phys.EntityHitResult(target);
                    var method=Quirk.class.getDeclaredMethod("autoMace",Minecraft.class,net.minecraft.client.player.LocalPlayer.class);method.setAccessible(true);method.invoke(null,mc,mc.player);
                    check(mc.player.getMainHandItem().is(net.minecraft.world.item.Items.MACE),"Automace selects the hotbar mace for the attack");
                    Quirk.settings().module("automace").enabled.set(false);
                }
                if(step==45){var target=mc.level.getEntity(maceTargetId);check(target==null||((net.minecraft.world.entity.LivingEntity)target).getHealth()<maceTargetHealth,"Automace attack damages the server-side target");check(mc.player.getInventory().getSelectedSlot()==0,"Automace restores the previous slot");screenshot("11-automace.png");
                    f8();
                }
                if(step==55)rightClickModule("fakestats");
                
                if(step==70){clickOption("title",.5,12);mc.screen.keyPressed(new KeyEvent(GLFW_KEY_A,0,GLFW_MOD_CONTROL));check(Quirk.settings().module("fakestats").get("title").choice().equals("LOCAL PREVIEW"),"Select-all keeps text until replacement");mc.screen.charTyped(new CharacterEvent(81,0));check(Quirk.settings().module("fakestats").get("title").choice().equals("Q"),"Text settings update immediately");} if(step==75){screenshot("12-editable-settings.png");mc.screen.onClose();Quirk.store().flush();Quirk.settings().module("fly").enabled.set(true);stage=6;step=0;}
            } else if(stage==6){
                step++;mc.setWindowActive(true);
                if(step==10){check(mc.player.getAbilities().flying,"Fly enables flight in an integrated world");Quirk.settings().module("fly").enabled.set(false);}
                if(step==20){check(!mc.player.getAbilities().flying&&!mc.player.getAbilities().mayfly,"Fly restores survival abilities when disabled");
                    mc.getConnection().sendCommand("item replace entity @s armor.chest with elytra");mc.getConnection().sendCommand("tp @s 0 40 0");}
                if(step==35){var server=mc.getSingleplayerServer();server.execute(()->server.getPlayerList().getPlayer(mc.player.getUUID()).startFallFlying());mc.player.startFallFlying();mc.player.setXRot(20);Quirk.settings().module("elytraglide").enabled.set(true);}
                if(step==45){check(mc.player.isFallFlying()&&mc.player.getDeltaMovement().length()>.4,"Elytra Glide provides sustained momentum without rockets");Quirk.settings().module("elytraglide").enabled.set(false);Quirk.settings().module("freecam").enabled.set(true);stage=3;
                    mc.schedule(()->{mc.level.disconnect(net.minecraft.network.chat.Component.literal("Smoke test complete"));mc.disconnectWithSavingScreen();});}
            } else if(stage==3 && mc.level==null) {
                check(!Quirk.freecam()&&!Quirk.settings().module("freecam").on(),"Disconnect disarms freecam"); finish(true,"All in-game smoke assertions passed.");
            }
            if(ticks>7000) finish(false,"Timed out waiting for game: "+(mc.screen==null?"none":mc.screen.getClass().getName()));
        } catch(Throwable e) { e.printStackTrace(); finish(false,e.toString()); }
    }
    private static void f8() { Quirk.key(GLFW_PRESS,new KeyEvent(GLFW_KEY_F8,0,0)); }
    private static void rightClickModule(String id) throws Exception {
        var screen=Minecraft.getInstance().screen;Field field=screen.getClass().getDeclaredField("moduleRows");field.setAccessible(true);
        @SuppressWarnings("unchecked") var rows=(java.util.Map<Settings.Module,int[]>)field.get(screen);
        int[] row=rows.get(Quirk.settings().module(id));check(row!=null,"Module is visible before right click: "+id);rightClick(row[0]+row[2]/2.0,row[1]+row[3]/2.0);
    }
    private static void clickOption(String id,double fraction,int offsetY) throws Exception {
        var screen=Minecraft.getInstance().screen;Field field=screen.getClass().getDeclaredField("optionRows");field.setAccessible(true);
        @SuppressWarnings("unchecked") var rows=(java.util.Map<String,int[]>)field.get(screen);
        int[] row=rows.get(id);check(row!=null,"Setting is visible before click: "+id);click(row[0]+row[2]*fraction,row[1]+offsetY);
    }
    private static double field(Object o,String name) throws Exception { Field f=o.getClass().getDeclaredField(name); f.setAccessible(true); return ((Number)f.get(o)).doubleValue(); }
    private static void click(double x,double y) throws Exception {
        var screen=Minecraft.getInstance().screen; check(screen instanceof QuirkMenu,"Menu exists before click");
        MouseButtonEvent e=new MouseButtonEvent(field(screen,"originX")+x*field(screen,"scale"),field(screen,"originY")+y*field(screen,"scale"),new MouseButtonInfo(0,0));
        screen.mouseClicked(e,false); screen.mouseReleased(e);
    }
    private static void rightClick(double x,double y) throws Exception {
        var screen=Minecraft.getInstance().screen; check(screen instanceof QuirkMenu,"Menu exists before opening module settings");
        MouseButtonEvent e=new MouseButtonEvent(field(screen,"originX")+x*field(screen,"scale"),field(screen,"originY")+y*field(screen,"scale"),new MouseButtonInfo(1,0));
        screen.mouseClicked(e,false);
    }
    private static void screenshot(String name) { var mc=Minecraft.getInstance(); Screenshot.grab(mc.gameDirectory,name,mc.getMainRenderTarget(),1,c->System.out.println("[Plutonium smoke] "+name)); }
    private static void check(boolean result,String description) { if(!result) throw new AssertionError(description); System.out.println("[Plutonium smoke] PASS: "+description); }
    private static void finish(boolean ok,String message) {
        if(stage==99) return; stage=99;
        try { Files.writeString(Minecraft.getInstance().gameDirectory.toPath().resolve("smoke-result.txt"),(ok?"PASS\n":"FAIL\n")+message); } catch(Exception ignored) {}
        System.out.println("[Plutonium smoke] "+message); Minecraft.getInstance().stop();
    }
}
