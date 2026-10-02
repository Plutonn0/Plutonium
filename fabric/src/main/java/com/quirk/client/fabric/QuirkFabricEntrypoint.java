package com.quirk.client.fabric;

import com.quirk.client.Quirk;
import net.fabricmc.api.ClientModInitializer;

public final class QuirkFabricEntrypoint implements ClientModInitializer {
    @Override
    public void onInitializeClient() {
        Quirk.initializeClient();
    }
}
