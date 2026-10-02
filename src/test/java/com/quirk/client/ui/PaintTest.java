package com.quirk.client.ui;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class PaintTest {
    @Test void bundledFontUsesTrueTypeMetricsAndFallback() throws Exception {
        try (var definition = getClass().getResourceAsStream("/assets/plutonium/font/ui.json")) {
            assertNotNull(definition);
            var providers = com.google.gson.JsonParser.parseReader(new java.io.InputStreamReader(definition,
                java.nio.charset.StandardCharsets.UTF_8)).getAsJsonObject().getAsJsonArray("providers");
            var ttf = providers.get(0).getAsJsonObject();
            assertEquals("ttf", ttf.get("type").getAsString());
            assertEquals("minecraft:quirk/ui.ttf", ttf.get("file").getAsString());
            assertEquals(22, ttf.get("size").getAsInt());
            assertEquals("minecraft:default", providers.get(1).getAsJsonObject().get("id").getAsString());
            try (var font = getClass().getResourceAsStream("/assets/minecraft/font/quirk/ui.ttf")) {
                assertNotNull(font);
                var montserrat = java.awt.Font.createFont(java.awt.Font.TRUETYPE_FONT, font);
                assertEquals("Montserrat Medium", montserrat.getFamily(java.util.Locale.ROOT));
                assertEquals(-1, montserrat.canDisplayUpTo("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 é"));
            }
        }
    }
    @Test void roundedEdgeCoveragePreservesColorAndScalesOnlyAlpha() {
        assertEquals(0x40123456,Paint.withCoverage(0x80123456,0.5));
        assertEquals(0x00123456,Paint.withCoverage(0x80123456,0));
    }
}
