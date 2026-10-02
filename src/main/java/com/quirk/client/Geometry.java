package com.quirk.client;

public final class Geometry {
    private Geometry() {}
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
