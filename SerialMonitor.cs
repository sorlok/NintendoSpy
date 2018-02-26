using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.IO.Ports;
using System.Threading;
using System.Windows.Threading;

namespace NintendoSpy
{
    public delegate void PacketEventHandler (object sender, byte[] packet);

    public class SerialMonitor
    {
        const int BAUD_RATE = 115200;
        const int TIMER_MS = 30;

        public event PacketEventHandler PacketReceived;
        public Dispatcher PacketReceivedInvoker;

        public event EventHandler Disconnected;

        SerialPort _datPort;
        List<byte> _localBuffer;

        // Previous "notified" packet, and a lock for it.
        private byte[] lastPacket;
        private Object lastPacketLock = new Object();

        // Has the serial controller processed a packet?
        // If not, don't barrage its message queue (it will be behind anyway).
        private int listenerProcessingPacket = 0;

        // Our data reading thread
        Thread readThread;
        int stopThread;

        public SerialMonitor(string portName)
        {
            _localBuffer = new List<byte>();
            _datPort = new SerialPort(portName, BAUD_RATE);
        }

        // Get a copy of the latest packet.
        public byte[] getLastPacket()
        {
            byte[] res = null;
            lock(lastPacketLock)
            {
                res = new byte[lastPacket.Length];
                Array.Copy(lastPacket, res, lastPacket.Length);
            }
            return res;
        }

        public void signalPacketParsed()
        {
            Interlocked.Exchange(ref listenerProcessingPacket, 0);
        }

        public void ReadThreadProc()
        {
            // Reading stopThread is atomic
            while (stopThread == 0)
            {
                tickThread();
                Thread.Sleep(0);
            }
        }

        public void Start ()
        {
            //if (_timer != null) return;
            if (readThread != null) { return; }

            _localBuffer.Clear ();
            lock(lastPacketLock)
            {
                lastPacket = new byte[] { };
            }
            _datPort.Open ();

            Interlocked.Exchange(ref listenerProcessingPacket, 0);
            Interlocked.Exchange(ref stopThread, 0);
            readThread = new Thread(new ThreadStart(ReadThreadProc));
            readThread.Start();
        }

        public void Stop ()
        {
            if (_datPort != null) {
                try { // If the device has been unplugged, Close will throw an IOException.  This is fine, we'll just keep cleaning up.
                    _datPort.Close ();
                }
                catch (IOException) {}
                _datPort = null;
            }
            //if (_timer != null) {
            //    _timer.Stop ();
            //    _timer = null;
            //}
            if (readThread != null) {
                Interlocked.Exchange(ref stopThread, 1);
                readThread.Join();
                readThread = null;
            }
        }

        // Tick for thread; only calls back if a button state changes.
        void tickThread()
        {
            if (_datPort == null || !_datPort.IsOpen || PacketReceived == null) return;

            // Read 
            try
            {
                int readCount = _datPort.BytesToRead;
                if (readCount < 1) return;
                byte[] readBuffer = new byte[readCount];
                _datPort.Read(readBuffer, 0, readCount);
                _datPort.DiscardInBuffer();
                _localBuffer.AddRange(readBuffer);
            }
            catch (IOException)
            {
                Stop();
                if (Disconnected != null) Disconnected(this, EventArgs.Empty);
                return;
            }

            // Try and find 2 splitting characters in our buffer.
            int lastSplitIndex = _localBuffer.LastIndexOf(0x0A);
            if (lastSplitIndex <= 1) return;
            int sndLastSplitIndex = _localBuffer.LastIndexOf(0x0A, lastSplitIndex - 1);
            if (lastSplitIndex == -1) return;

            // Grab the latest packet out of the buffer.
            int packetStart = sndLastSplitIndex + 1;
            int packetSize = lastSplitIndex - packetStart;
            byte[] currPacket = _localBuffer.GetRange(packetStart, packetSize).ToArray();

            // Fire off an update only if different.
            bool firePacket = false;
            lock(lastPacketLock)
            {
                // I *think* this won't cause problems?
                // Worst case, just skip this check.
                if (!lastPacket.SequenceEqual(currPacket))
                {
                    if (Interlocked.Exchange(ref listenerProcessingPacket, 1) == 0)
                    {
                        firePacket = true;
                    }
                    lastPacket = currPacket;
                }
            }
            if (firePacket)
            {
                PacketReceivedInvoker.Invoke(PacketReceived, new object[] { null,null });
            }

            // Clear our buffer up until the last split character.
            _localBuffer.RemoveRange(0, lastSplitIndex);
        }
    }
}
