package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class XrayFilterTest {
    @Test void preservesOresAndValuableBlocks() {
        assertTrue(XrayFilter.isValuable("diamond_ore"));
        assertTrue(XrayFilter.isValuable("deepslate_emerald_ore"));
        assertTrue(XrayFilter.isValuable("ancient_debris"));
        assertTrue(XrayFilter.isValuable("diamond_block"));
        assertTrue(XrayFilter.isValuable("spawner"));
        assertTrue(XrayFilter.isValuable("ender_chest"));
    }

    @Test void hidesOrdinaryTerrainAndDecorativeBlocks() {
        assertFalse(XrayFilter.isValuable("stone"));
        assertFalse(XrayFilter.isValuable("dirt"));
        assertFalse(XrayFilter.isValuable("oak_planks"));
    }
}
