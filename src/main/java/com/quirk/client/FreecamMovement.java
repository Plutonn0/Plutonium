package com.quirk.client;

import net.minecraft.world.phys.Vec3;

final class FreecamMovement {
    private FreecamMovement() {}

    static Vec3 direction(double yawDegrees, double pitchDegrees, double forward, double strafe) {
        double yaw = Math.toRadians(yawDegrees);
        double pitch = Math.toRadians(pitchDegrees);
        double length = Math.max(1, Math.hypot(forward, strafe));
        forward /= length;
        strafe /= length;
        double horizontalForward = Math.cos(pitch) * forward;
        return new Vec3(
            -Math.sin(yaw) * horizontalForward - Math.cos(yaw) * strafe,
            -Math.sin(pitch) * forward,
            Math.cos(yaw) * horizontalForward - Math.sin(yaw) * strafe
        );
    }
}
