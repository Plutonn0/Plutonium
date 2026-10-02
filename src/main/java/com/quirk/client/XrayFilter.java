package com.quirk.client;

import java.util.Set;

final class XrayFilter {
    private static final Set<String> VALUABLE_BLOCKS = Set.of(
        "ancient_debris", "amethyst_block", "barrel", "chest", "coal_block", "copper_block",
        "diamond_block", "emerald_block", "ender_chest", "gold_block", "iron_block", "lapis_block",
        "netherite_block", "raw_copper_block", "raw_gold_block", "raw_iron_block", "redstone_block",
        "spawner", "trapped_chest"
    );

    private XrayFilter() {}

    static boolean isValuable(String registryPath) {
        return registryPath.endsWith("_ore") || VALUABLE_BLOCKS.contains(registryPath);
    }
}
