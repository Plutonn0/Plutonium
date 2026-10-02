package com.quirk.client;

import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;
import java.nio.file.*;
import java.util.List;
import java.util.concurrent.atomic.AtomicInteger;
import static org.junit.jupiter.api.Assertions.*;

class SettingsTest {
    @TempDir Path temp;
    @Test void slidersClampQuantizeAndRejectInvalidNumbers() {
        var s=new Settings(); var o=s.module("freecam").get("speed");
        assertEquals(16,o.number()); o.set(500); assertEquals(80,o.number()); o.set(-50); assertEquals(1,o.number());
        o.set(8.2); assertEquals(8,o.number()); o.set(Double.NaN); assertEquals(8,o.number());
        o.set(Double.POSITIVE_INFINITY); assertEquals(8,o.number()); o.set("not a number"); assertEquals(8,o.number());
        o.fraction(2); assertEquals(80,o.number());
    }
    @Test void changesNotifyImmediatelyAndUnchangedValuesDoNot() {
        var s=new Settings(); AtomicInteger calls=new AtomicInteger(); s.onChange(m->calls.incrementAndGet());
        var e=s.module("tracers").enabled; e.toggle(); assertTrue(e.on()); assertEquals(1,calls.get());
        e.set(true); assertEquals(1,calls.get()); e.toggle(); assertFalse(e.on()); assertEquals(2,calls.get());
    }
    @Test void persistsEveryOptionAndDisarmsFreecam() throws Exception {
        var s=new Settings(); var store=new ConfigStore(temp.resolve("settings.json"),s); s.onChange(m->store.changed());
        for(var m:s.modules) for(var o:m.options) switch(o.kind) {
            case TOGGLE -> o.toggle(); case SLIDER -> o.set(o.max); case CHOICE -> o.set(o.choices.getLast()); case COLOR -> o.set(0xff123456); case TEXT -> o.set("Saved text | test");
        }
        store.flush(); var loaded=new Settings(); new ConfigStore(temp.resolve("settings.json"),loaded).load();
        for(var m:s.modules) for(var o:m.options) {
            if((m.id.equals("freecam")||m.id.equals("freelook"))&&o.id.equals("enabled")) assertFalse(loaded.module(m.id).get(o.id).on());
            else assertEquals(o.value(),loaded.module(m.id).get(o.id).value(),m.id+"."+o.id);
        }
    }
    @Test void malformedFileIsBackedUpAndDefaultsSurvive() throws Exception {
        Path p=temp.resolve("settings.json"); Files.writeString(p,"{broken");
        var s=new Settings(); var store=new ConfigStore(p,s); store.load(); assertFalse(store.error.isEmpty());
        assertTrue(s.module("coordinates").on()); assertFalse(s.module("freecam").on());
        try(var files=Files.list(temp)) { assertEquals(2,files.count()); }
    }
    @Test void invalidChoiceAndResetAreSafe() {
        var s=new Settings(); var m=s.module("storage"); m.get("style").set("bogus"); assertEquals("Filled",m.get("style").choice());
        m.get("distance").set(256); m.get("chests").set(false); s.reset(m);
        assertEquals(96,m.number("distance")); assertTrue(m.flag("chests"));
    }
    @Test void visualAndAimModulesDefaultOffWithBoundedAimSettings() {
        var s=new Settings();
        assertFalse(s.module("fullbright").on());
        assertFalse(s.module("xray").on());
        assertFalse(s.module("aimassist").on());
        var aim=s.module("aimassist");
        aim.get("range").set(100); assertEquals(64,aim.number("range"));
        aim.get("angle").set(-1); assertEquals(8,aim.number("angle"));
        aim.get("speed").set(2.26); assertEquals(2.5,aim.number("speed"),0.0001);
    }
    @Test void requestedAutomationModulesDefaultOffWithBoundedSettings() {
        var s=new Settings();
        for(String id:List.of("autoeat","autofirework","automace","fastplace","autototem","doubleanchor","suschunk","autoclutch"))
            assertFalse(s.module(id).on(),id+" is opt-in");
        assertFalse(s.module("quickexp").on(),"Quick XP is opt-in");
        var eat=s.module("autoeat").get("threshold");
        eat.set(30); assertEquals(20,eat.number());
        var fireworks=s.module("autofirework").get("interval");
        fireworks.set(1); assertEquals(10,fireworks.number());
        var threshold=s.module("suschunk").get("threshold");
        threshold.set(200); assertEquals(100,threshold.number());
        var clutchFall=s.module("autoclutch").get("fall");
        clutchFall.set(0); assertEquals(1,clutchFall.number());
    }
}


