package com.quirk.client;

import net.minecraft.world.phys.Vec3;

/** Smooth propulsion while already gliding; it neither starts flight nor consumes rockets. */
public final class GlideMotion {
    private GlideMotion() {}
    public static Vec3 accelerate(Vec3 velocity, Vec3 direction, double speed, double acceleration) {
        Vec3 target=direction.normalize().scale(Math.clamp(speed,.2,3));
        Vec3 change=target.subtract(velocity);
        double limit=Math.clamp(acceleration,.01,.3);
        return velocity.add(change.length()>limit?change.normalize().scale(limit):change);
    }
}
