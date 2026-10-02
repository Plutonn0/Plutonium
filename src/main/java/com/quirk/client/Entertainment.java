package com.quirk.client;

import com.google.gson.*;
import net.minecraft.client.Minecraft;
import net.minecraft.client.resources.sounds.AbstractSoundInstance;
import net.minecraft.client.resources.sounds.Sound;
import net.minecraft.client.resources.sounds.SoundInstance;
import net.minecraft.client.sounds.AudioStream;
import net.minecraft.client.sounds.JOrbisAudioStream;
import net.minecraft.client.sounds.SoundManager;
import net.minecraft.client.sounds.WeighedSoundEvents;
import net.minecraft.resources.Identifier;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import java.net.*;
import java.io.*;
import java.nio.*;
import java.nio.charset.StandardCharsets;
import java.util.*;
import java.util.concurrent.*;

/** Entertainment uses official YouTube playback and Discord's local IPC, never server credentials. */
public final class Entertainment {
    private static final Gson JSON=new Gson();
    private static final ScheduledExecutorService WORKER=Executors.newSingleThreadScheduledExecutor(r->{Thread t=new Thread(r,"Plutonium entertainment");t.setDaemon(true);return t;});
    private static final Identifier RADIO_SOUND_ID=Identifier.fromNamespaceAndPath("quirk","radio_stream");
    private static final Sound RADIO_SOUND=new Sound(RADIO_SOUND_ID,random->1,random->1,1,Sound.Type.FILE,true,false,0);
    public static final Identifier RADIO_STREAM_PATH=RADIO_SOUND.getPath();
    private static final WeighedSoundEvents RADIO_EVENT=createRadioEvent();
    private static boolean started;
    private static volatile boolean discordEnabled,radioEnabled;
    private static volatile String appId="",streamUrl="",activity="In the menu",status="Discord disconnected",radioStatus="Radio stopped";
    private static volatile int volume=50;
    private static volatile RandomAccessFile pipe;
    private static volatile boolean ready;
    private static String connectedId="",lastActivity="";
    private static long nextConnect,lastPresence;
    private static RadioSound radioSound;
    private static volatile String connectedStream="",failedStream="";
    private static long radioConnectSince;

    private static WeighedSoundEvents createRadioEvent(){
        WeighedSoundEvents event=new WeighedSoundEvents(RADIO_SOUND_ID,"subtitles.quirk.radio");event.addSound(RADIO_SOUND);return event;
    }

    public static void tick(){
        Settings s=Quirk.settings();var d=s.module("discord");discordEnabled=d.on();appId=d.get("application").choice().trim();activity=net.minecraft.client.Minecraft.getInstance().level==null?"In the menu":"Exploring Minecraft 1.21.11";
        var r=s.module("radio");radioEnabled=r.on();streamUrl=r.get("playlist").choice().trim();volume=(int)r.number("volume");
        if(!started){started=true;WORKER.scheduleWithFixedDelay(Entertainment::updateDiscord,0,1,TimeUnit.SECONDS);Runtime.getRuntime().addShutdownHook(new Thread(Entertainment::closePipe,"Plutonium entertainment shutdown"));}
        updateRadio();
    }

    public static void playRadio(){Quirk.settings().module("radio").enabled.set(true);}
    public static void stopRadio(){Quirk.settings().module("radio").enabled.set(false);}
    public static void reconnectRadio(){
        if(radioSound!=null)Minecraft.getInstance().getSoundManager().stop(radioSound);
        radioSound=null;connectedStream="";failedStream="";playRadio();
    }

    private static void updateRadio(){
        Minecraft mc=Minecraft.getInstance();
        if(!radioEnabled){
            if(radioSound!=null)mc.getSoundManager().stop(radioSound);
            radioSound=null;connectedStream="";failedStream="";radioStatus="Radio stopped";return;
        }
        if(radioSound!=null&&!connectedStream.equals(streamUrl)){
            mc.getSoundManager().stop(radioSound);radioSound=null;connectedStream="";failedStream="";
        }
        if(radioSound!=null){
            radioSound.setVolume(volume/100.0f);
            if(System.nanoTime()-radioConnectSince>5_000_000_000L&&!mc.getSoundManager().isActive(radioSound)){
                failedStream=connectedStream;connectedStream="";radioSound=null;radioStatus="Stream failed; check the Ogg/Vorbis URL";
            }
            return;
        }
        if(streamUrl.isBlank()){radioStatus="Enter a direct Ogg/Vorbis stream URL";return;}
        if(failedStream.equals(streamUrl))return;
        try{
            URI uri=radioStreamUri(streamUrl);String host=uri.getHost().toLowerCase(Locale.ROOT);
            if(host.equals("youtube.com")||host.endsWith(".youtube.com")||host.equals("youtu.be")){
                radioStatus="YouTube playlists are not direct audio streams";failedStream=streamUrl;return;
            }
            radioSound=new RadioSound();connectedStream=streamUrl;radioConnectSince=System.nanoTime();radioStatus="Connecting to Ogg/Vorbis stream";
            mc.getSoundManager().play(radioSound);
        }catch(IllegalArgumentException e){radioStatus=e.getMessage();failedStream=streamUrl;}
    }

