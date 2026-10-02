package com.quirk.client;

import com.google.gson.*;
import java.nio.file.*;
import java.io.*;
import java.time.Instant;

public final class ConfigStore {
    private static final Gson GSON = new GsonBuilder().setPrettyPrinting().create();
    private final Path file;
    private final Settings settings;
    private boolean dirty;
    private long deadline;
    public String error = "";
    public ConfigStore(Path file, Settings settings) { this.file = file; this.settings = settings; }
    public void load() {
        if (!Files.isRegularFile(file)) return;
        try (Reader in = Files.newBufferedReader(file)) {
            JsonObject root = JsonParser.parseReader(in).getAsJsonObject();
            for (Settings.Module m : settings.modules) {
                if (!root.has(m.id) || !root.get(m.id).isJsonObject()) continue;
                JsonObject data = root.getAsJsonObject(m.id);
                for (Settings.Option o : m.options) {
                    if (!data.has(o.id) || !data.get(o.id).isJsonPrimitive()) continue;
                    try {
                        JsonPrimitive p = data.getAsJsonPrimitive(o.id);
                        switch (o.kind) {
                            case TOGGLE -> { if (p.isBoolean()) o.set(p.getAsBoolean()); }
                            case SLIDER, COLOR -> { if (p.isNumber()) o.set(p.getAsDouble()); }
                            case CHOICE, TEXT -> { if (p.isString()) o.set(p.getAsString()); }
                        }
                    } catch (RuntimeException ignored) { /* Keep the safe default for an invalid individual value. */ }
                }
            }
            // A detached camera must never start automatically on joining a world.
            settings.module("freecam").enabled.set(false);
            settings.module("freelook").enabled.set(false);
            int schema=root.has("schema")?root.get("schema").getAsInt():1;
            if(schema<2){
                // Migrate previous defaults for the requested filled ESP and stronger aim.
                for(String id:new String[]{"players","storage","spawners"})if(settings.module(id).get("style").choice().equals("Outline"))settings.module(id).get("style").set("Filled");
                if(Math.abs(settings.module("aimassist").number("speed")-2.5)<.01)settings.module("aimassist").get("speed").set(12);
                changed();
            }
        } catch (Exception e) {
            error = "Could not load settings; defaults are active.";
            try { Files.copy(file, file.resolveSibling("settings-invalid-" + Instant.now().toEpochMilli() + ".json")); } catch (IOException ignored) {}
            System.err.println("[Plutonium] " + error + " " + e);
        }
    }
    public void changed() { dirty = true; deadline = System.nanoTime() + 300_000_000L; }
    public void tick() { if (dirty && System.nanoTime() >= deadline) flush(); }
    public void flush() {
        if (!dirty) return;
        JsonObject root = new JsonObject(); root.addProperty("schema", 2);
        for (Settings.Module m : settings.modules) {
            JsonObject data = new JsonObject();
            for (Settings.Option o : m.options) data.add(o.id, GSON.toJsonTree(o.value()));
            root.add(m.id, data);
        }
        try {
            Files.createDirectories(file.getParent());
            Path tmp = file.resolveSibling(file.getFileName() + ".tmp");
            Files.writeString(tmp, GSON.toJson(root));
            try { Files.move(tmp, file, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE); }
            catch (AtomicMoveNotSupportedException e) { Files.move(tmp, file, StandardCopyOption.REPLACE_EXISTING); }
            dirty = false; error = "";
        } catch (IOException e) { error = "Settings are active, but could not be saved."; deadline = System.nanoTime() + 5_000_000_000L; System.err.println("[Plutonium] " + error + " " + e); }
    }
}
