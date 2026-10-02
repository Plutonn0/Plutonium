package com.quirk.client;

final class ClutchPlanner {
    enum Item { NONE, WATER_BUCKET, COBWEB, POWDER_SNOW_BUCKET, HAY_BLOCK, SLIME_BLOCK, HONEY_BLOCK }

    private ClutchPlanner() {}

    static Item choose(boolean water, boolean nether, boolean cobweb, boolean powderSnow,
            boolean hay, boolean slime, boolean honey, double groundDistance) {
        if (!Double.isFinite(groundDistance) || groundDistance < 0) return Item.NONE;
        if (water && !nether && groundDistance <= 4) return Item.WATER_BUCKET;
        if (cobweb && groundDistance <= 8) return Item.COBWEB;
        if (powderSnow && groundDistance <= 4) return Item.POWDER_SNOW_BUCKET;
        if (groundDistance <= 2.5) {
            if (hay) return Item.HAY_BLOCK;
            if (slime) return Item.SLIME_BLOCK;
            if (honey) return Item.HONEY_BLOCK;
        }
        return Item.NONE;
    }
}
