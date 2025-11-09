using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;

namespace BabyStepsMultiplayerServer
{
    public class ClientInfo
    {
        public required NetPeer _peer;
        public required byte _uuid;
        public string? _displayName;
        public Color? _color;
        public bool collisionsEnabled = true;
        public bool jiminyState = false;
        public byte _lbKickoffPoint = 0;
        public byte[]? _latestRawBonePacket;
        public Vector3 position;
        public List<NetPeer>? distantClients;
        public Dictionary<NetPeer, long> lastTransmitTimes = new();
        public Dictionary<byte, byte[]?> _savedPackets = new();
    }
}
