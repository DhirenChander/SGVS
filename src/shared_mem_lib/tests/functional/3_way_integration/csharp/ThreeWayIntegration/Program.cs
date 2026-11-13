using System;
using System.Threading;
using System.Collections.Generic;

using SharedMemLibrary;

class Program {
    static void Receiver(ref GCFrameBuffer buff, Encoding enc) {
        while(true) {
            (GCFrame[] Frames, bool success) = buff.BlockingPopFrames(enc, 120, 2000);

            if (success && Frames.Length > 0) 
            {
                Console.WriteLine("Got " + Frames.Length.ToString() + " Frames.");
                Console.WriteLine("Leading Byte: {0:X}", Frames[0].Data[0]);
            } else if (success) 
            {
                Console.WriteLine("Got frames successfully, but no frames were present.");
            } else 
            {
                Console.WriteLine("Failed to get frames.");
            }
        }

    }

    static void Main(string[] args) {
        GCFrameBuffer buffer = new GCFrameBuffer();
        
        HashSet<ulong> running_encs = new HashSet<ulong>();

        while(true) {
            (Encoding[] encodings, bool success) = buffer.ReadEncodings(10);

            if (success) {
                Console.WriteLine("Received " + encodings.Length.ToString() + " Encodings");

                bool aligned = false;

                foreach (Encoding enc in encodings) {
                    if(running_encs.Contains(enc.id)) {
                        continue;
                    }

                    // Must align before starting receiver thread
                    while (!aligned) 
                    {
                        aligned = buffer.AlignRingsToConfig();
                    }

                    running_encs.Add(enc.id);

                    if(enc.IsIncoming()) {
                        _ = Task.Run(() => Receiver(ref buffer, enc));
                    }
                }
            } else {
                Console.WriteLine("Failed to fetch encodings");
            }

            Thread.Sleep(500);
        }
    }
}
