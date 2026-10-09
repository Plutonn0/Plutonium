package com.quirk.client;

import java.util.*;

/** Bounded session-local block deltas, not guesses about terrain generation or player presence. */
public final class SusChunkTracker {
    private static final int MAX_CHUNKS = 256, MAX_BLOCKS_PER_CHUNK = 1024;
    private final LinkedHashMap<ChunkKey, LinkedHashMap<Block, Change>> chunks = new LinkedHashMap<>(16,.75f,true);
    private record ChunkKey(int x, int z) {}
    public record Block(int x, int y, int z) {}
    private record Change(int original, int current) {}
    public record Chunk(int x, int z, int changedBlocks) {}

    public void changed(int x, int y, int z, int before, int after) {
        if (before == after) return;
        var key = new ChunkKey(Math.floorDiv(x,16),Math.floorDiv(z,16));
        var blocks = chunks.computeIfAbsent(key, ignored -> new LinkedHashMap<>(16,.75f,true));
        var pos = new Block(x,y,z);
        var previous = blocks.get(pos);
        int original = previous == null ? before : previous.original;
        if (original == after) blocks.remove(pos);
        else blocks.put(pos,new Change(original,after));
        if (blocks.isEmpty()) chunks.remove(key);
        else if (blocks.size() > MAX_BLOCKS_PER_CHUNK) blocks.pollFirstEntry();
        if (chunks.size() > MAX_CHUNKS) chunks.pollFirstEntry();
    }
    public List<Chunk> busiest(int minimum, int limit) {
        if (limit <= 0) return List.of();
        return chunks.entrySet().stream().filter(e -> e.getValue().size() >= Math.max(1,minimum))
            .map(e -> new Chunk(e.getKey().x,e.getKey().z,e.getValue().size()))
            .sorted(Comparator.comparingInt(Chunk::changedBlocks).reversed().thenComparingInt(Chunk::x).thenComparingInt(Chunk::z))
            .limit(limit).toList();
    }
    public List<Block> blocks(int chunkX,int chunkZ,int limit) {
        var changes=chunks.get(new ChunkKey(chunkX,chunkZ));
        return changes==null || limit<=0 ? List.of() : changes.keySet().stream().limit(limit).toList();
    }
    public void clear() { chunks.clear(); }
}
