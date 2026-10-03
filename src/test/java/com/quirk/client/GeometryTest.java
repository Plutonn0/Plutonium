package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class GeometryTest {
    @Test void tracerOriginStaysAtCrosshairWithOrWithoutCameraBobbing() {
        var projection=new org.joml.Matrix4f().perspective((float)Math.toRadians(80),16f/9f,.05f,512);
        for(boolean bob:new boolean[]{false,true})for(int frame=0;frame<40;frame++){
            var view=new org.joml.Matrix4f().rotateX(frame*.015f).rotateY(frame*.04f);
            if(bob)view.translate((float)Math.sin(frame)*.03f,(float)Math.cos(frame)*.02f,0).rotateZ(.025f);
            var matrix=new org.joml.Matrix4f(projection).mul(view);
            var point=new org.joml.Vector4f(Geometry.tracerOrigin(matrix),1).mul(matrix);
            assertEquals(0,point.x/point.w,0.00001);assertEquals(0,point.y/point.w,0.00001);
        }
    }
    @Test void crossingLineClipsAtBothEdges() { assertArrayEquals(new double[]{0,50,100,50},Geometry.clip(-500,50,500,50,100,100),0.00001); }
    @Test void behindViewportAndNonFiniteLinesAreDiscarded() {
        assertNull(Geometry.clip(-100,-10,200,-10,100,100)); assertNull(Geometry.clip(1,Double.NaN,2,3,100,100));
    }
    @Test void verticalLineAndDegeneratePointAreStable() {
        assertArrayEquals(new double[]{50,0,50,100},Geometry.clip(50,-200,50,400,100,100),0.00001);
        assertArrayEquals(new double[]{4,4,4,4},Geometry.clip(4,4,4,4,100,100));
    }
}
