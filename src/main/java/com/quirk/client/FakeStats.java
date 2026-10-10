package com.quirk.client;

import net.minecraft.client.Minecraft;
import net.minecraft.world.scores.*;
import net.minecraft.network.chat.numbers.StyledFormat;
import net.minecraft.network.chat.numbers.NumberFormat;
import net.minecraft.network.chat.*;
import java.util.*;

public final class FakeStats {
    public record Row(String id, String label, String value) {}

    public record Snapshot(String scope, String title, List<Row> rows) {}

    private static Snapshot snapshot;

    private record Detected(Row row, boolean inline) {}

    private static final Map<String, Detected> names = new HashMap<>();
    private static final Map<String, Detected> owners = new HashMap<>();

    private static String nameKey(Team team, Component name) {
        return (team == null ? "" : team.getName()) + "\u0000" + name.getString();
    }

    public static void prepare() {
        tick();
    }

    private static boolean enabled() {
        return Quirk.settings() != null
                && Quirk.settings().module("fakestats").on()
                && snapshot != null;
    }

    public static MutableComponent formatName(Team team, Component name) {
        var original = PlayerTeam.formatNameForTeam(team, name);
        var detected = names.get(nameKey(team, name));
        if (!enabled() || detected == null) return original;
        var edit = Quirk.settings().sidebar.get(snapshot.scope(), detected.row.id());
        return editName(original, detected.row, detected.inline, edit);
    }

    public static MutableComponent editName(
            Component original, Row row, boolean inline, SidebarOverrides.Fields edit) {
        var result = original.copy();
        if (inline && edit.value() != null) {
            int start = result.getString().lastIndexOf(row.value());
            if (start >= 0)
                result = replaceRange(result, start, start + row.value().length(), edit.value());
        }
        if (edit.label() != null && !row.label().isEmpty()) {
            int start = result.getString().indexOf(row.label());
            if (start >= 0)
                result = replaceRange(result, start, start + row.label().length(), edit.label());
        }
        return result;
    }

    /** Preserve the server's styled text runs around the replaced value. */
    private static MutableComponent replaceRange(
            Component original, int start, int end, String value) {
        var result = Component.empty();
        int[] offset = {0};
        original.visit(
                (style, text) -> {
                    int from = offset[0], to = from + text.length();
                    if (from < start)
                        result.append(
                                Component.literal(
                                                text.substring(
                                                        0, Math.min(text.length(), start - from)))
                                        .setStyle(style));
                    if (from <= start && start < to)
                        result.append(Component.literal(value).setStyle(style));
                    if (to > end)
                        result.append(
                                Component.literal(text.substring(Math.max(0, end - from)))
                                        .setStyle(style));
                    offset[0] = to;
                    return Optional.empty();
                },
                Style.EMPTY);
        return result;
    }

    public static MutableComponent formatValue(PlayerScoreEntry entry, NumberFormat format) {
        var original = entry.formatValue(format);
        var detected = owners.get(entry.owner());
        if (!enabled() || detected == null || detected.inline) return original;
        var edit = Quirk.settings().sidebar.get(snapshot.scope(), detected.row.id());
        return edit.value() == null
                ? original
                : Component.literal(edit.value()).setStyle(original.getStyle());
    }

    public static Component formatTitle(Objective objective) {
        var original = objective.getDisplayName();
        if (!enabled()) return original;
        var edit = Quirk.settings().sidebar.get(snapshot.scope(), "$title");
        return edit.label() == null
                ? original
                : Component.literal(edit.label()).setStyle(original.getStyle());
    }

    public static Snapshot snapshot() {
        return snapshot;
    }

    public static Snapshot read(Scoreboard board, String player, String server) {
        names.clear();
        owners.clear();
        var team = board.getPlayersTeam(player);
        Objective objective = null;
        if (team != null) {
            var slot = DisplaySlot.teamColorToSlot(team.getColor());
            if (slot != null) objective = board.getDisplayObjective(slot);
        }
        if (objective == null) objective = board.getDisplayObjective(DisplaySlot.SIDEBAR);
        if (objective == null) return null;
        var format = objective.numberFormatOrDefault(StyledFormat.SIDEBAR_DEFAULT);
        var entries =
                board.listPlayerScores(objective).stream()
                        .filter(e -> !e.isHidden())
                        .sorted(
                                Comparator.comparingInt(PlayerScoreEntry::value)
                                        .reversed()
                                        .thenComparing(
                                                PlayerScoreEntry::owner,
                                                String.CASE_INSENSITIVE_ORDER))
                        .limit(15)
                        .toList();
        List<Row> rows = new ArrayList<>();
        Map<String, Integer> duplicates = new HashMap<>();
        for (var entry : entries) {
            var rowTeam = board.getPlayersTeam(entry.owner());
            String text = PlayerTeam.formatNameForTeam(rowTeam, entry.ownerName()).getString();
            var parts = SidebarOverrides.split(text, entry.formatValue(format).getString());
            String identity =
                    rowTeam != null
                            ? "team:" + rowTeam.getName() + ":row:" + parts.label()
                            : "row:" + parts.label();
            int index = duplicates.merge(identity, 1, Integer::sum);
            var row = new Row(identity + "#" + index, parts.label(), parts.value());
            rows.add(row);
            var detected =
                    new Detected(
                            row,
                            !parts.label()
                                    .equals(text.replaceAll("\u00a7[0-9A-FK-ORa-fk-or]", "")));
            names.put(nameKey(rowTeam, entry.ownerName()), detected);
            owners.put(entry.owner(), detected);
        }
        return new Snapshot(
                server + "/" + objective.getName(),
                objective.getDisplayName().getString(),
                List.copyOf(rows));
    }

    public static void tick() {
        var mc = Minecraft.getInstance();
        if (mc.level == null || mc.player == null) {
            snapshot = null;
            return;
        }
        String server =
                mc.getCurrentServer() != null
                        ? mc.getCurrentServer().ip
                        : mc.getSingleplayerServer() != null
                                ? "local:"
                                        + mc.getSingleplayerServer().getWorldData().getLevelName()
                                : "local";
        snapshot = read(mc.level.getScoreboard(), mc.player.getScoreboardName(), server);
    }
}
