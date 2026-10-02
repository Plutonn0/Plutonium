package com.quirk.client;

import com.mojang.blaze3d.buffers.*;
import com.mojang.blaze3d.pipeline.*;
import com.mojang.blaze3d.platform.*;
import com.mojang.blaze3d.shaders.UniformType;
import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.blaze3d.vertex.*;
import net.minecraft.client.Minecraft;
import net.minecraft.world.phys.*;
import org.joml.Matrix4f;
import org.lwjgl.system.MemoryStack;
import java.util.OptionalInt;

/** Double-precision camera subtraction followed by the engine's exact view/projection matrices. */
public final class WorldDraw {
    private static final RenderPipeline PIPELINE=RenderPipeline.builder()
        .withLocation("pipeline/quirk_world").withVertexShader("core/quirk_world").withFragmentShader("core/quirk_world")
        .withUniform("QuirkMatrices",UniformType.UNIFORM_BUFFER)
        .withVertexFormat(DefaultVertexFormat.POSITION_COLOR,VertexFormat.Mode.QUADS)
        .withBlend(BlendFunction.TRANSLUCENT).withCull(false).withDepthWrite(false)
        .withDepthTestFunction(DepthTestFunction.NO_DEPTH_TEST).build();
    private static final int[][] FACES={{0,1,3,2},{4,6,7,5},{0,4,5,1},{2,3,7,6},{0,2,6,4},{1,5,7,3}};
    private static final int[][] EDGES={{0,1},{0,2},{0,4},{1,3},{1,5},{2,3},{2,6},{3,7},{4,5},{4,6},{5,7},{6,7}};
    private static final ByteBufferBuilder MEMORY=new ByteBufferBuilder(262144);
    private static GpuBuffer uniform;
    private final BufferBuilder vertices=new BufferBuilder(MEMORY,VertexFormat.Mode.QUADS,DefaultVertexFormat.POSITION_COLOR);
    private final Vec3 eye;
    private final Matrix4f matrix;
    private final double pixelScale;
    public WorldDraw(Vec3 eye,Matrix4f view,Matrix4f projection){this.eye=eye;matrix=new Matrix4f(projection).mul(view);pixelScale=2.0/(Math.abs(projection.m11())*Minecraft.getInstance().getWindow().getHeight());}
    private void vertex(Vec3 p,int color){vertices.addVertex((float)p.x,(float)p.y,(float)p.z).setColor(color);}
    public void box(AABB bounds,int color,boolean fill){
        Vec3[] v=new Vec3[8];for(int i=0;i<8;i++)v[i]=new Vec3((i&1)==0?bounds.minX:bounds.maxX,(i&2)==0?bounds.minY:bounds.maxY,(i&4)==0?bounds.minZ:bounds.maxZ).subtract(eye);
        if(fill)for(int[] f:FACES)for(int i:f)vertex(v[i],(color&0xffffff)|0x18000000);
        for(int[] e:EDGES)relativeLine(v[e[0]],v[e[1]],color,1.5);
    }
    public void line(Vec3 a,Vec3 b,int color,double thickness){relativeLine(a.subtract(eye),b.subtract(eye),color,thickness);}
    private void relativeLine(Vec3 a,Vec3 b,int color,double thickness){
        Vec3 direction=b.subtract(a);if(direction.lengthSqr()<1e-12)return;
        Vec3 side=direction.cross(a.add(b).scale(.5)).normalize();if(side.lengthSqr()<1e-8)side=new Vec3(0,1,0).cross(direction).normalize();
        double half=pixelScale*thickness*.5;
        Vec3 sa=side.scale(Math.max(.1,a.length())*half),sb=side.scale(Math.max(.1,b.length())*half);
        vertex(a.add(sa),color);vertex(b.add(sb),color);vertex(b.subtract(sb),color);vertex(a.subtract(sa),color);
    }
    public void finish(){
        try(MeshData mesh=vertices.build()){
            if(mesh==null)return;
            var device=RenderSystem.getDevice();var encoder=device.createCommandEncoder();
            if(uniform==null)uniform=device.createBuffer(()->"Quirk world matrix",GpuBuffer.USAGE_UNIFORM|GpuBuffer.USAGE_COPY_DST,64);
            try(MemoryStack stack=MemoryStack.stackPush()){encoder.writeToBuffer(uniform.slice(),Std140Builder.onStack(stack,64).putMat4f(matrix).get());}
            var buffer=DefaultVertexFormat.POSITION_COLOR.uploadImmediateVertexBuffer(mesh.vertexBuffer());
            var indices=RenderSystem.getSequentialBuffer(VertexFormat.Mode.QUADS);int count=mesh.drawState().indexCount();var indexBuffer=indices.getBuffer(count);
            try(var pass=encoder.createRenderPass(()->"Quirk world highlights",Minecraft.getInstance().getMainRenderTarget().getColorTextureView(),OptionalInt.empty())){
                pass.setPipeline(PIPELINE);pass.setUniform("QuirkMatrices",uniform);pass.setVertexBuffer(0,buffer);pass.setIndexBuffer(indexBuffer,indices.type());pass.drawIndexed(0,0,count,1);
            }
        }
    }
}
