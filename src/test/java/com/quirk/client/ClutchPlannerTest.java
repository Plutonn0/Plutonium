package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class ClutchPlannerTest {
    @Test void prefersWaterCloseToLandingButCanUseCobwebEarlier() {
        assertEquals(ClutchPlanner.Item.WATER_BUCKET,ClutchPlanner.choose(true,false,true,false,false,false,false,3));
        assertEquals(ClutchPlanner.Item.COBWEB,ClutchPlanner.choose(true,false,true,false,false,false,false,6));
    }

    @Test void avoidsWaterInNetherAndRejectsOutOfRangeOrInvalidSurfaces() {
        assertEquals(ClutchPlanner.Item.COBWEB,ClutchPlanner.choose(true,true,true,false,false,false,false,3));
        assertEquals(ClutchPlanner.Item.NONE,ClutchPlanner.choose(true,true,false,false,false,false,false,3));
        assertEquals(ClutchPlanner.Item.NONE,ClutchPlanner.choose(false,false,true,false,false,false,false,Double.NaN));
        assertEquals(ClutchPlanner.Item.NONE,ClutchPlanner.choose(false,false,false,false,true,true,true,4));
    }
}
