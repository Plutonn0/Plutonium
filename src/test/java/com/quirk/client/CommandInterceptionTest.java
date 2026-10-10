package com.quirk.client;

import org.junit.jupiter.api.Test;
import static org.junit.jupiter.api.Assertions.*;

class CommandInterceptionTest {
    @Test
    void paymentMatchesWholeCommandTokenIncludingNamespaces() {
        for (String value :
                new String[] {
                    "pay Steve 10",
                    " /pay Steve 10 ",
                    "PAY Steve 10",
                    "essentials:pay Steve 10",
                    "minecraft:pay"
                }) assertTrue(Quirk.isPayCommand(value), value);
        for (String value :
                new String[] {
                    "say pay Steve 10",
                    "payment Steve 10",
                    "repay Steve 10",
                    "",
                    "   ",
                    "msg Steve /pay"
                }) assertFalse(Quirk.isPayCommand(value), value);
    }

    @Test
    void paymentAmountsNormalizeSupportedSuffixesAndRejectMalformedInput() {
        assertEquals("2.5K", Quirk.normalizePayAmount("2.5k"));
        assertEquals("10M", Quirk.normalizePayAmount("10m"));
        assertEquals("1B", Quirk.normalizePayAmount("1b"));
        assertEquals("3M", Quirk.normalizePayAmount("3M"));
        assertEquals("100", Quirk.normalizePayAmount("100"));
        assertEquals("1,250.50", Quirk.normalizePayAmount("1,250.50"));
        for (String value :
                new String[] {"2t", "1kk", "5mb", "k", "NaN", "1e6", "1.2.3m", "1,25k", "10$", ""})
            assertNull(Quirk.normalizePayAmount(value), value);
    }
}
