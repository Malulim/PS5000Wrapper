using System;
using System.IO;
using System.Threading;
using Wrapper;

namespace Demo
{
    class GenerateWaveDemo
    {
        const int BUFFER_SIZE = 1024;
        static readonly string outFile = "wave_gen.txt";

        static void Main(string[] args)
        {
            short handle = 0;
            short maxADCValue = 0;

            // Open device
            var status = ApiWrapper.ps5000aOpenUnit(out handle, null, ApiWrapper.PS5000A_DEVICE_RESOLUTION.PS5000A_DR_8BIT);
            if (status != 0)
            {
                Console.WriteLine($"Unable to open device, error code: 0x{status:X8}");
                return;
            }

            try
            {
                // Get max ADC value
                uint maxValueStatus = ApiWrapper.ps5000aMaximumValue(handle, out maxADCValue);
                Console.WriteLine($"ps5000aMaximumValue return: 0x{maxValueStatus:X8}, maxADCValue = {maxADCValue}");
                if (maxADCValue == 0)
                {
                    Console.WriteLine("Error: maxADCValue is 0 — cannot convert ADC counts to mV. Aborting.");
                    return;
                }

                // Configure channel A for capture
                uint setChannelStatus = ApiWrapper.ps5000aSetChannel(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    1, // enabled
                    ApiWrapper.PS5000A_COUPLING.PS5000A_DC,
                    ApiWrapper.PS5000A_RANGE.PS5000A_2V, // ±2V range
                    0.0f
                );
                Console.WriteLine($"ps5000aSetChannel return: 0x{setChannelStatus:X8}");

                // Prepare data buffers for channel A (two buffers)
                short[] bufferMax = new short[BUFFER_SIZE];
                short[] bufferMin = new short[BUFFER_SIZE];
                uint setDataBuffersStatus = ApiWrapper.ps5000aSetDataBuffers(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    bufferMax,
                    bufferMin,
                    BUFFER_SIZE,
                    0,
                    ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE
                );
                Console.WriteLine($"ps5000aSetDataBuffers return: 0x{setDataBuffersStatus:X8}");

                // Configure signal generator: continuous 1 kHz sine, 2000 mV pk-to-pk (2 Vpp)
                uint pkToPk = 2000; // mV
                int offsetVoltage = 0; // mV
                double frequency = 1000.0; // Hz

                status = ApiWrapper.ps5000aSetSigGenBuiltInV2(
                    handle,
                    offsetVoltage,
                    pkToPk,
                    ApiWrapper.PS5000A_WAVE_TYPE.PS5000A_SINE,
                    frequency,
                    frequency,
                    0.0, // increment
                    1.0, // dwell time (s) - irrelevant for single freq
                    ApiWrapper.PS5000A_SWEEP_TYPE.PS5000A_UP,
                    ApiWrapper.PS5000A_EXTRA_OPERATIONS.PS5000A_ES_OFF,
                    ApiWrapper.PS5000A_SHOT_SWEEP_TRIGGER_CONTINUOUS_RUN, // continuous
                    1,
                    ApiWrapper.PS5000A_SIGGEN_TRIG_TYPE.PS5000A_SIGGEN_GATE_HIGH,
                    ApiWrapper.PS5000A_SIGGEN_TRIG_SOURCE.PS5000A_SIGGEN_NONE,
                    0
                );
                uint siggenSetStatus = status;
                if (siggenSetStatus != 0)
                {
                    Console.WriteLine($"ps5000aSetSigGenBuiltInV2 failed: 0x{siggenSetStatus:X8}");
                    return;
                }

                // Start signal generator (software control) and check return status
                uint siggenStartStatus = ApiWrapper.ps5000aSigGenSoftwareControl(handle, 1);
                if (siggenStartStatus != 0)
                {
                    Console.WriteLine($"ps5000aSigGenSoftwareControl failed to start AWG: 0x{siggenStartStatus:X8}");
                    // Continue to attempt capture so we can give more diagnostics below
                }

                Console.WriteLine("Signal generator started (sine 1 kHz, 2 Vpp). Collecting block data...");

                // Get timebase
                int timeInterval;
                int maxSamples;
                uint getTimebaseStatus = ApiWrapper.ps5000aGetTimebase(handle, 8, BUFFER_SIZE, out timeInterval, out maxSamples, 0);
                Console.WriteLine($"ps5000aGetTimebase return: 0x{getTimebaseStatus:X8}, timeInterval(ns)={timeInterval}, maxSamples={maxSamples}");

                // Start block collection: no pre-trigger, BUFFER_SIZE post-trigger
                int timeIndisposedMs;
                uint runBlockStatus = ApiWrapper.ps5000aRunBlock(handle, 0, BUFFER_SIZE, 8, out timeIndisposedMs, 0, null, IntPtr.Zero);
                Console.WriteLine($"ps5000aRunBlock return: 0x{runBlockStatus:X8}, timeIndisposedMs={timeIndisposedMs}");

                // small wait for acquisition to complete
                Thread.Sleep(200);

                // Retrieve data
                uint sampleCount = (uint)BUFFER_SIZE;
                uint getValuesStatus = ApiWrapper.ps5000aGetValues(handle, 0, out sampleCount, 1, ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE, 0, null);
                Console.WriteLine($"ps5000aGetValues return: 0x{getValuesStatus:X8}, sampleCount={sampleCount}");

                // --- changed code: convert to mV, detect all-zero-like traces, save & display ---
                int[] samplesMv = new int[sampleCount];
                int maxAbsMv = 0;
                for (int i = 0; i < sampleCount; i++)
                {
                    samplesMv[i] = (bufferMax[i] * 1000) / maxADCValue; // convert to mV
                    int absv = samplesMv[i] >= 0 ? samplesMv[i] : -samplesMv[i];
                    if (absv > maxAbsMv) maxAbsMv = absv;
                }

                // If every sample is very close to 0 mV, warn and give troubleshooting hints
                const int ZERO_THRESHOLD_MV = 5; // adjust tolerance if needed
                if (maxAbsMv <= ZERO_THRESHOLD_MV)
                {
                    Console.WriteLine("Warning: captured waveform is ~0 mV for all samples.");
                    // Show siggen API statuses to help determine cause
                    Console.WriteLine($"SigGen configuration return: 0x{siggenSetStatus:X8}");
                    Console.WriteLine($"SigGen software control return: 0x{siggenStartStatus:X8}");
                    Console.WriteLine($"SetChannel return: 0x{setChannelStatus:X8}");
                    Console.WriteLine($"SetDataBuffers return: 0x{setDataBuffersStatus:X8}");
                    Console.WriteLine($"GetTimebase return: 0x{getTimebaseStatus:X8}");
                    Console.WriteLine($"RunBlock return: 0x{runBlockStatus:X8}");
                    Console.WriteLine($"GetValues return: 0x{getValuesStatus:X8}");
                }
                else
                {
                    Console.WriteLine("First 10 samples (mV):");
                    for (int i = 0; i < Math.Min(10, (int)sampleCount); i++)
                    {
                        Console.WriteLine($"[{i}] {samplesMv[i]} mV");
                    }
                }

                // Save to file (all samples)
                using (StreamWriter writer = new StreamWriter(outFile))
                {
                    writer.WriteLine("Generated wave capture (mV)\n");
                    for (int i = 0; i < sampleCount; i++)
                    {
                        writer.WriteLine(samplesMv[i]);
                    }
                }
                // --- end changed code ---

                Console.WriteLine($"Done. Data saved to {outFile}");
            }
            finally
            {
                // Stop signal generator and device
                try { ApiWrapper.ps5000aSigGenSoftwareControl(handle, 0); } catch { }
                try { ApiWrapper.ps5000aStop(handle); } catch { }
                try { ApiWrapper.ps5000aCloseUnit(handle); } catch { }
            }
        }
    }
}
