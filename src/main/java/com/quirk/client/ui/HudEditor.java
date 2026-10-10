package com.quirk.client.ui;
import com.quirk.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.*;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import java.util.*;
import static com.quirk.client.ui.Paint.*;

public final class HudEditor extends Screen {
    private final Screen previous;
    private String selected="coordinates",dragging;
    private double offsetX,offsetY,resizeWidth,resizeScale,resizeX,resizeY,resizeMouseX;
    private boolean resizing;
    public HudEditor(Screen previous){super(Component.literal("HUD editor"));this.previous=previous;}
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return true;}
    @Override public void renderBackground(GuiGraphics g,int x,int y,float partial){}
    @Override public void onClose(){Quirk.store().flush();minecraft.setScreen(previous);}
    @Override public void removed(){Quirk.store().flush();}
    private void reset(String id){Quirk.settings().hud.reset(id);var option=Quirk.settings().module(id).get("scale");option.set(option.defaultValue);}
    @Override public void render(GuiGraphics g,int mx,int my,float partial){
        g.fill(0,0,width,height,0x65000000);HudRenderer.render(g,true);
        for(String id:HudLayout.IDS){var b=HudRenderer.frames().get(id);if(b==null)continue;int x=(int)b.x(),y=(int)b.y(),r=(int)(b.x()+b.width()),bottom=(int)(b.y()+b.height());
            int color=id.equals(selected)?0xffeeeeee:0x88777777;g.fill(x-2,y-2,r+2,y-1,color);g.fill(x-2,bottom+1,r+2,bottom+2,color);g.fill(x-2,y-2,x-1,bottom+2,color);g.fill(r+1,y-2,r+2,bottom+2,color);
            if(id.equals(selected)){g.fill(r-4,bottom-4,r+3,bottom+3,TEXT);if(dragging!=null){if(Math.abs(b.x()+b.width()/2-width/2f)<2)g.fill(width/2,0,width/2+1,height,0x99ffffff);if(Math.abs(b.y()+b.height()/2-height/2f)<2)g.fill(0,height/2,width,height/2+1,0x99ffffff);}}
        }
        String info=Quirk.settings().module(selected).name+" · "+Quirk.settings().module(selected).get("scale").display();
        String help=HudRenderer.fit("Drag to move · corner/scroll to resize · arrows to nudge",width-28);int headerWidth=Math.min(width-16,Math.max(300,Paint.width(help)+24));
        g.fill((width-headerWidth)/2,7,(width+headerWidth)/2,44,0xee111111);hudText(g,info,width/2-Paint.width(info)/2,9,16,TEXT);
        hudText(g,help,width/2-Paint.width(help)/2,26,14,MUTED);
        String[] buttons={Quirk.settings().module(selected).enabled.on()?"Hide selected":"Show selected","Reset selected","Reset all","Done"};
        int bw=Math.min(118,(width-16)/4),start=(width-bw*4)/2;for(int i=0;i<4;i++){int x=start+i*bw;boolean hover=mx>=x&&mx<x+bw-4&&my>=height-31&&my<height-8;g.fill(x,height-31,x+bw-4,height-8,hover?0xff333333:0xef181818);String title=HudRenderer.fit(buttons[i],bw-10);hudText(g,title,x+(bw-4-Paint.width(title))/2,height-31,23,TEXT);}
    }
    @Override public boolean mouseClicked(MouseButtonEvent e,boolean twice){
        if(e.button()!=0)return true;
        int bw=Math.min(118,(width-16)/4),start=(width-bw*4)/2;if(e.y()>=height-31&&e.y()<=height-8){int at=(int)(e.x()-start)/bw;if(e.x()>=start&&e.x()<start+bw*4){switch(at){case 0->Quirk.settings().module(selected).enabled.toggle();case 1->reset(selected);case 2->{for(String id:HudLayout.IDS)reset(id);}case 3->onClose();}return true;}}
        var ids=new ArrayList<>(HudLayout.IDS);Collections.reverse(ids);
        for(String id:ids){var b=HudRenderer.frames().get(id);if(b!=null&&e.x()>=b.x()-3&&e.y()>=b.y()-3&&e.x()<=b.x()+b.width()+4&&e.y()<=b.y()+b.height()+4){selected=id;dragging=id;offsetX=e.x()-b.x();offsetY=e.y()-b.y();resizing=e.x()>=b.x()+b.width()-7&&e.y()>=b.y()+b.height()-7;resizeWidth=b.width();resizeScale=Quirk.settings().module(id).number("scale");resizeX=b.x();resizeY=b.y();resizeMouseX=e.x();return true;}}
        return true;
    }
    @Override public boolean mouseDragged(MouseButtonEvent e,double dx,double dy){
        if(dragging==null)return true;var b=HudRenderer.frames().get(dragging);if(b==null)return true;
        if(resizing){var settings=Quirk.settings();var option=settings.module(dragging).get("scale");option.set(resizeScale*(resizeWidth+e.x()-resizeMouseX)/Math.max(1,resizeWidth));var resized=settings.hud.bounds(dragging,width,height,b.width()/b.scale(),b.height()/b.scale(),(float)(option.number()/100));settings.hud.move(dragging,resizeX,resizeY,width,height,resized);}
        else Quirk.settings().hud.move(dragging,HudLayout.snap(e.x()-offsetX,b.width(),width,6),HudLayout.snap(e.y()-offsetY,b.height(),height,6),width,height,b);
        return true;
    }
    @Override public boolean mouseReleased(MouseButtonEvent e){dragging=null;Quirk.store().flush();return true;}
    @Override public boolean mouseScrolled(double x,double y,double dx,double dy){var option=Quirk.settings().module(selected).get("scale");option.set(option.number()+dy*5);return true;}
    @Override public boolean keyPressed(KeyEvent e){
        if(e.key()==GLFW.GLFW_KEY_ESCAPE||e.key()==GLFW.GLFW_KEY_F8){onClose();return true;}
        if(e.key()==GLFW.GLFW_KEY_TAB){selected=HudLayout.IDS.get((HudLayout.IDS.indexOf(selected)+1)%HudLayout.IDS.size());return true;}
        var b=HudRenderer.frames().get(selected);int step=(e.modifiers()&GLFW.GLFW_MOD_SHIFT)!=0?10:1;
        if(b!=null){int dx=e.key()==GLFW.GLFW_KEY_LEFT?-step:e.key()==GLFW.GLFW_KEY_RIGHT?step:0,dy=e.key()==GLFW.GLFW_KEY_UP?-step:e.key()==GLFW.GLFW_KEY_DOWN?step:0;if(dx!=0||dy!=0)Quirk.settings().hud.move(selected,b.x()+dx,b.y()+dy,width,height,b);}
        return true;
    }
}
