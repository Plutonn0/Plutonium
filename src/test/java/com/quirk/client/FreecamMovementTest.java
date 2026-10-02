package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class FreecamMovementTest {
    @Test void strafingFollowsCameraRightAtCardinalYawAngles() {
        var facingSouth=FreecamMovement.direction(0,0,0,1);
        assertEquals(-1,facingSouth.x,1e-9);
        assertEquals(0,facingSouth.z,1e-9);

        var facingWest=FreecamMovement.direction(90,0,0,1);
        assertEquals(0,facingWest.x,1e-9);
        assertEquals(-1,facingWest.z,1e-9);

        var facingNorth=FreecamMovement.direction(180,0,0,1);
        assertEquals(1,facingNorth.x,1e-9);
        assertEquals(0,facingNorth.z,1e-9);
    }

    @Test void forwardAndDiagonalMovementAreNormalized() {
        var forward=FreecamMovement.direction(0,0,1,0);
        assertEquals(0,forward.x,1e-9);
        assertEquals(1,forward.z,1e-9);

        var diagonal=FreecamMovement.direction(0,0,1,1);
        assertEquals(-Math.sqrt(0.5),diagonal.x,1e-9);
        assertEquals(Math.sqrt(0.5),diagonal.z,1e-9);
    }

    @Test void forwardMovementFollowsMouseLookYawAndPitch() {
        var turned=FreecamMovement.direction(90,0,1,0);
        assertEquals(-1,turned.x,1e-9);
        assertEquals(0,turned.z,1e-9);

        var lookingDown=FreecamMovement.direction(0,30,1,0);
        assertEquals(-0.5,lookingDown.y,1e-9);
        assertEquals(Math.sqrt(0.75),lookingDown.z,1e-9);
    }
}
