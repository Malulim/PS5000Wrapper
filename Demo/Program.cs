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
                ApiWrapper.ps5000aMaximumValue(handle, out maxADCValue);

                // Configure channel A for capture
                ApiWrapper.ps5000aSetChannel(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    1, // enabled
                    ApiWrapper.PS5000A_COUPLING.PS5000A_DC,
                    ApiWrapper.PS5000A_RANGE.PS5000A_2V, // ±2V range
                    0.0f
                );

                // Prepare data buffers for channel A (two buffers)
                short[] bufferMax = new short[BUFFER_SIZE];
                short[] bufferMin = new short[BUFFER_SIZE];
                ApiWrapper.ps5000aSetDataBuffers(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    bufferMax,
                    bufferMin,
                    BUFFER_SIZE,
                    0,
                    ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE
                );

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
                if (status != 0)
                {
                    Console.WriteLine($"ps5000aSetSigGenBuiltInV2 failed: 0x{status:X8}");
                    return;
                }

                // Start signal generator (software control)
                ApiWrapper.ps5000aSigGenSoftwareControl(handle, 1);

                Console.WriteLine("Signal generator started (sine 1 kHz, 2 Vpp). Collecting block data...");

                // Get timebase
                int timeInterval;
                int maxSamples;
                ApiWrapper.ps5000aGetTimebase(handle, 8, BUFFER_SIZE, out timeInterval, out maxSamples, 0);

                // Start block collection: no pre-trigger, BUFFER_SIZE post-trigger
                int timeIndisposedMs;
                ApiWrapper.ps5000aRunBlock(handle, 0, BUFFER_SIZE, 8, out timeIndisposedMs, 0, null, IntPtr.Zero);

                // small wait for acquisition to complete
                Thread.Sleep(200);

                // Retrieve data
                uint sampleCount = (uint)BUFFER_SIZE;
                ApiWrapper.ps5000aGetValues(handle, 0, out sampleCount, 1, ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE, 0, null);

                // Save to file and display first few samples
                using (StreamWriter writer = new StreamWriter(outFile))
                {
                    writer.WriteLine("Generated wave capture (mV)\n");
                    Console.WriteLine("First 10 samples (mV):");
                    for (int i = 0; i < sampleCount; i++)
                    {
                        int mV = (bufferMax[i] * 1000) / maxADCValue; // convert to mV
                        writer.WriteLine(mV);
                        if (i < 10)
                        {
                            Console.WriteLine($"[{i}] {mV} mV");
                        }
                    }
                }

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
