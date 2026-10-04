package com.quirk.client;

import net.minecraft.world.phys.Vec3;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class GlideMotionTest {
    @Test void gainsMomentumWithoutRocketInput(){
        Vec3 motion=Vec3.ZERO;
        for(int i=0;i<40;i++)motion=GlideMotion.accelerate(motion,new Vec3(0,0,1),1.2,.08);
        assertEquals(1.2,motion.z,1e-9);assertEquals(0,motion.y,1e-9);
    }
    @Test void climbingUsesLookDirectionAndAccelerationLimit(){
        Vec3 initial=new Vec3(0,0,1);
        Vec3 next=GlideMotion.accelerate(initial,new Vec3(0,1,1),1.2,.08);
        assertTrue(next.y>0);assertTrue(next.distanceTo(initial)<=.080000001);
    }
    @Test void doesNotSnapOrAcceleratePastTarget(){
        Vec3 current=new Vec3(0,0,1.19);
        assertEquals(1.2,GlideMotion.accelerate(current,new Vec3(0,0,1),1.2,.08).z,1e-9);
        assertTrue(GlideMotion.accelerate(new Vec3(0,0,2),new Vec3(0,0,1),1.2,.08).z<2);
    }
}
