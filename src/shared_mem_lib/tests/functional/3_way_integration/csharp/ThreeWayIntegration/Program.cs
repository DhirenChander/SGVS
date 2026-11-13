using System;
using System.Threading;
using System.Collections.Generic;

using SharedMemLibrary;

class Program {
    static void Receiver(ref GCFrameBuffer buff, Encoding enc) {
        (GCFrame[] Frames, bool success)buff.BlockingPopFrames()
    }

    static void Main(string[] args) {
        GCFrameBuffer buffer = new GCFrameBuffer();
        
        HashSet<ulong> running_encs = new HashSet<ulong>();

        while(true) {
            (Encoding[] encodings, bool success) = buffer.ReadEncodings(10);

            if (success) {
                Console.WriteLine("Received " + encodings.Length.ToString() + " Encodings");

                foreach (Encoding enc in encodings) {
                    if(running_encs.Contains(enc.id)) {
                        continue;
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
