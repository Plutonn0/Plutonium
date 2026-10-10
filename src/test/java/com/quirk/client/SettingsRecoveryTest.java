package com.quirk.client;

import static org.junit.jupiter.api.Assertions.*;

import java.nio.file.Files;
import java.nio.file.Path;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.api.io.TempDir;

class SettingsRecoveryTest {
    @TempDir Path temporary;

    @Test
    void invalidRowsDoNotDiscardOtherSettings() throws Exception {
        var file = temporary.resolve("settings.json");
        Files.writeString(
                file,
                """
            {"schema":2,"coordinates":{"scale":80},
             "hudLayout":{"coordinates":{"x":"invalid","y":0},"active":{"x":0.25,"y":0.5}},
             "sidebarOverrides":{"broken":[],"server/board":{"bad":{"value":{}},"good":{"value":"100K"}}}}
            """);
        var settings = new Settings();
        new ConfigStore(file, settings).load();
        assertEquals(80, settings.module("coordinates").number("scale"));
        assertEquals(new HudLayout.Position(0, 0), settings.hud.position("coordinates"));
        assertEquals(new HudLayout.Position(.25, .5), settings.hud.position("active"));
        assertEquals("100K", settings.sidebar.get("server/board", "good").value());
    }

    @Test
    void failedSaveCanRetryWithoutLosingInMemoryChanges() throws Exception {
        var parent = temporary.resolve("blocked");
        Files.writeString(parent, "existing data");
        var settings = new Settings();
        var file = parent.resolve("settings.json");
        var store = new ConfigStore(file, settings);
        settings.onChange(module -> store.changed());
        settings.module("coordinates").get("scale").set(80);
        store.flush();
        assertFalse(store.error.isEmpty());
        assertEquals("existing data", Files.readString(parent));
        Files.delete(parent);
        store.flush();
        assertTrue(store.error.isEmpty());
        var restored = new Settings();
        new ConfigStore(file, restored).load();
        assertEquals(80, restored.module("coordinates").number("scale"));
    }

    @Test
    void malformedFileIsPreservedForRecovery() throws Exception {
        var file = temporary.resolve("settings.json");
        Files.writeString(file, "{ broken JSON");
        var store = new ConfigStore(file, new Settings());
        store.load();
        assertFalse(store.error.isEmpty());
        assertEquals("{ broken JSON", Files.readString(file));
        try (var files = Files.list(temporary)) {
            assertEquals(
                    1,
                    files.filter(
                                    path ->
                                            path.getFileName()
                                                    .toString()
                                                    .startsWith("settings-invalid-"))
                            .count());
        }
    }
}
