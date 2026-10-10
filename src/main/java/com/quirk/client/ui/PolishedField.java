package com.quirk.client.ui;
import com.quirk.client.TextBuffer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.narration.*;
import net.minecraft.client.input.*;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import java.util.function.Consumer;

public final class PolishedField extends AbstractWidget {
    private final TextBuffer text;
    private final Consumer<String> changed;
    private int offset;
    public PolishedField(int x,int y,int w,int h,String label,String value,Consumer<String> changed){super(x,y,w,h,Component.literal(label));text=new TextBuffer(value);this.changed=changed;}
    public String value(){return text.value();}
    private int pixel(int position){return Paint.width(text.value().substring(0,position));}
    @Override protected void renderWidget(GuiGraphics g,int mx,int my,float partial){
        g.fill(getX(),getY(),getX()+width,getY()+height,0xff191919);g.fill(getX(),getY()+height-1,getX()+width,getY()+height,isFocused()?Paint.TEXT:0xff444444);
        int caret=pixel(text.cursor());if(isFocused()){if(caret-offset>width-14)offset=caret-width+14;if(caret<offset)offset=caret;}else offset=0;
        g.enableScissor(getX()+5,getY(),getX()+width-5,getY()+height);
        if(isFocused()&&text.start()!=text.end())g.fill(getX()+6+pixel(text.start())-offset,getY()+5,getX()+6+pixel(text.end())-offset,getY()+height-5,0xff555555);
        Paint.hudText(g,text.value(),getX()+6-offset,getY(),height,Paint.TEXT);
        if(isFocused()&&(System.currentTimeMillis()/500)%2==0)g.fill(getX()+6+caret-offset,getY()+5,getX()+7+caret-offset,getY()+height-5,Paint.TEXT);
        g.disableScissor();
    }
    private int index(double mouseX){double local=mouseX-getX()-6+offset;int result=0;while(result<text.value().length()){int next=text.value().offsetByCodePoints(result,1);if((pixel(result)+pixel(next))/2.0>local)break;result=next;}return result;}
    @Override public void onClick(MouseButtonEvent e,boolean twice){text.at(index(e.x()),false);if(twice)text.selectAll();}
    @Override protected void onDrag(MouseButtonEvent e,double dx,double dy){text.at(index(e.x()),true);}
    @Override public boolean charTyped(CharacterEvent e){if(!isFocused()||!e.isAllowedChatCharacter())return false;text.insert(e.codepointAsString());changed.accept(value());return true;}
    @Override public boolean keyPressed(KeyEvent e){
        if(!isFocused())return false;boolean ctrl=(e.modifiers()&GLFW.GLFW_MOD_CONTROL)!=0,shift=(e.modifiers()&GLFW.GLFW_MOD_SHIFT)!=0;String before=value();
        var keyboard=Minecraft.getInstance().keyboardHandler;
        if(ctrl&&e.key()==GLFW.GLFW_KEY_A)text.selectAll();
        else if(ctrl&&e.key()==GLFW.GLFW_KEY_C)keyboard.setClipboard(text.selection());
        else if(ctrl&&e.key()==GLFW.GLFW_KEY_X){keyboard.setClipboard(text.selection());text.insert("");}
        else if(ctrl&&e.key()==GLFW.GLFW_KEY_V)text.insert(keyboard.getClipboard());
        else switch(e.key()){case GLFW.GLFW_KEY_LEFT->text.move(-1,shift);case GLFW.GLFW_KEY_RIGHT->text.move(1,shift);case GLFW.GLFW_KEY_HOME->text.at(0,shift);case GLFW.GLFW_KEY_END->text.at(value().length(),shift);case GLFW.GLFW_KEY_BACKSPACE->text.delete(true);case GLFW.GLFW_KEY_DELETE->text.delete(false);case GLFW.GLFW_KEY_ENTER->setFocused(false);default->{return false;}}
        if(!before.equals(value()))changed.accept(value());return true;
    }
    @Override protected void updateWidgetNarration(NarrationElementOutput output){output.add(NarratedElementType.TITLE,Component.literal(getMessage().getString()+": "+value()));}
}
