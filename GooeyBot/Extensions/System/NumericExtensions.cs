// Licensed under the Apache License, Version 2.0 (the "License");

namespace System;

public static class NumericExtensions
{
    public static string ToBase64(this ulong value)
    {
        Span<byte> bytes = stackalloc byte[sizeof(ulong)];
        BitConverter.TryWriteBytes(bytes, value);

        Span<char> base64 = stackalloc char[sizeof(ulong) * 2];
        Convert.TryToBase64Chars(bytes, base64, out int written);

        base64 = base64[..written];

        // Remove padding
        while (base64[^1] == '=')
        {
            base64 = base64[..^1];
        }

        return new string(base64);
    }
}