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
    private final Map<Settings.Module,int[]> moduleRows=new LinkedHashMap<>();
    private final Map<String,int[]> optionRows=new LinkedHashMap<>();
    private Settings.Module expanded;
    private Settings.Option editing,dragging;
    private boolean selectAll;
    private int scroll,contentHeight,dragChannel=-1;
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
        mx=(rawX-originX)/scale;my=(rawY-originY)/scale;hits.clear();moduleRows.clear();optionRows.clear();
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
                moduleRows.put(module,new int[]{x+5,row+1,COLUMN_W-10,ROW_H-2});
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
        if(expanded!=null)renderSettings(g);
        if(!Quirk.store().error.isEmpty())centeredText(g,Quirk.store().error,W/2,H-39,0xffff9999);
        g.pose().popMatrix();
    }
    private void renderSettings(GuiGraphics g){
        hits.clear();
        g.fill(0,0,W,H,0xb5000000);g.fill(248,64,712,490,0xf5111111);
        text(g,expanded.name,266,78,TEXT);text(g,"Close",656,78,MUTED);
        hit(648,73,54,28,()->{expanded=null;editing=null;dragging=null;});
        String description=expanded.description;
        while(width(description)>428)description=description.substring(0,description.length()-1);
        text(g,description,266,104,MUTED);g.fill(266,132,694,133,0xff333333);
        contentHeight=expanded.options.stream().mapToInt(o->o.kind==Settings.Kind.COLOR?96:40).sum();
        scroll=(int)Math.clamp(scroll,0,Math.max(0,contentHeight-330));
        int y=142-scroll;
        for(Settings.Option option:expanded.options){
            int height=option.kind==Settings.Kind.COLOR?96:40;
            if(y>=140&&y+height<=476){
                final int row=y;
                text(g,option.label,266,y+3,TEXT);
                optionRows.put(option.id,new int[]{458,y,226,height});
                switch(option.kind){
                    case TOGGLE -> {switchAt(g,"setting"+expanded.id+option.id,662,y+9,option.on());hit(458,y,226,32,option::toggle);}
                    case CHOICE -> {text(g,option.choice(),458,y+3,MUTED);hit(458,y,226,32,()->option.set(option.choices.get((option.choices.indexOf(option.choice())+1)%option.choices.size())));}
                    case SLIDER -> {
                        text(g,option.display(),458,y-2,MUTED);bar(g,458,y+25,226,option.fraction(),0xffcccccc);
                        hit(458,y,226,35,()->{dragging=option;dragChannel=-1;applyDrag(mx);});
                    }
                    case COLOR -> {
                        g.fill(664,y+4,684,y+20,option.color());text(g,option.display(),458,y+1,MUTED);
                        for(int channel=0;channel<3;channel++){
                            int shift=(2-channel)*8,cy=row+28+channel*21;
                            text(g,new String[]{"R","G","B"}[channel],440,cy-7,MUTED);
                            bar(g,458,cy+5,226,((option.color()>>>shift)&255)/255.0,0xffcccccc);
                            final int component=channel;hit(458,cy-5,226,19,()->{dragging=option;dragChannel=component;applyDrag(mx);});
                        }
                    }
                    case TEXT -> {
                        g.fill(458,y,684,y+32,editing==option?0xff383838:0xff242424);
                        String value=option.choice();while(width(value)>210&&!value.isEmpty())value=value.substring(1);
                        text(g,value+(editing==option?"|":""),464,y+3,TEXT);
                        hit(458,y,226,32,()->{editing=option;selectAll=false;});
                    }
                }
            }
            y+=height;
        }
        if(expanded.id.equals("radio")){String status=Entertainment.radioStatus();while(width(status)>425)status=status.substring(0,status.length()-1);text(g,status,266,476,MUTED);}
        if(contentHeight>330){g.fill(702,142,704,472,0xff333333);int top=142+(int)(scroll/(double)(contentHeight-330)*290);g.fill(702,top,704,top+40,0xffaaaaaa);}
    }
    private void bar(GuiGraphics g,int x,int y,int w,double fraction,int color){g.fill(x,y,x+w,y+2,0xff444444);int at=x+(int)(w*fraction);g.fill(x,y,at,y+2,color);g.fill(at-2,y-3,at+2,y+5,TEXT);}
    private void applyDrag(double x){
        if(dragging==null)return;
        double fraction=Math.clamp((x-458)/226,0,1);
        if(dragChannel<0)dragging.fraction(fraction);
        else {int shift=(2-dragChannel)*8;dragging.set((dragging.color()&~(255<<shift))|((int)Math.round(fraction*255)<<shift));}
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        double x=(e.x()-originX)/scale,y=(e.y()-originY)/scale;
        mx=x;my=y;
        if(e.button()==1&&expanded==null){
            for(var entry:moduleRows.entrySet()){int[] r=entry.getValue();if(x>=r[0]&&x<r[0]+r[2]&&y>=r[1]&&y<r[1]+r[3]){expanded=entry.getKey();scroll=0;editing=null;break;}}
            return true;
        }
        if(e.button()!=0)return true;
        editing=null;
        for(int i=hits.size()-1;i>=0;i--){
            Hit h=hits.get(i);
            if(h.contains(x,y)){
                h.action.run();
                return true;
            }
        }
        return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent e){dragging=null;Quirk.store().flush();return true;}
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){applyDrag((e.x()-originX)/scale);return true;}
    @Override public boolean mouseScrolled(double x,double y,double dx,double dy){if(expanded!=null)scroll=(int)Math.clamp(scroll-dy*40,0,Math.max(0,contentHeight-330));return true;}
    @Override public boolean keyPressed(KeyEvent e){
        if(e.key()==GLFW.GLFW_KEY_F8||e.key()==GLFW.GLFW_KEY_ESCAPE){onClose();return true;}
        if(editing!=null){
            if(e.key()==GLFW.GLFW_KEY_A&&(e.modifiers()&GLFW.GLFW_MOD_CONTROL)!=0){selectAll=true;return true;}
            if(e.key()==GLFW.GLFW_KEY_BACKSPACE){String v=editing.choice();editing.set(selectAll||v.isEmpty()?"":v.substring(0,v.offsetByCodePoints(v.length(),-1)));selectAll=false;}
            if(e.key()==GLFW.GLFW_KEY_V&&(e.modifiers()&GLFW.GLFW_MOD_CONTROL)!=0){editing.set((selectAll?"":editing.choice())+minecraft.keyboardHandler.getClipboard());selectAll=false;}
            if(e.key()==GLFW.GLFW_KEY_ENTER)editing=null;
        }
        return true;
    }
    @Override public boolean charTyped(CharacterEvent e){
        if(editing!=null&&e.isAllowedChatCharacter()){editing.set((selectAll?"":editing.choice())+e.codepointAsString());selectAll=false;}
        return true;
    }
}
