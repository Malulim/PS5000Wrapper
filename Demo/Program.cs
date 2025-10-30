using System;
using System.IO;
using System.Threading;
using Wrapper;

namespace Demo
{
    class Program 
    {
        const int BUFFER_SIZE = 1024;
        
        static void Main(string[] args)
        {
            short handle = 0;
            short maxADCValue = 0;
            
            // Open device with 8-bit resolution
            var status = ApiWrapper.ps5000aOpenUnit(out handle, null, ApiWrapper.PS5000A_DEVICE_RESOLUTION.PS5000A_DR_8BIT);
            if (status != 0)
            {
                Console.WriteLine($"Unable to open device, error code: 0x{status:X8}");
                return;
            }

            try 
            {
                // Get max ADC value and verify
                uint maxValueStatus = ApiWrapper.ps5000aMaximumValue(handle, out maxADCValue);
                Console.WriteLine($"Device opened, maxADCValue = {maxADCValue}");
                
                if (maxADCValue == 0)
                {
                    throw new Exception("maxADCValue is 0");
                }

                // Configure signal generator first: 1 kHz sine wave, 2000mV pk-pk
                uint pkToPk = 2000;      // mV
                int offsetVoltage = 0;    // mV  
                double frequency = 1000;   // Hz

                status = ApiWrapper.ps5000aSetSigGenBuiltInV2(
                    handle,
                    offsetVoltage,
                    pkToPk,
                    ApiWrapper.PS5000A_WAVE_TYPE.PS5000A_SINE,
                    frequency,
                    frequency,
                    0.0,    // increment
                    1.0,    // dwell time
                    ApiWrapper.PS5000A_SWEEP_TYPE.PS5000A_UP,
                    ApiWrapper.PS5000A_EXTRA_OPERATIONS.PS5000A_ES_OFF,
                    ApiWrapper.PS5000A_SHOT_SWEEP_TRIGGER_CONTINUOUS_RUN,
                    1,
                    ApiWrapper.PS5000A_SIGGEN_TRIG_TYPE.PS5000A_SIGGEN_GATE_HIGH,
                    ApiWrapper.PS5000A_SIGGEN_TRIG_SOURCE.PS5000A_SIGGEN_NONE,
                    0
                );

                if (status != 0)
                {
                    Console.WriteLine($"Failed to configure signal generator: 0x{status:X8}");
                    return;
                }

                // Start signal generator
                status = ApiWrapper.ps5000aSigGenSoftwareControl(handle, 1);
                if (status != 0)
                {
                    Console.WriteLine($"Failed to start signal generator: 0x{status:X8}");
                    return;
                }

                Console.WriteLine("Signal generator started (1 kHz sine, 2 Vpp)");
                Thread.Sleep(100); // Let signal stabilize

                // Configure channel A for capture
                status = ApiWrapper.ps5000aSetChannel(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    1,          // enabled
                    ApiWrapper.PS5000A_COUPLING.PS5000A_DC,
                    ApiWrapper.PS5000A_RANGE.PS5000A_2V,
                    0.0f        // no offset
                );

                if (status != 0)
                {
                    Console.WriteLine($"Failed to configure channel: 0x{status:X8}");
                    return;
                }

                // Set up data buffers
                short[] bufferA = new short[BUFFER_SIZE];
                short[] bufferB = new short[BUFFER_SIZE];
                
                status = ApiWrapper.ps5000aSetDataBuffers(
                    handle,
                    ApiWrapper.PS5000A_CHANNEL.PS5000A_CHANNEL_A,
                    bufferA,
                    bufferB, 
                    BUFFER_SIZE,
                    0,
                    ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE
                );

                // Configure timebase and verify
                int timeInterval;
                int maxSamples;
                status = ApiWrapper.ps5000aGetTimebase(handle, 8, BUFFER_SIZE, out timeInterval, out maxSamples, 0);
                Console.WriteLine($"Timebase configured: interval = {timeInterval}ns");

                // Start block capture
                int timeIndisposedMs;
                status = ApiWrapper.ps5000aRunBlock(
                    handle,
                    0,              // no pretrigger samples
                    BUFFER_SIZE,    // number of samples
                    8,              // timebase
                    out timeIndisposedMs,
                    0,              // segment index
                    null,           // callback
                    IntPtr.Zero     // callback param
                );

                if (status != 0)
                {
                    Console.WriteLine($"Failed to start capture: 0x{status:X8}");
                    return;
                }

                // Wait for data
                Thread.Sleep(timeIndisposedMs + 100);

                // Get block of data
                uint sampleCount = BUFFER_SIZE;
                status = ApiWrapper.ps5000aGetValues(
                    handle,
                    0,              // start index
                    out sampleCount,
                    1,              // downsample ratio
                    ApiWrapper.PS5000A_RATIO_MODE.PS5000A_RATIO_MODE_NONE,
                    0,              // segment index 
                    null            // overflow
                );

                if (status != 0)
                {
                    Console.WriteLine($"Failed to get samples: 0x{status:X8}");
                    return;
                }

                // Display first 10 samples in mV
                Console.WriteLine("\nFirst 10 samples:");
                for (int i = 0; i < 10; i++)
                {
                    int mV = (bufferA[i] * 1000) / maxADCValue;
                    Console.WriteLine($"Sample {i}: {mV} mV");
                }

                // Save all samples to file
                using (StreamWriter writer = new StreamWriter("siggen_capture.txt"))
                {
                    writer.WriteLine("Time(ns),Voltage(mV)");
                    for (int i = 0; i < sampleCount; i++) 
                    {
                        int mV = (bufferA[i] * 1000) / maxADCValue;
                        writer.WriteLine($"{i * timeInterval},{mV}");
                    }
                }

                Console.WriteLine($"\nCaptured {sampleCount} samples");
                Console.WriteLine("Data saved to siggen_capture.txt");
            }
            finally
            {
                // Clean up
                try { ApiWrapper.ps5000aSigGenSoftwareControl(handle, 0); } catch { }
                try { ApiWrapper.ps5000aStop(handle); } catch { }
                try { ApiWrapper.ps5000aCloseUnit(handle); } catch { }
            }
        }
    }
}
