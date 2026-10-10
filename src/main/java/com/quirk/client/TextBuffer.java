package com.quirk.client;

/** Small Unicode-safe editing model shared by the custom scoreboard fields. */
public final class TextBuffer {
    private String value;
    private int cursor,anchor;
    public TextBuffer(String value){this.value=value;cursor=anchor=value.length();}
    public String value(){return value;} public int cursor(){return cursor;}
    public int start(){return Math.min(cursor,anchor);} public int end(){return Math.max(cursor,anchor);}
    public String selection(){return value.substring(start(),end());}
    public void selectAll(){anchor=0;cursor=value.length();}
    public void at(int position,boolean select){cursor=Math.clamp(position,0,value.length());if(cursor>0&&cursor<value.length()&&Character.isLowSurrogate(value.charAt(cursor)))cursor--;if(!select)anchor=cursor;}
    public void move(int direction,boolean select){if(!select&&cursor!=anchor){at(direction<0?start():end(),false);return;}if(direction<0&&cursor>0)at(value.offsetByCodePoints(cursor,-1),select);if(direction>0&&cursor<value.length())at(value.offsetByCodePoints(cursor,1),select);}
    public void insert(String input){String clean=input.replaceAll("[\\p{Cntrl}]","");int a=start(),b=end(),available=160-(value.codePointCount(0,a)+value.codePointCount(b,value.length()));if(available<0)return;clean=clean.substring(0,clean.offsetByCodePoints(0,Math.min(available,clean.codePointCount(0,clean.length()))));value=value.substring(0,a)+clean+value.substring(b);cursor=anchor=a+clean.length();}
    public void delete(boolean backwards){if(cursor==anchor){if(backwards&&cursor>0)anchor=value.offsetByCodePoints(cursor,-1);if(!backwards&&cursor<value.length())anchor=value.offsetByCodePoints(cursor,1);}insert("");}
}
