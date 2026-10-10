package com.quirk.client.ui;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.*;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
public final class FlatButton extends AbstractWidget {
    private final Runnable action;
    public FlatButton(int x,int y,int w,int h,String title,Runnable action){super(x,y,w,h,Component.literal(title));this.action=action;}
    @Override protected void renderWidget(GuiGraphics g,int mx,int my,float partial){g.fill(getX(),getY(),getX()+width,getY()+height,isHoveredOrFocused()?0xff363636:0xff222222);if(isFocused())g.fill(getX(),getY()+height-1,getX()+width,getY()+height,Paint.TEXT);String text=com.quirk.client.HudRenderer.fit(getMessage().getString(),width-8);Paint.hudText(g,text,getX()+(width-Paint.width(text))/2f,getY(),height,active?Paint.TEXT:Paint.MUTED);}
    @Override public void onClick(MouseButtonEvent e,boolean twice){action.run();}
    @Override public boolean keyPressed(KeyEvent e){if(active&&(e.key()==GLFW.GLFW_KEY_ENTER||e.key()==GLFW.GLFW_KEY_SPACE)){action.run();return true;}return false;}
    @Override protected void updateWidgetNarration(NarrationElementOutput output){defaultButtonNarrationText(output);}
}
