package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class GeometryTest {
    @Test void crossingLineClipsAtBothEdges() { assertArrayEquals(new double[]{0,50,100,50},Geometry.clip(-500,50,500,50,100,100),0.00001); }
    @Test void behindViewportAndNonFiniteLinesAreDiscarded() {
        assertNull(Geometry.clip(-100,-10,200,-10,100,100)); assertNull(Geometry.clip(1,Double.NaN,2,3,100,100));
    }
    @Test void verticalLineAndDegeneratePointAreStable() {
        assertArrayEquals(new double[]{50,0,50,100},Geometry.clip(50,-200,50,400,100,100),0.00001);
        assertArrayEquals(new double[]{4,4,4,4},Geometry.clip(4,4,4,4,100,100));
    }
}
