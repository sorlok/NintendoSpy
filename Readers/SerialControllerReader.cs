using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using System.Threading;
using System.Threading.Tasks;

namespace NintendoSpy.Readers
{
    sealed public class SerialControllerReader : IControllerReader 
    {
        public event StateEventHandler ControllerStateChanged;
        public event EventHandler ControllerDisconnected;

        Func <byte[], ControllerState> _packetParser;
        SerialMonitor _serialMonitor;

        public SerialControllerReader (string portName, Func <byte[], ControllerState> packetParser) 
        {
            _packetParser = packetParser;
            _serialMonitor = new SerialMonitor (portName);
            _serialMonitor.PacketReceived += serialMonitor_PacketReceived;
            _serialMonitor.PacketReceivedInvoker = Dispatcher.CurrentDispatcher;
            _serialMonitor.Disconnected += serialMonitor_Disconnected;
            _serialMonitor.Start ();
        }

        void serialMonitor_Disconnected(object sender, EventArgs e)
        {
            Finish ();
            if (ControllerDisconnected != null) ControllerDisconnected (this, EventArgs.Empty);
        }

        void serialMonitor_PacketReceived (object sender, byte[] packet)
        {
            if (ControllerStateChanged != null) {
                packet = _serialMonitor.getLastPacket(); // Need the latest.
                var state = _packetParser (packet);
                if (state != null) {
                    ControllerStateChanged (this, state);
                }
            }
            _serialMonitor.signalPacketParsed();
        }

        public void Finish ()
        {
            if (_serialMonitor != null) {
                _serialMonitor.Stop ();
                _serialMonitor = null;
            }
        }
    }
}
