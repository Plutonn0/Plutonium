package com.quirk.build;

import java.nio.file.Files;
import java.nio.file.Path;

/** Generate the provider definition for the bundled Montserrat UI font. */
public final class FontAtlasBuilder {
    public static void main(String[] args) throws Exception {
        Path resources = Path.of(args[0]);
        Path definition = resources.resolve("assets/minecraft/font/quirk.json");
        Files.createDirectories(definition.getParent());
                Files.writeString(definition, """
                        {"providers":[
                              {"type":"ttf","file":"minecraft:quirk/ui.ttf","shift":[0.0,0.0],"size":22.0,"oversample":2.0},
                            {"type":"reference","id":"minecraft:default"}
                        ]}
                        """);
    }
}
