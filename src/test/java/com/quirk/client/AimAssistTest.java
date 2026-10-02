package com.quirk.client;

import net.minecraft.world.phys.Vec3;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class AimAssistTest {
    @Test void limitsEachCorrectionStep() {
        assertEquals(1.2f,AimAssist.approach(0,15,1.2f),0.0001f);
        assertEquals(-1.2f,AimAssist.approach(0,-15,1.2f),0.0001f);
    }

    @Test void choosesTheShortPathAcrossYawWraparound() {
        assertEquals(180f,AimAssist.approach(179,-179,1),0.0001f);
        assertEquals(-180f,AimAssist.approach(-179,179,1),0.0001f);
    }

    @Test void prefersAnAlignedTargetAndUsesAProgressiveTurnRate() {
        double aligned=AimAssist.score(3,28,32,40);
        double offCenter=AimAssist.score(10,8,32,40);
        assertTrue(aligned<offCenter);
        assertTrue(AimAssist.adaptiveStep(2.4f,20,32)>AimAssist.adaptiveStep(2.4f,2,32));
        assertTrue(AimAssist.adaptiveStep(2.4f,20,32)<=2.4f);
    }

    @Test void predictionIsBoundedAndIgnoresInvalidLeadTimes() {
        assertEquals(new Vec3(0,0,0),AimAssist.predictedOffset(new Vec3(12,2,0),0));
        Vec3 lead=AimAssist.predictedOffset(new Vec3(12,2,0),3);
        assertEquals(1.5,Math.hypot(lead.x,lead.z),0.0001);
        assertEquals(0.5,lead.y,0.0001);
    }
}
