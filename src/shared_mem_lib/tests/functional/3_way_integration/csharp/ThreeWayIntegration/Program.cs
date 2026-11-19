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
        
        // encodings we already have a task running for, keyed by their 4-limb id
        HashSet<(ulong, ulong, ulong, ulong)> running_encs = new();

        while(true) {
            (Encoding[] encodings, bool success) = buffer.ReadEncodings(10);

            if (success) {
                Console.WriteLine("Received " + encodings.Length.ToString() + " Encodings");

                bool aligned = false;

                foreach (Encoding enc in encodings) {
                    // ids are 256 bits wide now, so a single ulong no longer fits. use the four limbs as the set key
                    var encKey = (enc.id[0], enc.id[1], enc.id[2], enc.id[3]);

                    if(running_encs.Contains(encKey)) {
                        continue;
                    }

                    // Must align before starting receiver thread
                    while (!aligned) 
                    {
                        aligned = buffer.AlignRingsToConfig();
                    }

                    running_encs.Add(encKey);

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
