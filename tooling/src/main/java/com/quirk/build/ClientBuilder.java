package com.quirk.build;

import net.fabricmc.mappingio.MappingReader;
import net.fabricmc.mappingio.tree.MemoryMappingTree;
import net.fabricmc.tinyremapper.*;
import org.objectweb.asm.*;
import org.objectweb.asm.tree.*;
import java.nio.file.*;
import java.io.*;
import java.util.*;
import java.util.jar.*;
import java.net.*;

/** Build-time integration only. The shipped client has no loader, agent or ASM dependency. */
public final class ClientBuilder {
    public static void main(String[] args) throws Exception {
        Path game = Path.of(args[1]);
        MemoryMappingTree tree = new MemoryMappingTree();
        MappingReader.read(game.resolve("mappings.txt"), tree);
        if (args[0].equals("map")) {
            remap(game.resolve("client.jar"), game.resolve("named.jar"), tree, false, game);
        } else {
            Path merged = game.resolve("patched-named.jar");
            Map<String, byte[]> entries = readJar(game.resolve("named.jar"));
            List<URL> urls = new ArrayList<>(); urls.add(game.resolve("named.jar").toUri().toURL());
            try (var files=Files.list(game.resolve("libraries"))) { for(Path p:files.toList()) urls.add(p.toUri().toURL()); }
            URLClassLoader frameLoader = new URLClassLoader(urls.toArray(URL[]::new), ClientBuilder.class.getClassLoader());
            int count = 0;
            for (String name : List.of("net/minecraft/client/Minecraft.class", "net/minecraft/client/KeyboardHandler.class",
                    "net/minecraft/client/gui/Gui.class", "net/minecraft/client/Camera.class",
                    "net/minecraft/world/entity/Entity.class", "net/minecraft/client/player/KeyboardInput.class", "net/minecraft/client/renderer/GameRenderer.class",
                    "net/minecraft/client/renderer/LightTexture.class", "net/minecraft/client/sounds/SoundBufferLibrary.class", "net/minecraft/world/level/block/state/BlockBehaviour$BlockStateBase.class",
                    "net/minecraft/client/renderer/LevelRenderer.class", "net/minecraft/client/renderer/entity/EntityRenderer.class", "net/minecraft/client/renderer/entity/player/AvatarRenderer.class",
                    "net/minecraft/world/level/chunk/LevelChunk.class", "net/minecraft/client/multiplayer/ClientPacketListener.class", "net/minecraft/client/renderer/block/LiquidBlockRenderer.class")) {
                byte[] bytes = entries.get(name);
                if (bytes == null) throw new IllegalStateException("Missing integration class: " + name);
                entries.put(name, patch(bytes, frameLoader)); count++;
            }
            entries.putAll(readJar(Path.of(args[2])));
            entries.put("com/quirk/client/EngineAccess.class", bridge());
            writeJar(merged, entries);
            frameLoader.close();
            Path out = Path.of(args[3]); Files.createDirectories(out.getParent());
            remap(merged, out, tree, true, game);
            System.out.println("Built standalone Plutonium; integrated " + count + " engine classes.");
        }
    }
    static void remap(Path input, Path output, MemoryMappingTree tree, boolean toObfuscated, Path game) throws Exception {
        String source = tree.getSrcNamespace(), target = tree.getDstNamespaces().getFirst();
        TinyRemapper mapper = TinyRemapper.newRemapper().withMappings(TinyUtils.createMappingProvider(tree,
                toObfuscated ? source : target, toObfuscated ? target : source)).renameInvalidLocals(true).build();
        Files.deleteIfExists(output);
        try (OutputConsumerPath consumer = new OutputConsumerPath.Builder(output).build()) {
            consumer.addNonClassFiles(input, NonClassCopyMode.FIX_META_INF, mapper);
            try (var files = Files.list(game.resolve("libraries"))) { mapper.readClassPath(files.filter(p -> p.toString().endsWith(".jar")).toArray(Path[]::new)); }
            mapper.readInputs(input); mapper.apply(consumer);
        } finally { mapper.finish(); }
    }
    static Map<String,byte[]> readJar(Path path) throws IOException {
        Map<String,byte[]> out = new LinkedHashMap<>();
        try (JarInputStream in = new JarInputStream(Files.newInputStream(path))) {
            JarEntry e; while ((e = in.getNextJarEntry()) != null) {
                String n = e.getName();
                boolean signature = n.equalsIgnoreCase("META-INF/MANIFEST.MF") || n.matches("(?i)META-INF/[^/]+\\.(SF|RSA|DSA)");
                if (!e.isDirectory() && !signature) out.put(n, in.readAllBytes());
            }
        } return out;
    }
    static void writeJar(Path path, Map<String,byte[]> entries) throws IOException {
        try (JarOutputStream out = new JarOutputStream(Files.newOutputStream(path))) {
            for (var e : entries.entrySet()) { out.putNextEntry(new JarEntry(e.getKey())); out.write(e.getValue()); out.closeEntry(); }
        }
    }
    static AbstractInsnNode call(String name, String desc) {
        return new MethodInsnNode(Opcodes.INVOKESTATIC, "com/quirk/client/Quirk", name, desc, false);
    }
    static byte[] patch(byte[] bytes, ClassLoader frameLoader) {
        ClassNode c = new ClassNode(); new ClassReader(bytes).accept(c, 0);
        int hooks = 0;
        for (MethodNode m : c.methods) {
            if(c.name.equals("net/minecraft/world/level/chunk/LevelChunk") && m.name.equals("setBlockState") && m.desc.equals("(Lnet/minecraft/core/BlockPos;Lnet/minecraft/world/level/block/state/BlockState;I)Lnet/minecraft/world/level/block/state/BlockState;")) {
                for(var ins:m.instructions.toArray())if(ins.getOpcode()==Opcodes.ARETURN){
                    InsnList h=new InsnList();h.add(new InsnNode(Opcodes.DUP));h.add(new VarInsnNode(Opcodes.ALOAD,0));h.add(new VarInsnNode(Opcodes.ALOAD,1));
                    h.add(call("blockChanged","(Lnet/minecraft/world/level/block/state/BlockState;Lnet/minecraft/world/level/chunk/LevelChunk;Lnet/minecraft/core/BlockPos;)V"));m.instructions.insertBefore(ins,h);
                } hooks++;
            }

            String owner = c.name;
            if(owner.endsWith("/Minecraft")&&m.name.equals("stop")&&m.desc.equals("()V")){
                m.instructions.insert(call("saveVideoSettings","()V"));hooks++;
            }
            if(owner.endsWith("/SoundBufferLibrary")&&m.name.equals("getStream")){
                InsnList h=new InsnList();h.add(new VarInsnNode(Opcodes.ALOAD,1));h.add(call("radioStream","(Lnet/minecraft/resources/Identifier;)Ljava/util/concurrent/CompletableFuture;"));h.add(new InsnNode(Opcodes.DUP));LabelNode next=new LabelNode();h.add(new JumpInsnNode(Opcodes.IFNULL,next));h.add(new InsnNode(Opcodes.ARETURN));h.add(next);h.add(new InsnNode(Opcodes.POP));m.instructions.insert(h);hooks++;
            }
            if(owner.endsWith("/Minecraft")&&m.name.equals("startAttack")) {
                m.access=(m.access&~Opcodes.ACC_PRIVATE)|Opcodes.ACC_PUBLIC;
                InsnList h=new InsnList();h.add(call("noHitDelay","()Z"));LabelNode next=new LabelNode();h.add(new JumpInsnNode(Opcodes.IFEQ,next));
                h.add(new VarInsnNode(Opcodes.ALOAD,0));h.add(new InsnNode(Opcodes.ICONST_0));h.add(new FieldInsnNode(Opcodes.PUTFIELD,owner,"missTime","I"));h.add(next);m.instructions.insert(h);hooks++;
            }
            if(owner.endsWith("/LevelRenderer")&&m.name.equals("renderLevel")) {
                for(var ins:m.instructions.toArray())if(ins.getOpcode()==Opcodes.RETURN){InsnList h=new InsnList();
                    h.add(new VarInsnNode(Opcodes.ALOAD,4));h.add(new VarInsnNode(Opcodes.ALOAD,5));h.add(new VarInsnNode(Opcodes.ALOAD,6));h.add(new VarInsnNode(Opcodes.ALOAD,2));
                    h.add(call("renderWorld","(Lnet/minecraft/client/Camera;Lorg/joml/Matrix4f;Lorg/joml/Matrix4f;Lnet/minecraft/client/DeltaTracker;)V"));m.instructions.insertBefore(ins,h);
                }hooks++;
            }
            if(owner.endsWith("/ClientPacketListener")&&(m.name.equals("sendCommand")||m.name.equals("sendUnattendedCommand"))) {
                InsnList h=new InsnList();h.add(new VarInsnNode(Opcodes.ALOAD,1));h.add(call("interceptCommand","(Ljava/lang/String;)Z"));LabelNode next=new LabelNode();h.add(new JumpInsnNode(Opcodes.IFEQ,next));h.add(new InsnNode(Opcodes.RETURN));h.add(next);m.instructions.insert(h);hooks++;
            }
            if(((owner.endsWith("/EntityRenderer")||owner.endsWith("/AvatarRenderer"))&&m.name.equals("submitNameTag")&&(!owner.endsWith("/AvatarRenderer")||m.desc.startsWith("(Lnet/minecraft/client/renderer/entity/state/AvatarRenderState;")))||(owner.endsWith("/Gui")&&m.name.equals("renderScoreboardSidebar"))||(owner.endsWith("/LiquidBlockRenderer")&&m.name.equals("tesselate"))) {
                InsnList h=new InsnList();h.add(call(owner.endsWith("/EntityRenderer")||owner.endsWith("/AvatarRenderer")?"hideNameTags":owner.endsWith("/Gui")?"fakeStats":"xray","()Z"));LabelNode next=new LabelNode();h.add(new JumpInsnNode(Opcodes.IFEQ,next));h.add(new InsnNode(Opcodes.RETURN));h.add(next);m.instructions.insert(h);hooks++;
            }
            if(owner.endsWith("/BlockBehaviour$BlockStateBase")&&m.name.equals("isSolidRender")) {
                InsnList h=new InsnList();h.add(call("xray","()Z"));LabelNode next=new LabelNode();h.add(new JumpInsnNode(Opcodes.IFEQ,next));h.add(new InsnNode(Opcodes.ICONST_0));h.add(new InsnNode(Opcodes.IRETURN));h.add(next);m.instructions.insert(h);hooks++;
            }
            if (owner.endsWith("/Minecraft") && m.name.equals("createTitle") && m.desc.equals("()Ljava/lang/String;")) {
                m.instructions.clear(); m.tryCatchBlocks.clear(); if(m.localVariables!=null) m.localVariables.clear();
                m.instructions.add(new LdcInsnNode("Plutonium | Minecraft 1.21.11"));
                m.instructions.add(new InsnNode(Opcodes.ARETURN)); hooks++;
            }
            if (owner.endsWith("/Minecraft") && m.name.equals("tick") && m.desc.equals("()V")) {
                InsnList h = new InsnList();
                h.add(call("tick", "()V"));
                h.add(new MethodInsnNode(Opcodes.INVOKESTATIC, "com/quirk/client/SmokeTest", "tick", "()V", false));
                h.add(call("fastPlace", "()Z"));
                LabelNode resetDelay = new LabelNode(), skipReset = new LabelNode();
                h.add(new JumpInsnNode(Opcodes.IFNE, resetDelay));
                h.add(call("doubleAnchor", "()Z"));
                h.add(new JumpInsnNode(Opcodes.IFEQ, skipReset));
                h.add(resetDelay);
                h.add(new VarInsnNode(Opcodes.ALOAD, 0));
                h.add(new InsnNode(Opcodes.ICONST_0));
                h.add(new FieldInsnNode(Opcodes.PUTFIELD, owner, "rightClickDelay", "I"));
                h.add(skipReset);
                m.instructions.insert(h); hooks++;
            }
            if ((owner.endsWith("/Minecraft") && m.name.equals("handleKeybinds") && m.desc.equals("()V"))
                    || (owner.endsWith("/GameRenderer") && m.name.equals("renderItemInHand"))) {
                InsnList h = new InsnList(); h.add(call("freecam", "()Z"));
                LabelNode next = new LabelNode(); h.add(new JumpInsnNode(Opcodes.IFEQ,next)); h.add(new InsnNode(Opcodes.RETURN)); h.add(next);
                m.instructions.insert(h); hooks++;
            }
            if (owner.endsWith("/GameRenderer") && m.name.equals("getFov")) {
                m.access = (m.access & ~Opcodes.ACC_PRIVATE) | Opcodes.ACC_PUBLIC; hooks++;
            }
            if (owner.endsWith("/LightTexture") && m.name.equals("getBrightness")
                    && (m.desc.equals("(Lnet/minecraft/world/level/dimension/DimensionType;I)F") || m.desc.equals("(FI)F"))) {
                InsnList h = new InsnList(); h.add(call("fullbright", "()Z"));
                LabelNode next = new LabelNode(); h.add(new JumpInsnNode(Opcodes.IFEQ,next));
                h.add(new InsnNode(Opcodes.FCONST_1)); h.add(new InsnNode(Opcodes.FRETURN)); h.add(next);
                m.instructions.insert(h); hooks++;
            }
            if (owner.endsWith("/LightTexture") && m.name.equals("updateLightTexture") && m.desc.equals("(F)V")) {
                for(var ins:m.instructions.toArray())if(ins.getOpcode()==Opcodes.RETURN){InsnList h=new InsnList();h.add(new VarInsnNode(Opcodes.ALOAD,0));h.add(call("lightmap","(Lnet/minecraft/client/renderer/LightTexture;)V"));m.instructions.insertBefore(ins,h);}
                boolean darknessFound=false, brightnessFound=false;
                for (var ins : m.instructions.toArray()) {
                    if (!(ins instanceof VarInsnNode store) || store.getOpcode()!=Opcodes.FSTORE) continue;
                    int local=store.var;
                    if (local!=12 && local!=16) continue;
                    InsnList h = new InsnList();
                    h.add(new VarInsnNode(Opcodes.FLOAD,local));
                    h.add(call(local==12 ? "lightmapDarkness" : "lightmapBrightness", "(F)F"));
                    h.add(new VarInsnNode(Opcodes.FSTORE,local));
                    m.instructions.insert(ins,h);
                    if(local==12) darknessFound=true; else brightnessFound=true;
                }
                if(!darknessFound||!brightnessFound)
                    throw new IllegalStateException("Could not locate darkness and brightness lightmap values in " + c.name);
                hooks++;
            }
            if (owner.endsWith("/BlockBehaviour$BlockStateBase") && m.name.equals("getRenderShape")
                    && m.desc.equals("()Lnet/minecraft/world/level/block/RenderShape;")) {
                boolean foundReturn=false;
                for (var ins : m.instructions.toArray()) if (ins.getOpcode() == Opcodes.ARETURN) {
                    InsnList h = new InsnList(); h.add(new VarInsnNode(Opcodes.ALOAD,0)); h.add(new InsnNode(Opcodes.SWAP));
                    h.add(call("renderShape", "(Lnet/minecraft/world/level/block/state/BlockBehaviour$BlockStateBase;Lnet/minecraft/world/level/block/RenderShape;)Lnet/minecraft/world/level/block/RenderShape;"));
                    m.instructions.insertBefore(ins,h); foundReturn=true;
                }
                if(foundReturn) hooks++;
            }
            if (owner.endsWith("/BlockBehaviour$BlockStateBase") && m.name.equals("getFaceOcclusionShape")
                    && m.desc.equals("(Lnet/minecraft/core/Direction;)Lnet/minecraft/world/phys/shapes/VoxelShape;")) {
                boolean foundReturn=false;
                for (var ins : m.instructions.toArray()) if (ins.getOpcode() == Opcodes.ARETURN) {
                    InsnList h = new InsnList();
                    h.add(call("faceOcclusionShape", "(Lnet/minecraft/world/phys/shapes/VoxelShape;)Lnet/minecraft/world/phys/shapes/VoxelShape;"));
                    m.instructions.insertBefore(ins,h); foundReturn=true;
                }
                if(foundReturn) hooks++;
            }
            if (owner.endsWith("/KeyboardHandler") && m.name.equals("keyPress")) {
                InsnList hook = new InsnList();
                hook.add(new VarInsnNode(Opcodes.ILOAD, 3)); hook.add(new VarInsnNode(Opcodes.ALOAD, 4));
                hook.add(call("key", "(ILnet/minecraft/client/input/KeyEvent;)Z"));
                LabelNode next = new LabelNode(); hook.add(new JumpInsnNode(Opcodes.IFEQ, next));
                hook.add(new InsnNode(Opcodes.RETURN)); hook.add(next); m.instructions.insert(hook); hooks++;
            }
            if (owner.endsWith("/Gui") && m.name.equals("render") && m.desc.startsWith("(Lnet/minecraft/client/gui/GuiGraphics;")) {
                for (var ins : m.instructions.toArray()) if (ins.getOpcode() == Opcodes.RETURN) {
                    InsnList h = new InsnList(); h.add(new VarInsnNode(Opcodes.ALOAD, 1));
                    h.add(call("renderHud", "(Lnet/minecraft/client/gui/GuiGraphics;)V")); m.instructions.insertBefore(ins, h);
                } hooks++;
            }
            if (owner.endsWith("/Camera") && m.name.equals("setup")) {
                for (var ins : m.instructions.toArray()) if (ins.getOpcode() == Opcodes.RETURN) {
                    InsnList h = new InsnList(); h.add(new VarInsnNode(Opcodes.ALOAD, 0));
                    h.add(call("camera", "(Lnet/minecraft/client/Camera;)V")); m.instructions.insertBefore(ins, h);
                } hooks++;
            }
            if (owner.endsWith("/Camera") && m.name.equals("isDetached") && m.desc.equals("()Z")) {
                InsnList h = new InsnList(); h.add(call("freecam", "()Z"));
                LabelNode next = new LabelNode(); h.add(new JumpInsnNode(Opcodes.IFEQ,next));
                h.add(new InsnNode(Opcodes.ICONST_1)); h.add(new InsnNode(Opcodes.IRETURN)); h.add(next); m.instructions.insert(h); hooks++;
            }
            // Expose the camera setters for direct calls; compile-time code uses a generated bridge below.
            if (owner.endsWith("/Camera") && (m.name.equals("setPosition") || m.name.equals("setRotation"))) {
                m.access = (m.access & ~Opcodes.ACC_PROTECTED) | Opcodes.ACC_PUBLIC;
            }
            if (owner.endsWith("/Entity") && m.name.equals("turn") && m.desc.equals("(DD)V")) {
                InsnList h = new InsnList(); h.add(new VarInsnNode(Opcodes.ALOAD, 0));
                h.add(new VarInsnNode(Opcodes.DLOAD, 1)); h.add(new VarInsnNode(Opcodes.DLOAD, 3));
                h.add(call("turn", "(Lnet/minecraft/world/entity/Entity;DD)Z"));
                LabelNode next = new LabelNode(); h.add(new JumpInsnNode(Opcodes.IFEQ, next));
                h.add(new InsnNode(Opcodes.RETURN)); h.add(next); m.instructions.insert(h); hooks++;
            }
            if (owner.endsWith("/KeyboardInput") && m.name.equals("tick") && m.desc.equals("()V")) {
                InsnList h = new InsnList(); h.add(call("freecam", "()Z"));
                LabelNode next = new LabelNode(); h.add(new JumpInsnNode(Opcodes.IFEQ,next));
                h.add(new VarInsnNode(Opcodes.ALOAD,0));
                h.add(new FieldInsnNode(Opcodes.GETSTATIC,"net/minecraft/world/entity/player/Input","EMPTY","Lnet/minecraft/world/entity/player/Input;"));
                h.add(new FieldInsnNode(Opcodes.PUTFIELD,"net/minecraft/client/player/ClientInput","keyPresses","Lnet/minecraft/world/entity/player/Input;"));
                h.add(new VarInsnNode(Opcodes.ALOAD,0));
                h.add(new FieldInsnNode(Opcodes.GETSTATIC,"net/minecraft/world/phys/Vec2","ZERO","Lnet/minecraft/world/phys/Vec2;"));
                h.add(new FieldInsnNode(Opcodes.PUTFIELD,"net/minecraft/client/player/ClientInput","moveVector","Lnet/minecraft/world/phys/Vec2;"));
                h.add(new InsnNode(Opcodes.RETURN)); h.add(next); m.instructions.insert(h); hooks++;
            }
        }
        int expected=c.name.endsWith("/Minecraft")?5:(c.name.endsWith("/Camera")||c.name.endsWith("/GameRenderer")||c.name.endsWith("/Gui")||c.name.endsWith("/ClientPacketListener"))?2:
                c.name.endsWith("/LightTexture")?3:c.name.endsWith("/BlockBehaviour$BlockStateBase")?3:1;
        if (hooks != expected) throw new IllegalStateException("Expected " + expected + " hooks in " + c.name + ", found " + hooks);
        // Mojang's optimized frames can discard even `this` at a return. Recompute using
        // the actual game hierarchy because the injected return hooks need those locals.
        ClassWriter out = new ClassWriter(ClassWriter.COMPUTE_FRAMES) {
            @Override protected ClassLoader getClassLoader() { return frameLoader; }
        };
        c.accept(out); return out.toByteArray();
    }
    static byte[] bridge() {
        ClassWriter c = new ClassWriter(ClassWriter.COMPUTE_MAXS);
        c.visit(Opcodes.V21, Opcodes.ACC_PUBLIC | Opcodes.ACC_FINAL, "com/quirk/client/EngineAccess", null, "java/lang/Object", null);
        MethodVisitor m = c.visitMethod(Opcodes.ACC_PUBLIC|Opcodes.ACC_STATIC,"position","(Lnet/minecraft/client/Camera;DDD)V",null,null);
        m.visitCode(); m.visitVarInsn(Opcodes.ALOAD,0); m.visitVarInsn(Opcodes.DLOAD,1); m.visitVarInsn(Opcodes.DLOAD,3); m.visitVarInsn(Opcodes.DLOAD,5);
        m.visitMethodInsn(Opcodes.INVOKEVIRTUAL,"net/minecraft/client/Camera","setPosition","(DDD)V",false); m.visitInsn(Opcodes.RETURN); m.visitMaxs(0,0); m.visitEnd();
        m = c.visitMethod(Opcodes.ACC_PUBLIC|Opcodes.ACC_STATIC,"rotation","(Lnet/minecraft/client/Camera;FF)V",null,null);
        m.visitCode(); m.visitVarInsn(Opcodes.ALOAD,0); m.visitVarInsn(Opcodes.FLOAD,1); m.visitVarInsn(Opcodes.FLOAD,2);
        m.visitMethodInsn(Opcodes.INVOKEVIRTUAL,"net/minecraft/client/Camera","setRotation","(FF)V",false); m.visitInsn(Opcodes.RETURN); m.visitMaxs(0,0); m.visitEnd();
        m = c.visitMethod(Opcodes.ACC_PUBLIC|Opcodes.ACC_STATIC,"fov","(Lnet/minecraft/client/renderer/GameRenderer;Lnet/minecraft/client/Camera;F)F",null,null);
        m.visitCode(); m.visitVarInsn(Opcodes.ALOAD,0); m.visitVarInsn(Opcodes.ALOAD,1); m.visitVarInsn(Opcodes.FLOAD,2); m.visitInsn(Opcodes.ICONST_1);
        m.visitMethodInsn(Opcodes.INVOKEVIRTUAL,"net/minecraft/client/renderer/GameRenderer","getFov","(Lnet/minecraft/client/Camera;FZ)F",false);
        m.visitInsn(Opcodes.FRETURN); m.visitMaxs(0,0); m.visitEnd();
        m=c.visitMethod(Opcodes.ACC_PUBLIC|Opcodes.ACC_STATIC,"attack","(Lnet/minecraft/client/Minecraft;)Z",null,null);
        m.visitCode();m.visitVarInsn(Opcodes.ALOAD,0);m.visitMethodInsn(Opcodes.INVOKEVIRTUAL,"net/minecraft/client/Minecraft","startAttack","()Z",false);m.visitInsn(Opcodes.IRETURN);m.visitMaxs(0,0);m.visitEnd();
        c.visitEnd(); return c.toByteArray();
    }
}
