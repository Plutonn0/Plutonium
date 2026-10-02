package com.quirk.client;

import java.util.*;

/** Session-local movement observations; it cannot infer activity in chunks not observed by this client. */
public final class SusChunkTracker {
    private static final int MAX_TRACKED_CHUNKS = 256;
    private final Map<UUID, Position> players = new HashMap<>();
    private final Map<ChunkKey, Integer> activity = new HashMap<>();

    private record Position(double x, double z) {}
    private record ChunkKey(int x, int z) {}

    public record Chunk(int x, int z, int movementSamples) {}

    public void observe(UUID player, double x, double z) {
        if (player == null || !Double.isFinite(x) || !Double.isFinite(z)) return;
        Position current = new Position(x, z);
        Position previous = players.put(player, current);
        if (previous == null || Math.hypot(x - previous.x, z - previous.z) < 0.75) return;

        ChunkKey chunk = new ChunkKey(Math.floorDiv((int)Math.floor(x), 16), Math.floorDiv((int)Math.floor(z), 16));
        activity.merge(chunk, 1, Integer::sum);
        if (activity.size() > MAX_TRACKED_CHUNKS) {
            ChunkKey leastActive = activity.entrySet().stream()
                .min(Comparator.comparingInt(Map.Entry::getValue))
                .orElseThrow().getKey();
            activity.remove(leastActive);
        }
    }

    public List<Chunk> busiest(int minimumSamples, int limit) {
        if (limit <= 0) return List.of();
        return activity.entrySet().stream()
            .filter(entry -> entry.getValue() >= minimumSamples)
            .sorted(Map.Entry.<ChunkKey, Integer>comparingByValue().reversed()
                .thenComparingInt(entry -> entry.getKey().x)
                .thenComparingInt(entry -> entry.getKey().z))
            .limit(limit)
            .map(entry -> new Chunk(entry.getKey().x, entry.getKey().z, entry.getValue()))
            .toList();
    }

    public void clear() {
        players.clear();
        activity.clear();
    }
}
