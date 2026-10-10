package com.quirk.client;

import com.google.gson.*;
import java.util.*;

/** Positions are fractions of the available screen area, so they survive GUI-scale changes. */
public final class HudLayout {
    public static final List<String> IDS=List.of("coordinates","active","notifications","fakestats");
    public record Position(double x,double y) {}
    public record Bounds(float x,float y,float width,float height,float scale) {
        public boolean contains(double px,double py){return px>=x&&py>=y&&px<=x+width&&py<=y+height;}
    }
    private final Map<String,Position> positions=new HashMap<>();
    private Runnable changed=()->{};
    public void onChange(Runnable callback){changed=callback;}
    public Position position(String id){return positions.getOrDefault(id,switch(id){case "active"->new Position(1,0);case "notifications"->new Position(0,1);case "fakestats"->new Position(1,.5);default->new Position(0,0);});}
    public void set(String id,double x,double y){if(!IDS.contains(id)||!Double.isFinite(x)||!Double.isFinite(y))return;var p=new Position(Math.clamp(x,0,1),Math.clamp(y,0,1));if(!p.equals(position(id))){positions.put(id,p);changed.run();}}
    public void reset(String id){if(positions.remove(id)!=null)changed.run();}
    public void resetAll(){positions.clear();changed.run();}
    public Bounds bounds(String id,int screenW,int screenH,float width,float height,float requestedScale){
        float margin=Math.min(6,Math.min(screenW,screenH)/4f);
        float scale=Math.min(requestedScale,Math.min(Math.max(1,screenW-2*margin)/Math.max(1,width),Math.max(1,screenH-2*margin)/Math.max(1,height)));
        var p=position(id);float w=width*scale,h=height*scale;
        return new Bounds(margin+(float)p.x*Math.max(0,screenW-2*margin-w),margin+(float)p.y*Math.max(0,screenH-2*margin-h),w,h,scale);
    }
    public void move(String id,double x,double y,int screenW,int screenH,Bounds b){set(id,(x-6)/Math.max(1,screenW-12-b.width),(y-6)/Math.max(1,screenH-12-b.height));}
    public static double snap(double edge,double size,double screenSize,double tolerance){
        for(double target:new double[]{6,(screenSize-size)/2,screenSize-6-size})if(Math.abs(edge-target)<=tolerance)return target;
        return edge;
    }
    public JsonObject save(){var root=new JsonObject();for(String id:IDS){var p=position(id);var row=new JsonObject();row.addProperty("x",p.x);row.addProperty("y",p.y);root.add(id,row);}return root;}
    public void load(JsonObject root){for(String id:IDS)try{if(root.has(id)){var row=root.getAsJsonObject(id);set(id,row.get("x").getAsDouble(),row.get("y").getAsDouble());}}catch(RuntimeException ignored){}}
}
