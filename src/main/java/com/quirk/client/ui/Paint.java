package com.quirk.client.ui;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.network.chat.*;
import net.minecraft.resources.Identifier;
import java.util.Locale;

public final class Paint {
    public static final int TEXT = 0xffeeeeee, MUTED = 0xff969696, ACCENT = 0xffcccccc, MINT = 0xffdddddd;
    private static final Style FONT = Style.EMPTY.withFont(new FontDescription.Resource(Identifier.fromNamespaceAndPath("plutonium", "ui")));
    public static Component label(String text) { return Component.literal(text).withStyle(FONT); }
    public static int width(String text) { return (int)Math.ceil(Minecraft.getInstance().font.width(label(text)) * 0.5); }
    public static void text(GuiGraphics g, String s, int x, int y, int color) {
        g.pose().pushMatrix(); g.pose().translate(x,y+6); g.pose().scale(0.5f,0.5f);
        g.drawString(Minecraft.getInstance().font, label(s), 0, 0, color, false); g.pose().popMatrix();
    }
    public static int pixelWidth(String text) { return Minecraft.getInstance().font.width(Component.literal(text)); }
    public static void pixelText(GuiGraphics g, String value, int x, int y, int color, int maxWidth, boolean centered) {
        if (maxWidth <= 0 || value.isEmpty()) return;
        String text = value.toUpperCase(Locale.ROOT);
        while (!text.isEmpty() && pixelWidth(text) > maxWidth) text = text.substring(0, text.length() - 1);
        if (text.isEmpty()) return;
        int drawX = centered ? x + (maxWidth - pixelWidth(text)) / 2 : x;
        g.drawString(Minecraft.getInstance().font, Component.literal(text), drawX, y+6, color, false);
    }
    public static void title(GuiGraphics g, String s, int x, int y, float scale, int color) {
        g.pose().pushMatrix(); g.pose().translate(x, y); g.pose().scale(scale, scale); text(g, s, 0, 0, color); g.pose().popMatrix();
    }
    public static void rounded(GuiGraphics g, int x, int y, int w, int h, int r, int color) {
        if (w <= 0 || h <= 0) return;
        r = Math.min(r, Math.min(w, h) / 2);
        for (int row = 0; row < h; row++) {
            double dy = row < r ? r - row - 0.5 : row >= h-r ? row - (h-r) + 0.5 : 0;
            double inset = dy == 0 ? 0 : r - Math.sqrt(Math.max(0, r * (double)r - dy * dy));
            double left = x + inset, right = x + w - inset;
            int first = (int)Math.ceil(left), last = (int)Math.floor(right);
            if (first < last) g.fill(first, y + row, last, y + row + 1, color);
            int leftX = (int)Math.floor(left);
            double leftCoverage = first - left;
            if (leftCoverage > 0 && leftCoverage < 1)
                g.fill(leftX, y + row, leftX + 1, y + row + 1, withCoverage(color,leftCoverage));
            int rightX = (int)Math.floor(right);
            double rightCoverage = right - rightX;
            if (rightCoverage > 0 && rightCoverage < 1 && rightX >= first)
                g.fill(rightX, y + row, rightX + 1, y + row + 1, withCoverage(color,rightCoverage));
        }
    }
    static int withCoverage(int color,double coverage) {
        int alpha=(int)Math.round(((color>>>24)&255)*coverage);
        return (color&0x00ffffff)|(alpha<<24);
    }
    public static int mix(int a, int b, double t) {
        t = Math.clamp(t, 0, 1); int out = 0;
        for (int shift = 0; shift <= 24; shift += 8) out |= ((int) (((a >>> shift) & 255) * (1-t) + ((b >>> shift) & 255) * t)) << shift;
        return out;
    }
    public static void line(GuiGraphics g, double x1, double y1, double x2, double y2, double thickness, int color) {
        double dx = x2-x1, dy = y2-y1, length = Math.hypot(dx, dy);
        if (length < 0.01) return;
        g.pose().pushMatrix(); g.pose().translate((float)x1, (float)y1); g.pose().rotate((float)Math.atan2(dy, dx));
        g.pose().scale(1, (float) thickness); g.fill(0, 0, (int)Math.ceil(length), 1, color); g.pose().popMatrix();
    }
}
