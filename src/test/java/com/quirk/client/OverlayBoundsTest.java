package com.quirk.client;

import net.minecraft.core.BlockPos;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.shapes.Shapes;
import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class OverlayBoundsTest {
    @Test void storageOutlineUsesWorldSpaceBoundsOfActualBlockShape() {
        var pos=new BlockPos(10,20,30);
        var shape=Shapes.box(0.0625,0,0.0625,0.9375,0.875,0.9375);
        assertEquals(new AABB(10.0625,20,30.0625,10.9375,20.875,30.9375),Overlay.blockBounds(shape,pos));
    }
    @Test void emptyBlockShapeFallsBackToItsBlockBounds() {
        var pos=new BlockPos(10,20,30);
        assertEquals(new AABB(pos),Overlay.blockBounds(Shapes.empty(),pos));
    }
}
