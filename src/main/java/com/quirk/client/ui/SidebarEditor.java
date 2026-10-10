package com.quirk.client.ui;

import com.quirk.client.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import org.lwjgl.glfw.GLFW;
import static com.quirk.client.ui.Paint.*;

/** Edits only persisted display overrides; the live scoreboard remains read-only. */
public final class SidebarEditor extends Screen {
    private final Screen previous;
    private FakeStats.Snapshot snapshot;
    private int page,left,top,panelWidth,panelHeight,perPage;

    public SidebarEditor(Screen previous){
        super(Component.literal("Scoreboard editor"));
        this.previous=previous;
        Quirk.settings().module("fakestats").get("live").set(true);
    }
    @Override public boolean isPauseScreen(){return false;}
    @Override public boolean isInGameUi(){return true;}
    @Override public void renderBackground(GuiGraphics g,int x,int y,float partial){}
    @Override public void onClose(){Quirk.store().flush();minecraft.setScreen(previous);}
    @Override public void removed(){Quirk.store().flush();}
    private void refresh(){FakeStats.tick();snapshot=FakeStats.snapshot();rebuildWidgets();}

    @Override protected void init(){
        clearWidgets();
        if(snapshot==null){FakeStats.tick();snapshot=FakeStats.snapshot();}
        panelWidth=Math.min(650,width-24);left=(width-panelWidth)/2;
        perPage=Math.max(1,(height-196)/38);
        int count=snapshot==null?0:snapshot.rows().size();
        page=Math.clamp(page,0,Math.max(0,(count-1)/perPage));
        boolean pages=count>perPage;
        int visible=Math.max(1,Math.min(perPage,count-page*perPage));
        panelHeight=Math.min(height-24,146+visible*38+(pages?26:0));
        top=(height-panelHeight)/2;
        int footer=top+panelHeight-37,buttonWidth=(panelWidth-28)/4;
        addRenderableWidget(new FlatButton(left+8,footer,buttonWidth,25,
            Quirk.settings().module("fakestats").enabled.on()?"Preview: ON":"Preview: OFF",()->{
                Quirk.settings().module("fakestats").enabled.toggle();rebuildWidgets();
            }));
        addRenderableWidget(new FlatButton(left+12+buttonWidth,footer,buttonWidth,25,"Refresh entries",this::refresh));
        addRenderableWidget(new FlatButton(left+16+buttonWidth*2,footer,buttonWidth,25,"Reset overrides",()->{
            if(snapshot!=null)Quirk.settings().sidebar.reset(snapshot.scope());rebuildWidgets();
        }));
        addRenderableWidget(new FlatButton(left+20+buttonWidth*3,footer,buttonWidth,25,"Done",this::onClose));
        if(snapshot==null)return;
        var current=snapshot;var model=Quirk.settings().sidebar;
        var title=model.apply(current.scope(),"$title",new SidebarOverrides.Fields(current.title(),""));
        addRenderableWidget(new PolishedField(left+8,top+50,panelWidth-16,28,"Sidebar title",title.label(),
            value->model.set(current.scope(),"$title",value.equals(current.title())?null:value,null)));
        int labelWidth=(panelWidth-80)*3/5,valueWidth=panelWidth-80-labelWidth;
        int index=0;
        for(var row:current.rows().stream().skip((long)page*perPage).limit(perPage).toList()){
            int y=top+101+index++*38;
            var fields=model.apply(current.scope(),row.id(),new SidebarOverrides.Fields(row.label(),row.value()));
            addRenderableWidget(new PolishedField(left+8,y,labelWidth,30,"Label",fields.label(),value->{
                var old=model.get(current.scope(),row.id());
                model.set(current.scope(),row.id(),value.equals(row.label())?null:value,old.value());
            }));
            addRenderableWidget(new PolishedField(left+14+labelWidth,y,valueWidth,30,"Displayed value",fields.value(),value->{
                var old=model.get(current.scope(),row.id());
                model.set(current.scope(),row.id(),old.label(),value.equals(row.value())?null:value);
            }));
            addRenderableWidget(new FlatButton(left+22+labelWidth+valueWidth,y,50,30,"Reset",()->{
                model.set(current.scope(),row.id(),null,null);rebuildWidgets();
            }));
        }
        if(pages){
            var previousPage=addRenderableWidget(new FlatButton(left+8,footer-26,62,22,"Previous",()->{page--;rebuildWidgets();}));
            previousPage.active=page>0;
            var nextPage=addRenderableWidget(new FlatButton(left+panelWidth-70,footer-26,62,22,"Next",()->{page++;rebuildWidgets();}));
            nextPage.active=(page+1)*perPage<count;
        }
    }
    @Override public void render(GuiGraphics g,int mx,int my,float partial){
        g.fill(0,0,width,height,0x99000000);
        g.fill(left,top,left+panelWidth,top+panelHeight,0xf5111111);
        hudText(g,"Scoreboard editor",left+10,top+6,20,TEXT);
        boolean changed=snapshot!=null&&(FakeStats.snapshot()==null||!snapshot.scope().equals(FakeStats.snapshot().scope()));
        String hint=changed?"Sidebar changed — refresh entries.":"Local display only. Values save immediately; server stats stay unchanged.";
        hudText(g,HudRenderer.fit(hint,panelWidth-20),left+10,top+27,16,changed?0xffffcc99:MUTED);
        if(snapshot==null){
            hudText(g,HudRenderer.fit("No scoreboard sidebar detected. Join a server, then refresh.",panelWidth-24),left+12,top+78,22,MUTED);
        }else{
            hudText(g,"Label",left+10,top+82,16,MUTED);
            hudText(g,"Displayed value",left+14+(panelWidth-80)*3/5,top+82,16,MUTED);
            if(snapshot.rows().isEmpty())hudText(g,HudRenderer.fit("The current sidebar has no visible rows yet.",panelWidth-24),left+12,top+104,22,MUTED);
            if(snapshot.rows().size()>perPage){
                String status="Page "+(page+1)+" / "+((snapshot.rows().size()+perPage-1)/perPage);
                hudText(g,status,width/2-Paint.width(status)/2,top+panelHeight-63,22,MUTED);
            }
        }
        super.render(g,mx,my,partial);
    }
    @Override public boolean keyPressed(KeyEvent e){
        if(e.key()==GLFW.GLFW_KEY_ESCAPE||e.key()==GLFW.GLFW_KEY_F8){onClose();return true;}
        return super.keyPressed(e);
    }
}
