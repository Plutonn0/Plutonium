package com.quirk.client;

import com.quirk.client.ui.Paint;
import net.minecraft.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.core.BlockPos;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.monster.Enemy;
import net.minecraft.world.item.Items;
import net.minecraft.world.level.ClipContext;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.*;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.Heightmap;
import net.minecraft.world.phys.*;
import net.minecraft.world.phys.shapes.VoxelShape;
import org.joml.Matrix4f; import org.joml.Vector4f;
import java.util.*;
import static com.quirk.client.ui.Paint.*;

public final class Overlay {
    private final List<BlockEntity> blocks=new ArrayList<>();
    private final Map<Long,List<BlockPos>> debris=new HashMap<>();
    private final ArrayDeque<long[]> scanQueue=new ArrayDeque<>();
    private final List<Label> labels=new ArrayList<>();
    private final SusChunkTracker susChunks=new SusChunkTracker();
    private Object level;
    private int untilScan,activityTicks,debrisCooldown;
    private record Label(Vec3 position,String text,int color){}
    private Matrix4f viewProjection;
    private Vec3 eye;
    public void invalidate(){untilScan=0;}
    public void tick(){
        Minecraft mc=Minecraft.getInstance();
        if(mc.level!=level){level=mc.level;blocks.clear();debris.clear();scanQueue.clear();susChunks.clear();untilScan=0;debrisCooldown=0;viewProjection=null;}
        if(mc.level==null||mc.player==null)return;
        Settings s=Quirk.settings();
        if(s.module("suschunk").on()&&++activityTicks>=5){activityTicks=0;for(var p:mc.level.players())if(p!=mc.player&&p.isAlive())susChunks.observe(p.getUUID(),p.getX(),p.getZ());}
        scanDebris(mc,s.module("netherite"));
        if(--untilScan>0)return;untilScan=10;blocks.clear();
        var st=s.module("storage");var sp=s.module("spawners");var tr=s.module("tracers");
        boolean storage=st.on()||(tr.on()&&tr.flag("storage")),spawners=sp.on()||(tr.on()&&tr.flag("spawners"));if(!storage&&!spawners)return;
        double distance=Math.max(storage?st.number("distance"):0,spawners?sp.number("distance"):0);if(tr.on())distance=Math.max(distance,tr.number("distance"));
        int radius=(int)Math.ceil(distance/16)+1;BlockPos origin=mc.gameRenderer.getMainCamera().blockPosition();int cx=origin.getX()>>4,cz=origin.getZ()>>4;
        for(int x=cx-radius;x<=cx+radius;x++)for(int z=cz-radius;z<=cz+radius;z++){
            var chunk=mc.level.getChunkSource().getChunk(x,z,ChunkStatus.FULL,false);if(chunk==null)continue;
            for(BlockEntity be:chunk.getBlockEntities().values())if((storage&&isStorage(be,st))||(spawners&&be instanceof SpawnerBlockEntity))blocks.add(be);
        }
    }
    private void scanDebris(Minecraft mc,Settings.Module m){
        if(!m.on()){debris.clear();scanQueue.clear();return;}
        BlockPos origin=mc.gameRenderer.getMainCamera().blockPosition();int cx=origin.getX()>>4,cz=origin.getZ()>>4,r=(int)Math.ceil(m.number("distance")/16);
        debris.keySet().removeIf(k->Math.abs((int)(k>>32)-cx)>r||Math.abs((int)(long)k-cz)>r||!mc.level.hasChunk((int)(k>>32),(int)(long)k));
        if(scanQueue.isEmpty()&&--debrisCooldown<=0){for(int x=cx-r;x<=cx+r;x++)for(int z=cz-r;z<=cz+r;z++)scanQueue.add(new long[]{x,z});debrisCooldown=40;}
        for(int budget=0;budget<2&&!scanQueue.isEmpty();budget++){
            long[] p=scanQueue.removeFirst();int x=(int)p[0],z=(int)p[1];if(Math.abs(x-cx)>r||Math.abs(z-cz)>r)continue;
            var chunk=mc.level.getChunkSource().getChunk(x,z,ChunkStatus.FULL,false);if(chunk==null)continue;
            List<BlockPos> found=new ArrayList<>();var sections=chunk.getSections();
            for(int i=0;i<sections.length;i++){var section=sections[i];if(!section.maybeHas(state->state.is(Blocks.ANCIENT_DEBRIS)))continue;
                for(int bx=0;bx<16;bx++)for(int by=0;by<16;by++)for(int bz=0;bz<16;bz++)if(section.getBlockState(bx,by,bz).is(Blocks.ANCIENT_DEBRIS))found.add(new BlockPos(x*16+bx,mc.level.getMinY()+i*16+by,z*16+bz));}
            debris.put(((long)x<<32)|(z&0xffffffffL),found);
        }
    }
    private boolean isStorage(BlockEntity be,Settings.Module m){return be instanceof ChestBlockEntity&&m.flag("chests")||be instanceof BarrelBlockEntity&&m.flag("barrels")||be instanceof ShulkerBoxBlockEntity&&m.flag("shulker")||be instanceof HopperBlockEntity&&m.flag("hoppers")||be instanceof AbstractFurnaceBlockEntity&&m.flag("furnaces")||be instanceof DispenserBlockEntity&&m.flag("dispensers");}
    private int storageColor(BlockEntity be,Settings.Module m){
        if(!m.flag("perblock"))return m.color();
        String id=be instanceof ChestBlockEntity?"chestcolor":be instanceof BarrelBlockEntity?"barrelcolor":be instanceof ShulkerBoxBlockEntity?"shulkercolor":be instanceof HopperBlockEntity?"hoppercolor":be instanceof AbstractFurnaceBlockEntity?"furnacecolor":"dispensercolor";
        return m.get(id).color();
    }
    public void world(Camera camera,Matrix4f view,Matrix4f projection,DeltaTracker delta){
        Minecraft mc=Minecraft.getInstance();if(mc.level==null||mc.player==null)return;
        eye=camera.position();viewProjection=new Matrix4f(projection).mul(view);labels.clear();
        WorldDraw draw=new WorldDraw(eye,view,projection);Settings s=Quirk.settings();float partial=delta.getGameTimeDeltaPartialTick(false);
        var pl=s.module("players");var st=s.module("storage");var sp=s.module("spawners");var tr=s.module("tracers");var mobs=s.module("mobs");
        Vec3 start=eye.add(new Vec3(Geometry.tracerOrigin(viewProjection)));
        for(var entity:mc.level.entitiesForRendering()){
            boolean player=entity instanceof net.minecraft.world.entity.player.Player;if(entity==mc.player||!entity.isAlive())continue;
            Settings.Module m=player?pl:mobs;
            if(!player&&(!(entity instanceof Mob)||!mobs.on()||(entity instanceof Enemy?!mobs.flag("hostile"):!mobs.flag("passive"))))continue;
            Vec3 pos=entity.getPosition(partial);double distance=pos.distanceTo(eye);AABB bounds=entity.getBoundingBox().move(pos.subtract(entity.position()));
            if(m.on()&&distance<=m.number("distance"))draw.box(bounds,m.color(),m.get("style").choice().equals("Filled"));
            if(player&&tr.on()&&tr.flag("players")&&distance<=tr.number("distance"))draw.line(start,bounds.getCenter(),tr.color(),tr.number("thickness"));
        }
        for(BlockEntity be:blocks){
            BlockPos pos=be.getBlockPos();if(be.isRemoved()||!mc.level.hasChunk(pos.getX()>>4,pos.getZ()>>4))continue;
            boolean spawn=be instanceof SpawnerBlockEntity;Settings.Module m=spawn?sp:st;if(!spawn&&!isStorage(be,st))continue;
            Vec3 center=Vec3.atCenterOf(pos);double distance=center.distanceTo(eye);
            if(m.on()&&distance<=m.number("distance")){
                draw.box(blockBounds(be.getBlockState().getShape(mc.level,pos),pos),spawn?m.color():storageColor(be,m),m.get("style").choice().equals("Filled"));
                if(spawn&&sp.flag("mob")){var entity=((SpawnerBlockEntity)be).getSpawner().getOrCreateDisplayEntity(mc.level,pos);labels.add(new Label(center.add(0,.9,0),entity==null?"Spawner":entity.getType().getDescription().getString(),sp.color()));}
            }
            if(tr.on()&&tr.flag(spawn?"spawners":"storage")&&distance<=tr.number("distance"))draw.line(start,center,tr.color(),tr.number("thickness"));
            else if(spawn&&sp.on()&&sp.flag("tracer")&&distance<=sp.number("distance"))draw.line(start,center,sp.color(),1);
        }
        var netherite=s.module("netherite");if(netherite.on())for(var positions:debris.values())for(BlockPos pos:positions)if(Vec3.atCenterOf(pos).distanceTo(eye)<=netherite.number("distance")&&mc.level.getBlockState(pos).is(Blocks.ANCIENT_DEBRIS))draw.box(new AABB(pos),netherite.color(),netherite.get("style").choice().equals("Filled"));
        if(s.module("suschunk").on())for(var chunk:susChunks.busiest((int)s.module("suschunk").number("threshold"),32)){
            int x=chunk.x()*16,z=chunk.z()*16;if(!mc.level.hasChunk(chunk.x(),chunk.z()))continue;
            // A red block grid on the chunk's surface is anchored to integer world coordinates.
            for(int edge=0;edge<16;edge+=3)for(int side=0;side<4;side++){int bx=x+(side==0?0:side==1?15:edge),bz=z+(side==2?0:side==3?15:edge);int y=mc.level.getHeight(Heightmap.Types.MOTION_BLOCKING,bx,bz)-1;draw.box(new AABB(new BlockPos(bx,y,bz)),0xffe75858,true);}
        }
        if(s.module("trajectory").on()&&(mc.player.getMainHandItem().is(Items.ENDER_PEARL)||mc.player.getOffhandItem().is(Items.ENDER_PEARL)))pearl(draw,mc,s.module("trajectory").color());
        draw.finish();
    }
    private void pearl(WorldDraw draw,Minecraft mc,int color){
        var player=mc.player;Vec3 p=player.getEyePosition().add(0,-.1,0),movement=player.getDeltaMovement();
        Vec3 velocity=player.getLookAngle().scale(1.5).add(movement.x,player.onGround()?0:movement.y,movement.z);
        for(int i=0;i<160;i++){
            Vec3 next=p.add(velocity);HitResult hit=mc.level.clip(new ClipContext(p,next,ClipContext.Block.COLLIDER,ClipContext.Fluid.NONE,player));double closest=hit.getType()==HitResult.Type.MISS?Double.POSITIVE_INFINITY:p.distanceToSqr(hit.getLocation());
            for(var entity:mc.level.getEntities(player,new AABB(p,next).inflate(1),e->e.isPickable()&&!e.isSpectator())){var intersection=entity.getBoundingBox().inflate(.125).clip(p,next);if(intersection.isPresent()&&p.distanceToSqr(intersection.get())<closest){hit=new EntityHitResult(entity,intersection.get());closest=p.distanceToSqr(hit.getLocation());}}
            if(hit.getType()!=HitResult.Type.MISS){draw.line(p,hit.getLocation(),color,1.8);draw.box(new AABB(hit.getLocation(),hit.getLocation()).inflate(.12),color,true);break;}
            draw.line(p,next,color,1.8);p=next;velocity=velocity.scale(mc.level.getFluidState(BlockPos.containing(p)).isEmpty()?.99:.8).add(0,-.03,0);if(p.y<mc.level.getMinY()-16)break;
        }
    }
    static AABB blockBounds(VoxelShape shape,BlockPos pos){return shape.isEmpty()?new AABB(pos):shape.bounds().move(pos);}
    public void render(GuiGraphics g){
        Minecraft mc=Minecraft.getInstance();if(mc.player==null)return;Settings s=Quirk.settings();int w=g.guiWidth(),h=g.guiHeight();
        if(viewProjection!=null&&eye!=null)for(Label label:labels){Vec3 r=label.position.subtract(eye);Vector4f p=new Vector4f((float)r.x,(float)r.y,(float)r.z,1).mul(viewProjection);if(p.w<=0||Math.abs(p.x)>p.w||Math.abs(p.y)>p.w)continue;int x=(int)((p.x/p.w+1)*w*.5),y=(int)((1-p.y/p.w)*h*.5),tw=width(label.text);g.fill(x-tw/2-4,y-3,x+tw/2+4,y+12,0xb0000000);text(g,label.text,x-tw/2,y,label.color);}
        if(s.module("coordinates").on()){
            BlockPos p=mc.player.blockPosition();String[] lines={"X: "+p.getX(),"Y: "+p.getY(),"Z: "+p.getZ()};int bw=Arrays.stream(lines).mapToInt(Paint::width).max().orElse(45)+12;
            g.pose().pushMatrix();g.pose().translate(8,8);g.fill(0,0,bw+8,51,0xd9111111);g.fill(0,0,1,51,0xffcccccc);for(int i=0;i<3;i++)text(g,lines[i],10,5+14*i,TEXT);g.pose().popMatrix();
        }
        if(s.module("active").on()){
            var module=s.module("active");float scale=(float)(module.number("scale")/100);
            var enabled=s.modules.stream().filter(m->m.on()&&!m.category.equals("HUD")&&!m.id.equals("nametags")).sorted(java.util.Comparator.comparingInt((Settings.Module m)->width(m.name)).reversed()).toList();
            int slots=Math.max(2,(int)((h-12)*.33/(16*scale)));
            int visible=Math.min(enabled.size(),Math.min((int)module.number("rows"),slots));
            if(visible<enabled.size())visible=Math.min(visible,slots-1);
            g.pose().pushMatrix();g.pose().translate(w-6,6);g.pose().scale(scale,scale);
            int y=0;
            for(int i=0;i<visible+(visible<enabled.size()?1:0);i++){
                String name=i<visible?enabled.get(i).name:"+ "+(enabled.size()-visible)+" more";
                int tw=width(name);g.fill(-tw-12,y,0,y+15,0xd9111111);g.fill(-1,y,0,y+15,0xffcccccc);
                text(g,name,-tw-7,y-1,TEXT);y+=16;
            }
            g.pose().popMatrix();
        }
        if(s.module("fakestats").on()){
            var m=s.module("fakestats");List<String> rows=new ArrayList<>();rows.add(m.get("title").choice());rows.addAll(Arrays.stream(m.get("lines").choice().split("\\|",-1)).limit(15).toList());int bw=Math.min(w/2,rows.stream().mapToInt(Paint::width).max().orElse(100)+16),y=h/2-rows.size()*8;
            g.fill(w-bw-7,y-6,w-7,y+rows.size()*16+4,0xbf000000);g.enableScissor(w-bw-7,y-6,w-7,y+rows.size()*16+4);for(String row:rows){text(g,row,w-bw/2-7-width(row)/2,y,TEXT);y+=16;}g.disableScissor();
        }
        Notifications.render(g);
    }
}

