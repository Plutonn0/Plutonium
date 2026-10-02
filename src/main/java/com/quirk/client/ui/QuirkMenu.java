package com.quirk.client.ui;

import com.quirk.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.*;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import static com.quirk.client.ui.Paint.*;

/** Compact, resolution-independent module columns with an on-demand settings panel. */
public final class QuirkMenu extends Screen {
    private static final int W=960,H=530,COLUMN_W=148,COLUMN_GAP=8,ROW_H=24;
    private static final List<String> CATEGORIES=List.of("COMBAT","MOVEMENT","RENDER","MISC","HUD","ENTERTAINMENT");
    private final Screen previous;
    private final Settings settings=Quirk.settings();
    private final List<Hit> hits=new ArrayList<>();
    private final Map<String,Double> animations=new HashMap<>();
    private float scale,originX,originY;
    private double mx,my,dt,open;
    private long last=System.nanoTime();
    private record Hit(int x,int y,int w,int h,Runnable action) {
        boolean contains(double x,double y) {return x>=this.x&&x<this.x+w&&y>=this.y&&y<this.y+h;}
    }
    public QuirkMenu(Screen previous) {super(Component.literal("Plutonium"));this.previous=previous;}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return true;}
    @Override public void renderBackground(GuiGraphics g,int x,int y,float partial){}
    @Override public void onClose(){Quirk.store().flush();minecraft.setScreen(previous);}
    @Override public void removed(){Quirk.store().flush();}
    private double animate(String key,double target){double v=animations.getOrDefault(key,target);v+=(target-v)*(1-Math.exp(-dt*17));animations.put(key,v);return v;}
    private void hit(int x,int y,int w,int h,Runnable action){hits.add(new Hit(x,y,w,h,action));}
    private void switchAt(GuiGraphics g,String id,int x,int y,boolean on){
        double a=animate(id,on?1:0);
        g.fill(x,y,x+18,y+10,mix(0xb0000000,0xffbcbcbc,a));
        int knob=x+1+(int)Math.round(8*a);g.fill(knob,y+1,knob+8,y+9,0xffeeeeee);
    }
    private void centeredText(GuiGraphics g,String value,int centerX,int y,int color){
        text(g,value,centerX-width(value)/2,y,color);
    }
    private void clippedCenteredText(GuiGraphics g,String value,int centerX,int y,int color,int maxWidth){
        if(width(value)>maxWidth){
            while(!value.isEmpty()&&width(value+"…")>maxWidth)
                value=value.substring(0,value.offsetByCodePoints(value.length(),-1));
            value+="…";
        }
        centeredText(g,value,centerX,y,color);
    }
    @Override public void render(GuiGraphics g,int rawX,int rawY,float partial){
        long now=System.nanoTime();dt=Math.min(.1,(now-last)/1e9);last=now;open+=(1-open)*(1-Math.exp(-dt*14));
        scale=Math.min((width-24f)/W,(height-24f)/H);
        originX=(width-W*scale)/2;originY=(height-H*scale)/2+(float)((1-open)*10*scale);
        mx=(rawX-originX)/scale;my=(rawY-originY)/scale;hits.clear();
        g.fill(0,0,width,height,(int)(open*105)<<24);
        g.pose().pushMatrix();g.pose().translate(originX,originY);g.pose().scale(scale,scale);
        for(int i=0;i<CATEGORIES.size();i++){
            String category=CATEGORIES.get(i);
            int x=16+i*(COLUMN_W+COLUMN_GAP),y=16;
            List<Settings.Module> modules=settings.modules.stream().filter(m->m.category.equals(category)).toList();
            int cardHeight=34+modules.size()*ROW_H;
            g.fill(x,y,x+COLUMN_W,y+cardHeight,0xb8000000);
            g.fill(x+1,y+1,x+COLUMN_W-1,y+24,0xc8000000);
            centeredText(g,category,x+COLUMN_W/2,y+8,0xffeeeeee);
            g.fill(x+8,y+29,x+COLUMN_W-8,y+30,0x603f3f3f);
            int rowY=y+34;
            for(Settings.Module module:modules){
                int row=rowY;
                boolean hovered=mx>=x+4&&mx<x+COLUMN_W-4&&my>=row&&my<row+ROW_H;
                double hover=animate("hover"+module.id,hovered?1:0);
                if(hover>.01)g.fill(x+4,row,x+COLUMN_W-4,row+ROW_H,((int)(56*hover)<<24)|0x3f3f3f);
                int labelCenter=x+58;
                clippedCenteredText(g,module.name,labelCenter,row+7,module.on()?TEXT:MUTED,108);
                switchAt(g,"toggle"+module.id,x+118,row+7,module.on());
                hit(x+5,row+1,112,ROW_H-2,module.enabled::toggle);
                hit(x+116,row+2,19,ROW_H-4,module.enabled::toggle);
                rowY+=ROW_H;
            }
        }
        if(!Quirk.store().error.isEmpty())centeredText(g,Quirk.store().error,W/2,H-39,0xffff9999);
        g.pose().popMatrix();
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        double x=(e.x()-originX)/scale,y=(e.y()-originY)/scale;
        if(e.button()!=0)return true;
        for(int i=hits.size()-1;i>=0;i--){
            Hit h=hits.get(i);
            if(h.contains(x,y)){
                h.action.run();
                return true;
            }
        }
        return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent e){Quirk.store().flush();return true;}
    @Override public boolean mouseScrolled(double x,double y,double dx,double dy){return true;}
    @Override public boolean keyPressed(KeyEvent e){
        if(e.key()==GLFW.GLFW_KEY_F8||e.key()==GLFW.GLFW_KEY_ESCAPE){onClose();return true;}
        return true;
    }
    @Override public boolean charTyped(CharacterEvent e){
        return true;
    }
}
