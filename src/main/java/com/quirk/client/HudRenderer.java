package com.quirk.client;
import com.quirk.client.ui.Paint;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import java.util.*;
import static com.quirk.client.ui.Paint.*;

public final class HudRenderer {
    private static final Map<String,HudLayout.Bounds> frames=new LinkedHashMap<>();
    public static Map<String,HudLayout.Bounds> frames(){return Map.copyOf(frames);}
    public static void begin(GuiGraphics g,String id,int width,int height){
        var settings=Quirk.settings();float scale=(float)(settings.module(id).number("scale")/100);
        var b=settings.hud.bounds(id,g.guiWidth(),g.guiHeight(),width,height,scale);frames.put(id,b);
        g.pose().pushMatrix();g.pose().translate(b.x(),b.y());g.pose().scale(b.scale(),b.scale());
    }
    public static void render(GuiGraphics g,boolean preview){
        frames.clear();var mc=Minecraft.getInstance();var s=Quirk.settings();
        if(mc.player==null)return;
        if(preview||s.module("coordinates").on()){
            var p=mc.player.blockPosition();String[] rows={"X: "+p.getX(),"Y: "+p.getY(),"Z: "+p.getZ()};int width=Arrays.stream(rows).mapToInt(Paint::width).max().orElse(45)+12;
            begin(g,"coordinates",width,41);g.fill(0,0,width,41,0xd9111111);g.fill(0,0,1,41,0xffcccccc);
            for(int i=0;i<3;i++)hudText(g,rows[i],6,2.5f+12*i,12,TEXT);g.pose().popMatrix();
        }
        if(preview||s.module("active").on()){
            var m=s.module("active");List<String> names=new ArrayList<>(s.modules.stream().filter(v->v.on()&&!v.category.equals("HUD")&&!v.id.equals("nametags")).map(v->v.name).sorted(Comparator.comparingInt(Paint::width).reversed()).toList());
            if(preview&&names.isEmpty())names.addAll(List.of("Player ESP","Fullbright","Sprint"));
            int slots=Math.max(2,(int)((g.guiHeight()-12)*.33/(16*m.number("scale")/100)));int limit=Math.min((int)m.number("rows"),slots);
            if(names.size()>limit){int extra=names.size()-Math.max(1,limit-1);names=new ArrayList<>(names.subList(0,Math.max(1,limit-1)));names.add("+ "+extra+" more");}
            if(!names.isEmpty()){
                int width=names.stream().mapToInt(Paint::width).max().orElse(60)+12;begin(g,"active",width,names.size()*16-1);int y=0;
                for(String name:names){int tw=Paint.width(name);g.fill(width-tw-12,y,width,y+15,0xd9111111);g.fill(width-1,y,width,y+15,0xffcccccc);hudText(g,name,width-tw-7,y,15,TEXT);y+=16;}g.pose().popMatrix();
            }
        }
        if(preview||s.module("fakestats").on()){
            var sidebar=FakeStats.display(preview);
            if(sidebar!=null){
                int width=Math.max(100,Paint.width(sidebar.title())+16);for(var row:sidebar.rows())width=Math.max(width,Paint.width(row.label())+Paint.width(row.value())+24);
                width=Math.min(width,420);begin(g,"fakestats",width,24+sidebar.rows().size()*16);
                g.fill(0,0,width,24+sidebar.rows().size()*16,0xd9111111);hudText(g,fit(sidebar.title(),width-12),6,2,20,TEXT);g.fill(6,22,width-6,23,0xff444444);
                int y=24;for(var row:sidebar.rows()){String value=fit(row.value(),width/2),label=fit(row.label(),width-20-Paint.width(value));hudText(g,label,6,y,16,MUTED);hudText(g,value,width-6-Paint.width(value),y,16,TEXT);y+=16;}g.pose().popMatrix();
            }
        }
        Notifications.render(g,preview);
    }
    public static String fit(String text,int width){if(Paint.width(text)<=width)return text;while(!text.isEmpty()&&Paint.width(text+"…")>width)text=text.substring(0,text.offsetByCodePoints(text.length(),-1));return text+"…";}
}
