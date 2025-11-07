using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Wrapper;

// TODO： next steps
// - prepare guidelines for using the wrapper
// - implement different examples
    // - write example for generating signals with AWG
// - set up trigger circuit and write example for triggered captures
class DebugReadExample
{
    const int BUFFER_SIZE = 10000;
    static volatile bool g_ready = false;

    static uint BlockReady(short handle, uint status, IntPtr pParameter)
    {
        Console.WriteLine($"[Callback] ps5000aBlockReady() called with status=0x{status:X8}");
        if (status != 0x0000003A) // PICO_CANCELLED = 0x3A
            g_ready = true;
        return 0;
    }

    static int AdcToMv(short raw, ApiWrapper.PS5000A_RANGE rangeIndex, short maxADC)
    {
        int[] inputRanges = { 10, 20, 50, 100, 200, 500, 1000, 2000, 5000, 10000, 20000, 50000 };
        return (raw * inputRanges[(int)rangeIndex]) / maxADC;
    }

    static void Main()
    {
        short handle;
        uint status;
        short maxADC;
        short[] buffer = new short[BUFFER_SIZE];
        uint timebase = 128;
        int timeIntervalNs, maxSamples;
        int timeIndisposed;

        Console.WriteLine("=== PicoScope 5000A Debug Read ===");

        // --- 1. Open device ---
        status = ApiWrapper.ps5000aOpenUnit(out handle, null, ApiWrapper.PS5000A_DEVICE_RESOLUTION.PS5000A_DR_8BIT);
        Console.WriteLine($"[OpenUnit] status=0x{status:X8}, handle={handle}");
        if (status != 0)
            return;

        // --- 2. Get max ADC value ---
        status = ApiWrapper.ps5000aMaximumValue(handle, out maxADC);
        Console.WriteLine($"[MaximumValue] status=0x{status:X8}, maxADC={maxADC}");

        // --- 3. Configure Channel A ---
        status = ApiWrapper.ps5000aSetChannel(
            handle,
            ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
            1,
            ApiWrapper.PS5000A_COUPLING.PS5000A_DC,
            ApiWrapper.PS5000A_RANGE.PS5000A_2V,
            0.0f);
        Console.WriteLine($"[SetChannel] status=0x{status:X8}");

        // --- 4. Set data buffer ---
        status = ApiWrapper.ps5000aSetDataBuffer(
            handle,
            ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
            buffer,
            buffer.Length,
            0,
            ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE);
        Console.WriteLine($"[SetDataBuffer] status=0x{status:X8}");

        // --- 5. Verify timebase ---
        status = ApiWrapper.ps5000aGetTimebase(handle, timebase, BUFFER_SIZE, out timeIntervalNs, out maxSamples, 0);
        Console.WriteLine($"[GetTimebase] status=0x{status:X8}, timeIntervalNs={timeIntervalNs}, maxSamples={maxSamples}");

        // --- 6. Disable trigger ---
        status = ApiWrapper.ps5000aSetSimpleTrigger(
            handle, 0,
            ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
            0,
            ApiWrapper.PS5000A_THRESHOLD_DIRECTION.PS5000A_RISING,
            0, 0);
        Console.WriteLine($"[SetSimpleTrigger] status=0x{status:X8}");

        // --- 7. Start capture ---
        ApiWrapper.ps5000aBlockReady callback = new ApiWrapper.ps5000aBlockReady(BlockReady);
        g_ready = false;
        status = ApiWrapper.ps5000aRunBlock(handle, 0, BUFFER_SIZE, timebase, out timeIndisposed, 0, callback, IntPtr.Zero);
        Console.WriteLine($"[RunBlock] status=0x{status:X8}, timeIndisposed={timeIndisposed}");

        if (status != 0)
        {
            ApiWrapper.ps5000aCloseUnit(handle);
            return;
        }

        // --- 8. Wait until ready ---
        Console.WriteLine("[Wait] Waiting for acquisition to complete...");
        int waitCount = 0;
        while (!g_ready)
        {
            Thread.Sleep(10);
            if (++waitCount % 50 == 0)
                Console.WriteLine($"[Wait] Still waiting... ({waitCount * 10} ms)");
        }
        Console.WriteLine("[Wait] Acquisition complete!");

        // --- 9. Get values ---
        uint sampleCount = BUFFER_SIZE;
        status = ApiWrapper.ps5000aGetValues(
            handle, 0, out sampleCount, 1,
            ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE, 0, null);
        Console.WriteLine($"[GetValues] status=0x{status:X8}, sampleCount={sampleCount}");

        // --- 10. Check first few samples ---
        Console.WriteLine("[Preview] First 10 raw samples:");
        for (int i = 0; i < Math.Min(10, sampleCount); i++)
        {
            Console.Write($"{buffer[i]} ");
        }
        Console.WriteLine("\n--------------------------------");

        // --- 11. Write to file ---
        using (StreamWriter sw = new StreamWriter("channel_a_data.txt"))
        {
            sw.WriteLine("PicoScope 5000A - Channel A Data");
            sw.WriteLine("================================");
            sw.WriteLine($"Samples: {sampleCount}");
            sw.WriteLine($"Timebase: {timebase} (Sample interval: {timeIntervalNs} ns)");
            sw.WriteLine("Range: 2V, DC coupled\n");
            sw.WriteLine("Sample\tTime(ns)\tADC\tmV");
            sw.WriteLine("------\t--------\t---\t--");

            for (int i = 0; i < sampleCount; i++)
            {
                int mv = AdcToMv(buffer[i], ApiWrapper.PS5000A_RANGE.PS5000A_2V, maxADC);
                sw.WriteLine($"{i}\t{(long)i * timeIntervalNs}\t{buffer[i]}\t{mv}");
            }
        }

        Console.WriteLine($"[File] Data written to channel_a_data.txt ({sampleCount} samples)");

        // --- 12. Cleanup ---
        ApiWrapper.ps5000aStop(handle);
        ApiWrapper.ps5000aCloseUnit(handle);
        Console.WriteLine("[Done] Device closed and program finished.");
    }
}
