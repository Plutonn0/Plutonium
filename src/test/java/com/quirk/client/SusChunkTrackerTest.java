package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class SusChunkTrackerTest {
    @Test void findsPlacedMinedAndReplacedBlocksWithoutPlayers() {
        var tracker=new SusChunkTracker();
        tracker.changed(1,64,1,0,1);tracker.changed(2,20,1,1,0);tracker.changed(3,64,1,1,2);
        assertEquals(new SusChunkTracker.Chunk(0,0,3),tracker.busiest(1,32).getFirst());
        assertEquals(3,tracker.blocks(0,0,256).size());
        assertTrue(tracker.busiest(4,32).isEmpty());
    }
    @Test void countsUniquePositionsAndRestorationRemovesEvidence() {
        var tracker=new SusChunkTracker();
        tracker.changed(1,2,3,0,1);tracker.changed(1,2,3,1,2);tracker.changed(1,2,3,2,2);
        assertEquals(1,tracker.busiest(1,32).getFirst().changedBlocks());
        tracker.changed(1,2,3,2,0);assertTrue(tracker.busiest(1,32).isEmpty());
        tracker.changed(1,2,3,0,1);assertEquals(1,tracker.busiest(1,32).size());
    }
    @Test void ignoresUnchangedBlocksAndKeepsNegativeCoordinatesAndMiningHeight() {
        var tracker=new SusChunkTracker();tracker.changed(0,0,0,1,1);assertTrue(tracker.busiest(1,32).isEmpty());
        tracker.changed(-17,-50,-1,2,0);
        assertEquals(new SusChunkTracker.Chunk(-2,-1,1),tracker.busiest(1,32).getFirst());
        assertEquals(new SusChunkTracker.Block(-17,-50,-1),tracker.blocks(-2,-1,256).getFirst());
        tracker.clear();assertTrue(tracker.busiest(1,32).isEmpty());
    }
    @Test void boundsMemoryAndKeepsRecentChunksInsteadOfOldBusyOnes() {
        var tracker=new SusChunkTracker();
        for(int i=0;i<300;i++)tracker.changed(i*16,64,0,0,1);
        assertEquals(256,tracker.busiest(1,1000).size());assertTrue(tracker.blocks(0,0,1).isEmpty());
        assertFalse(tracker.blocks(299,0,1).isEmpty());
        for(int i=0;i<2000;i++)tracker.changed(0,i,0,0,1);
        assertEquals(1024,tracker.blocks(0,0,5000).size());
        assertTrue(tracker.busiest(1,0).isEmpty());
    }
}
