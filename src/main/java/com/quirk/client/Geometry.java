package com.quirk.client;

public final class Geometry {
    private Geometry() {}
    /** Unproject the crosshair through the actual rendered matrix, including optional camera effects. */
    public static org.joml.Vector3f tracerOrigin(org.joml.Matrix4f viewProjection) {
        var p = new org.joml.Vector4f(0, 0, 0, 1).mul(new org.joml.Matrix4f(viewProjection).invert());
        return new org.joml.Vector3f(p.x / p.w, p.y / p.w, p.z / p.w);
    }
    /** Liang-Barsky clipping avoids huge offscreen geometry close to the camera. */
    public static double[] clip(double ax,double ay,double bx,double by,double width,double height) {
        if(!Double.isFinite(ax+ay+bx+by)) return null;
        double dx=bx-ax,dy=by-ay,low=0,high=1;
        double[] p={-dx,dx,-dy,dy},q={ax,width-ax,ay,height-ay};
        for(int i=0;i<4;i++) {
            if(p[i]==0) { if(q[i]<0) return null; }
            else { double r=q[i]/p[i]; if(p[i]<0) low=Math.max(low,r); else high=Math.min(high,r); if(low>high) return null; }
        }
        return new double[]{ax+low*dx,ay+low*dy,ax+high*dx,ay+high*dy};
    }
}