    static URI radioStreamUri(String value){
        try{
            URI uri=URI.create(value.trim());String scheme=uri.getScheme();
            if(scheme==null||(!scheme.equalsIgnoreCase("http")&&!scheme.equalsIgnoreCase("https"))||uri.getHost()==null||uri.getUserInfo()!=null)
                throw new IllegalArgumentException("Use an HTTP(S) Ogg/Vorbis stream URL");
            return uri;
        }catch(IllegalArgumentException e){
            if(e.getMessage()!=null&&e.getMessage().startsWith("Use an HTTP"))throw e;
            throw new IllegalArgumentException("Enter a valid HTTP(S) Ogg/Vorbis stream URL",e);
        }
    }

    public static AudioStream openRadioAudioStream(){
        InputStream input=null;
        try{
            URLConnection connection=radioStreamUri(streamUrl).toURL().openConnection();
            connection.setConnectTimeout(10000);connection.setReadTimeout(30000);
            connection.setRequestProperty("Accept","audio/ogg, application/ogg, */*");
            input=connection.getInputStream();AudioStream decoded=new JOrbisAudioStream(input);radioStatus="Playing Ogg/Vorbis radio";return decoded;
        }catch(IOException e){
            if(input!=null)try{input.close();}catch(IOException ignored){}
            radioStatus="Could not open or decode the Ogg/Vorbis stream";
            throw new UncheckedIOException("Could not open Ogg/Vorbis radio stream",e);
        }
    }

    private static final class RadioSound extends AbstractSoundInstance implements net.minecraft.client.resources.sounds.TickableSoundInstance {
        RadioSound(){
            super(RADIO_SOUND_ID,SoundSource.MUSIC,SoundInstance.createUnseededRandom());
            sound=RADIO_SOUND;relative=true;attenuation=SoundInstance.Attenuation.NONE;volume=Entertainment.volume/100.0f;pitch=1;
        }
        void setVolume(float next){volume=next;}
        @Override public void tick(){setVolume(Entertainment.volume/100.0f);}
        @Override public boolean isStopped(){return false;}
        @Override public WeighedSoundEvents resolve(SoundManager manager){return RADIO_EVENT;}
    }

    public static String radioStatus(){return radioStatus;}

    private static void updateDiscord(){
        try{
            if(!discordEnabled||!appId.equals(connectedId)){if(pipe!=null){if(ready)sendActivity(null);closePipe();}if(!discordEnabled){status="Discord disconnected";return;}}
            if(!appId.matches("[0-9]{16,22}")){status="Enter a valid Discord application ID";return;}
            long now=System.currentTimeMillis();
            if(pipe==null){if(now<nextConnect)return;nextConnect=now+15000;
                for(int i=0;i<10;i++){try{RandomAccessFile opened=new RandomAccessFile("\\\\.\\pipe\\discord-ipc-"+i,"rw");pipe=opened;connectedId=appId;JsonObject handshake=new JsonObject();handshake.addProperty("v",1);handshake.addProperty("client_id",appId);writeFrame(0,JSON.toJson(handshake));
                    Thread reader=new Thread(()->readDiscord(opened),"Plutonium Discord IPC");reader.setDaemon(true);reader.start();lastPresence=0;break;}catch(IOException ignored){closePipe();}}
                if(pipe==null){status="Open desktop Discord to connect";return;}
            }
            if(ready&&(now-lastPresence>=15000||!lastActivity.equals(activity))){JsonObject a=new JsonObject();a.addProperty("details","Plutonium");a.addProperty("state",activity);sendActivity(a);lastActivity=activity;lastPresence=now;}
        }catch(Exception e){status="Discord disconnected; retrying";closePipe();}
    }
    private static void readDiscord(RandomAccessFile source){
        try{while(source==pipe){int opcode=Integer.reverseBytes(source.readInt()),length=Integer.reverseBytes(source.readInt());if(length<0||length>1048576)throw new IOException("IPC frame length");byte[] data=new byte[length];source.readFully(data);
            if(opcode==3){writeFrame(4,new String(data,StandardCharsets.UTF_8));continue;}if(opcode==2)throw new IOException("IPC closed");String payload=new String(data,StandardCharsets.UTF_8);
            JsonObject frame=JsonParser.parseString(payload).getAsJsonObject();String event=frame.has("evt")&&!frame.get("evt").isJsonNull()?frame.get("evt").getAsString():"";
            if(event.equals("READY"))ready=true;
            status=event.equals("ERROR")?"Discord rejected activity; check application ID":ready?"Discord connected":"Connecting to Discord";
        }}catch(Exception ignored){if(source==pipe)closePipe();}
    }
    private static void sendActivity(JsonObject activity)throws IOException{JsonObject args=new JsonObject();args.addProperty("pid",ProcessHandle.current().pid());args.add("activity",activity==null?JsonNull.INSTANCE:activity);JsonObject data=new JsonObject();data.addProperty("cmd","SET_ACTIVITY");data.add("args",args);data.addProperty("nonce",UUID.randomUUID().toString());writeFrame(1,JSON.toJson(data));}
    private static synchronized void writeFrame(int opcode,String json)throws IOException{if(pipe==null)return;byte[] bytes=json.getBytes(StandardCharsets.UTF_8);ByteBuffer b=ByteBuffer.allocate(8+bytes.length).order(ByteOrder.LITTLE_ENDIAN).putInt(opcode).putInt(bytes.length).put(bytes);pipe.write(b.array());}
    private static synchronized void closePipe(){RandomAccessFile old=pipe;pipe=null;ready=false;connectedId="";if(old!=null)try{old.close();}catch(IOException ignored){}}
    public static String discordStatus(){return status;}
}
