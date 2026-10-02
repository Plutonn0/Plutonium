package com.quirk.client;

import net.minecraft.world.phys.Vec3;

final class AimAssist {
    private AimAssist() {}

    static float delta(float current, float target) {
        double difference = Math.toRadians(target - current);
        return (float) Math.toDegrees(Math.atan2(Math.sin(difference), Math.cos(difference)));
    }

    static float approach(float current, float target, float maxStep) {
        return current + Math.clamp(delta(current, target), -maxStep, maxStep);
    }

    static double score(double angularError, double distance, double maxAngle, double range) {
        return angularError / maxAngle + 0.2 * Math.clamp(distance / range, 0, 1);
    }

    static float adaptiveStep(float maxStep, double angularError, double maxAngle) {
        double urgency = Math.clamp(angularError / maxAngle, 0, 1);
        return (float)(maxStep * (0.35 + 0.65 * urgency));
    }

    static Vec3 predictedOffset(Vec3 velocity, double ticks) {
        if (!Double.isFinite(ticks) || ticks <= 0) return Vec3.ZERO;
        double x = velocity.x * ticks, z = velocity.z * ticks;
        double horizontalLength = Math.hypot(x, z);
        if (horizontalLength > 1.5) {
            x *= 1.5 / horizontalLength;
            z *= 1.5 / horizontalLength;
        }
        return new Vec3(x, Math.clamp(velocity.y * ticks, -0.5, 0.5), z);
    }
}
