package com.quirk.client;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.Path;
import static org.junit.jupiter.api.Assertions.*;

class HudAndSidebarTest {
    @TempDir Path temp;
    @Test void defaultAnchorsAndResizingKeepHudOnScreen(){
        var hud=new HudLayout();
        for(int[] screen:new int[][]{{960,540},{320,240},{1920,1080}})for(String id:HudLayout.IDS){
            var b=hud.bounds(id,screen[0],screen[1],420,264,1.5f);
            assertTrue(b.x()>=0&&b.y()>=0);assertTrue(b.x()+b.width()<=screen[0]);assertTrue(b.y()+b.height()<=screen[1]);
        }
        var right=hud.bounds("active",960,540,100,50,1);assertEquals(954,right.x()+right.width());
        hud.set("active",.5,.5);var center=hud.bounds("active",320,240,100,50,1);assertEquals(160,center.x()+center.width()/2);assertEquals(120,center.y()+center.height()/2);
        hud.set("active",Double.NaN,0);assertEquals(new HudLayout.Position(.5,.5),hud.position("active"));
    }
    @Test void dragSnapsAndLayoutPersistsWithServerScopedOverrides(){
        var s=new Settings();var file=temp.resolve("settings.json");var store=new ConfigStore(file,s);s.onChange(m->store.changed());
        var bounds=s.hud.bounds("coordinates",960,540,100,50,1);s.hud.move("coordinates",430,245,960,540,bounds);
        assertEquals(new HudLayout.Position(.5,.5),s.hud.position("coordinates"));assertEquals(430,HudLayout.snap(432,100,960,6));
        s.sidebar.set("server/board","kills",null,"999");s.module("coordinates").get("scale").set(80);store.flush();
        var loaded=new Settings();new ConfigStore(file,loaded).load();assertEquals(s.hud.position("coordinates"),loaded.hud.position("coordinates"));assertEquals(80,loaded.module("coordinates").number("scale"));
        assertEquals("999",loaded.sidebar.apply("server/board","kills",new SidebarOverrides.Fields("Kills","5")).value());
        assertEquals("5",loaded.sidebar.apply("different/board","kills",new SidebarOverrides.Fields("Kills","5")).value());
    }
    @Test void parsesInlineValuesWithoutSplittingPlayerNames(){
        assertEquals(new SidebarOverrides.Fields("Balance:","$1,250.50"),SidebarOverrides.split("§aBalance: $1,250.50","15"));
        assertEquals(new SidebarOverrides.Fields("Kills »","24"),SidebarOverrides.split("Kills » 24",""));
        assertEquals(new SidebarOverrides.Fields("Player123","5"),SidebarOverrides.split("Player123","5"));
        assertEquals(new SidebarOverrides.Fields("Website", ""),SidebarOverrides.split("Website",""));
    }
    @Test void valuesStayOverriddenWhileUneditedFieldsStayLiveAndResetRestoresThem(){
        var overrides=new SidebarOverrides();overrides.set("a","kills",null,"999");
        assertEquals(new SidebarOverrides.Fields("Updated label","999"),overrides.apply("a","kills",new SidebarOverrides.Fields("Updated label","12")));
        overrides.set("a","kills",null,null);assertEquals("12",overrides.apply("a","kills",new SidebarOverrides.Fields("Kills","12")).value());
    }
    @Test void unicodeFieldEditingSupportsSelectionReplacementAndBackspace(){
        var t=new TextBuffer("A😀B");t.move(-1,false);t.delete(true);assertEquals("AB",t.value());
        t.selectAll();t.insert("$2,500");assertEquals("$2,500",t.value());t.at(0,false);t.move(1,true);assertEquals("$",t.selection());t.insert("€");assertEquals("€2,500",t.value());
        t.selectAll();t.insert("x".repeat(200));assertEquals(160,t.value().length());
    }
}
