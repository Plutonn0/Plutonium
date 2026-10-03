package com.quirk.client;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import com.quirk.client.ui.Paint;
import java.util.*;

public final class Notifications {
    private record Notice(String title,String description,long start,long duration){}
    private static final Deque<Notice> queue=new ArrayDeque<>();
    private static final Map<String,Long> cooldowns=new HashMap<>();
    private static Object level;
    private static int weather=-1;
    public static void show(String key,String text,int cooldownSeconds){
        String detail=switch(key){
            case "weather"->"The weather changed in your current world.";
            case "ate"->"Released use and restored your hotbar selection.";
            case "eating"->"Holding use until the food is consumed.";
            case "totem"->"A carried totem was moved to your offhand.";
            case "mace"->"Used a mace against the target within reach.";
            case "clutch"->"Used a hotbar item on the landing surface.";
            case "pay"->"Local preview only. No payment was sent.";
            default->"Your change takes effect immediately.";
        };
        show(key,text,detail,cooldownSeconds);
    }
    public static void show(String key,String title,String description,int cooldownSeconds){
        var s=Quirk.settings();if(!s.module("notifications").on())return;long now=System.nanoTime();
        if(now<cooldowns.getOrDefault(key,0L))return;cooldowns.put(key,now+cooldownSeconds*1_000_000_000L);
        queue.addLast(new Notice(title,description,now,(long)(s.module("notifications").number("duration")*1e9)));while(queue.size()>4)queue.removeFirst();
    }
    public static void tick(){
        var mc=Minecraft.getInstance();if(mc.level!=level){level=mc.level;weather=-1;queue.clear();cooldowns.clear();}if(mc.level==null)return;
        int next=mc.level.isThundering()?2:mc.level.isRaining()?1:0;
        if(weather>=0&&weather!=next&&Quirk.settings().module("weather").on())show("weather","Weather: "+new String[]{"clear skies","rain","thunderstorm"}[next],(int)Quirk.settings().module("weather").number("cooldown"));weather=next;
    }
    private static String fit(String value,int maxWidth){
        if(Paint.width(value)<=maxWidth)return value;
        while(!value.isEmpty()&&Paint.width(value+"…")>maxWidth)
            value=value.substring(0,value.offsetByCodePoints(value.length(),-1));
        return value.isEmpty()?value:value+"…";
    }
    public static void render(GuiGraphics g){
        if(!Quirk.settings().module("notifications").on())return;
        long now=System.nanoTime();queue.removeIf(n->now-n.start>n.duration);
        int x=8,y=g.guiHeight()-8;
        for(Notice n:queue){
            double elapsed=(now-n.start)/1e9,left=(n.duration-(now-n.start))/1e9;
            double opacity=Math.clamp(Math.min(elapsed*6,left*4),0,1);
            int height=n.description.isEmpty()?20:29;
            int maxWidth=Math.max(96,Math.min(240,g.guiWidth()-16));
            int width=Math.min(maxWidth,Math.max(96,Math.max(Paint.width(n.title),Paint.width(n.description))+16));
            String title=fit(n.title,width-16),description=fit(n.description,width-16);
            int cardX=x+(int)((1-opacity)*12);
            y-=height;
            int alpha=(int)(opacity*232);
            g.fill(cardX,y,cardX+width,y+height,(alpha<<24)|0x111111);
            g.fill(cardX,y,cardX+1,y+height,((int)(opacity*255)<<24)|0xcccccc);
            int titleY=y+(n.description.isEmpty()?1:0);
            Paint.text(g,title,cardX+8,titleY,((int)(opacity*255)<<24)|0xeeeeee);
            if(!description.isEmpty())
                Paint.text(g,description,cardX+8,y+11,((int)(opacity*210)<<24)|0xaaaaaa);
            y-=5;
        }
    }
}
