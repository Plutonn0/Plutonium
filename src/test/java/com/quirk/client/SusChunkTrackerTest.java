package com.quirk.client;

import org.junit.jupiter.api.Test;
import java.util.UUID;
import static org.junit.jupiter.api.Assertions.*;

class SusChunkTrackerTest {
    @Test void countsMovementOnlyInObservedChunksAndSortsBusiestFirst() {
        var tracker = new SusChunkTracker();
        UUID player = UUID.randomUUID();
        tracker.observe(player, 0, 0);
        tracker.observe(player, 1, 0);
        tracker.observe(player, 1, 0);
        tracker.observe(player, 2, 0);
        tracker.observe(player, 17, 0);
        tracker.observe(player, 18, 0);

        assertEquals(new SusChunkTracker.Chunk(0, 0, 2), tracker.busiest(1, 4).getFirst());
        assertEquals(new SusChunkTracker.Chunk(1, 0, 2), tracker.busiest(1, 4).get(1));
        assertTrue(tracker.busiest(3, 4).isEmpty());
    }

    @Test void usesFloorBasedChunkCoordinatesAndCanClearSessionData() {
        var tracker = new SusChunkTracker();
        UUID player = UUID.randomUUID();
        tracker.observe(player, -15.5, -0.5);
        tracker.observe(player, -16.5, -0.5);

        assertEquals(new SusChunkTracker.Chunk(-2, -1, 1), tracker.busiest(1, 1).getFirst());
        tracker.clear();
        assertTrue(tracker.busiest(1, 4).isEmpty());
    }
}
