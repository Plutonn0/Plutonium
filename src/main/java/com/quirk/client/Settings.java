package com.quirk.client;

import java.util.*;
import java.util.function.Consumer;

/** Typed settings are the single source of truth for both the UI and rendering. */
public final class Settings {
    public enum Kind { TOGGLE, SLIDER, CHOICE, COLOR, TEXT }
    public static final class Option {
        public final String id, label, unit;
        public final Kind kind;
        public final double min, max, step;
        public final List<String> choices;
        public final Object defaultValue;
        private Object value;
        private Runnable changed = () -> {};
        private Option(String id, String label, Kind kind, Object initial, double min, double max, double step, String unit, List<String> choices) {
            this.id = id; this.label = label; this.kind = kind; this.value = initial; this.defaultValue = initial;
            this.min = min; this.max = max; this.step = step; this.unit = unit; this.choices = choices;
        }
        public Object value() { return value; }
        public boolean on() { return (Boolean) value; }
        public double number() { return ((Number) value).doubleValue(); }
        public int color() { return ((Number) value).intValue() | 0xff000000; }
        public String choice() { return (String) value; }
        public void set(Object next) {
            Object safe;
            switch (kind) {
                case TOGGLE -> { if (!(next instanceof Boolean)) return; safe = next; }
                case COLOR -> { if (!(next instanceof Number)) return; safe = ((Number) next).intValue() | 0xff000000; }
                case CHOICE -> { if (!choices.contains(next)) return; safe = next; }
                case TEXT -> { if (!(next instanceof String s)) return; safe = s.replaceAll("[\\p{Cntrl}]", "").substring(0, Math.min(1024, s.replaceAll("[\\p{Cntrl}]", "").length())); }
                case SLIDER -> {
                    if (!(next instanceof Number)) return;
                    double n = ((Number) next).doubleValue(); if (!Double.isFinite(n)) return;
                    safe = Math.clamp(min + Math.round((Math.clamp(n, min, max) - min) / step) * step, min, max);
                }
                default -> throw new IllegalStateException();
            }
            if (!Objects.equals(value, safe)) { value = safe; changed.run(); }
        }
        public void toggle() { set(!on()); }
        public void fraction(double f) { set(min + Math.clamp(f, 0, 1) * (max - min)); }
        public double fraction() { return (number() - min) / (max - min); }
        public String display() {
            if (kind == Kind.SLIDER) return (step >= 1 ? String.valueOf((int) number()) : String.format(Locale.ROOT, "%.1f", number())) + unit;
            if (kind == Kind.COLOR) return String.format("#%06X", color() & 0xffffff);
            return String.valueOf(value);
        }
    }
    public static final class Module {
        public final String id, name, category, description;
        public final Option enabled;
        public final List<Option> options = new ArrayList<>();
        Module(String id, String name, String category, String description, boolean on) {
            this.id = id; this.name = name; this.category = category; this.description = description;
            enabled = bool("enabled", "Enabled", on); options.add(enabled);
        }
        Module add(Option... extra) { options.addAll(List.of(extra)); return this; }
        public Option get(String id) { return options.stream().filter(o -> o.id.equals(id)).findFirst().orElseThrow(); }
        public boolean on() { return enabled.on(); }
        public boolean flag(String id) { return get(id).on(); }
        public double number(String id) { return get(id).number(); }
        public int color() { return get("color").color(); }
    }
    public final List<Module> modules = List.of(
        new Module("players", "Player ESP", "RENDER", "Keep other players in sight", false).add(
            slider("distance", "Render distance", 128, 16, 256, 8, " m"), choice("style", "Render style", "Filled", "Outline", "Filled"), color("color", "Color", 0xff93a5ff)),
        new Module("storage", "Storage ESP", "RENDER", "Highlight nearby containers", false).add(
            bool("chests", "Chests", true), bool("barrels", "Barrels", true), bool("shulker", "Shulker boxes", true),
            slider("distance", "Render distance", 96, 16, 256, 8, " m"), choice("style", "Render style", "Filled", "Outline", "Filled"), color("color", "Color", 0xffe9bd78),
            bool("hoppers", "Hoppers", false),bool("furnaces", "Furnaces", false),bool("dispensers", "Dispensers / droppers", false),
            bool("perblock", "Separate block colors", false),color("chestcolor", "Chest color", 0xffe9bd78),color("barrelcolor", "Barrel color", 0xffdba787),color("shulkercolor", "Shulker color", 0xffc294dc),color("hoppercolor", "Hopper color", 0xffa3b2c4),color("furnacecolor", "Furnace color", 0xffdedede),color("dispensercolor", "Dispenser color", 0xff99b6ac)),
        new Module("spawners", "Spawner ESP", "RENDER", "Locate spawners and their mob types", false).add(
            bool("tracer", "Show tracer", true), bool("mob", "Show mob type", true), slider("distance", "Render distance", 96, 16, 256, 8, " m"),
            choice("style", "Render style", "Filled", "Outline", "Filled"), color("color", "Color", 0xffba9cf8)),
        new Module("tracers", "Tracers", "RENDER", "Draw a line to what matters", false).add(
            bool("players", "Player tracers", true), bool("storage", "Storage tracers", true), bool("spawners", "Spawner tracers", true),
            slider("distance", "Maximum distance", 128, 16, 256, 8, " m"), slider("thickness", "Line thickness", 1.5, 0.5, 4, 0.5, " px"), color("color", "Color", 0xff8dd9c1)),
        new Module("xray", "X-ray", "RENDER", "See ores and valuable blocks through terrain", false),
        new Module("fullbright", "Fullbright", "RENDER", "Keep the world clearly visible in darkness", false),
        new Module("suschunk", "SusChunk", "RENDER", "Track player movement observed during this session", false).add(
            slider("threshold", "Activity threshold", 12, 4, 100, 1, " moves")),
        new Module("freecam", "Freecam", "MOVEMENT", "Explore with a detached camera", false).add(
            slider("speed", "Speed", 16, 1, 80, 0.5, " m/s"), slider("vertical", "Vertical speed", 12, 1, 60, 0.5, " m/s")),
        new Module("fastplace", "Fast Place", "MOVEMENT", "Remove the vanilla delay while placing blocks", false),
        new Module("fly", "Fly", "MOVEMENT", "Flight in singleplayer or when the server grants flight", false).add(slider("speed", "Flight speed", 1, .2, 3, .1, "x")),
        new Module("elytraglide", "Elytra Glide", "MOVEMENT", "Stabilize pitch during an existing elytra flight", false).add(slider("pitch", "Glide pitch", -5, -30, 15, 1, " deg")),
        new Module("inventorymove", "Inventory Move", "MOVEMENT", "Use movement keys in your inventory, except while typing", false),
        new Module("autoclutch", "Auto Clutch", "MOVEMENT", "Use a suitable hotbar item to break a dangerous fall", false).add(
            slider("fall", "Fall distance", 3, 1, 8, 0.5, " m")),
        new Module("aimassist", "Aim Assist", "COMBAT", "Smoothly track visible nearby players with motion prediction", false).add(
            slider("range", "Target range", 40, 8, 64, 4, " m"), slider("angle", "Target angle", 32, 8, 60, 2, " deg"),
            slider("speed", "Turn limit", 12, 0.5, 30, 0.5, " deg/tick"),
            slider("prediction", "Motion prediction", 2.5, 0, 8, 0.5, " ticks")),
        new Module("autoeat", "Auto Eat", "MISC", "Eat hotbar food when hunger falls below the threshold", false).add(
            slider("threshold", "Hunger threshold", 16, 6, 20, 1, " food")),
        new Module("autofirework", "Auto Firework", "MOVEMENT", "Use hotbar rockets while elytra flying", false).add(
            slider("interval", "Rocket interval", 20, 10, 80, 2, " ticks")),
        new Module("automace", "Automace", "COMBAT", "Select a hotbar mace and attack the target in reach", false).add(bool("select", "Select hotbar mace", true), bool("falling", "Only while falling", true)),
        new Module("autototem", "Auto Totem", "COMBAT", "Move a carried totem into the offhand", false),
        new Module("doubleanchor", "Double Anchor", "COMBAT", "Rapidly repeat interactions with the aimed respawn anchor", false),
        new Module("mobs", "Mob ESP", "RENDER", "Separate highlights for hostile and passive mobs", false).add(bool("hostile", "Hostile mobs", true),bool("passive", "Passive mobs", true),slider("distance", "Render distance", 96, 16, 256, 8, " m"),choice("style", "Render style", "Filled", "Outline", "Filled"),color("color", "Color", 0xff91c7a1)),
        new Module("nametags", "Name Tags", "RENDER", "Show entity name tags; switch off to hide them", true),
        new Module("freelook", "Freelook", "RENDER", "Hold Alt for third-person free look; release to restore your view", false),
        new Module("trajectory", "Pearl Trajectory", "RENDER", "Preview a held pearl's path and first impact", false).add(color("color", "Color", 0xffeeeeee)),
        new Module("sprint", "Sprint", "MOVEMENT", "Sprint automatically while moving forward", false),
        new Module("nohitdelay", "No Hit Delay", "COMBAT", "Remove the client miss delay for manual clicks", false),
        new Module("autoclicker", "Autoclicker", "COMBAT", "Repeat attacks while the left mouse button is held", false).add(slider("cps", "Clicks per second", 10, 1, 20, 1, " CPS"),bool("hold", "Require left mouse held", true)),
        new Module("quickexp", "Quick XP", "MISC", "Throw XP bottles at 10 per second while right-click is held", false),
        new Module("invtotem", "Auto Inv Totem", "MISC", "Equip a carried totem when your inventory is open", false),
        new Module("fakepay", "Fake Pay", "MISC", "Intercept /pay locally and show a simulated notification", false),
        new Module("netherite", "Netherite Finder", "MISC", "Highlight ancient debris in loaded chunks", false).add(slider("distance", "Render distance", 64, 16, 128, 8, " m"),choice("style", "Render style", "Filled", "Outline", "Filled"),color("color", "Color", 0xffd69b85)),
        new Module("fakestats", "Fake Stats", "MISC", "Editable local sidebar; server statistics stay unchanged", false).add(text("title", "Sidebar title", "PLUTONIUM"),text("lines", "Rows (separate with |)", "Kills: 100|Deaths: 0|Balance: $1,000,000")),
        new Module("weather", "Weather Notifier", "HUD", "Notify when weather changes, with a cooldown", false).add(slider("cooldown", "Cooldown", 15, 3, 120, 1, " s")),
        new Module("notifications", "Notifications", "HUD", "Centered action notifications with helpful module descriptions", true).add(slider("duration", "Display duration", 4, 2, 10, 1, " s")),
        new Module("discord", "Discord Presence", "ENTERTAINMENT", "Display Plutonium in your desktop Discord activity", false).add(text("application", "Application ID", "1555301670643310712")),
        new Module("radio", "Radio", "ENTERTAINMENT", "YouTube opens in your browser; direct Ogg streams play in-game", false).add(choice("mode", "Playback", "YouTube", "YouTube", "In-game stream"),text("youtube", "YouTube playlist URL", "https://www.youtube.com/playlist?list=PLP75Cve0zgoY"),text("playlist", "Ogg/Vorbis stream URL", ""),slider("volume", "In-game volume", 50, 0, 100, 1, "%")),
        new Module("coordinates", "Coordinates", "HUD", "Your block position, including correct negative coordinates", true),
        new Module("active", "Active Modules HUD", "HUD", "A compact list of enabled features", true)
    );
    public Module module(String id) { return modules.stream().filter(m -> m.id.equals(id)).findFirst().orElseThrow(); }
    public void onChange(Consumer<Module> callback) { for (Module m : modules) for (Option o : m.options) o.changed = () -> callback.accept(m); }
    public void reset(Module m) { for (Option o : m.options) o.set(o.defaultValue); }
    static Option bool(String id, String label, boolean v) { return new Option(id, label, Kind.TOGGLE, v, 0, 1, 1, "", List.of()); }
    static Option slider(String id, String label, double v, double min, double max, double step, String unit) { return new Option(id, label, Kind.SLIDER, v, min, max, step, unit, List.of()); }
    static Option choice(String id, String label, String v, String... items) { return new Option(id, label, Kind.CHOICE, v, 0, 0, 0, "", List.of(items)); }
    static Option color(String id, String label, int v) { return new Option(id, label, Kind.COLOR, v, 0, 0, 0, "", List.of()); }
    static Option text(String id, String label, String v) { return new Option(id, label, Kind.TEXT, v, 0, 0, 0, "", List.of()); }
}
