using Shared.Files;
using System.Collections.Generic;
using static Shared.CommonEnumerators;

namespace TCPNetwork.Packets
{
    public class TransferData
    {
        public enum TransferMode { Gift, Trade, Rebound, TransportPod }

        public enum TransferLocation { Caravan, Settlement, TransportPod }

        public enum TransferStepMode { TradeRequest, TradeAccept, TradeReject, TradeReRequest, TradeReAccept, TradeReReject, Recover, TransportPod }

        public TransferStepMode _stepMode { get; set; } = TransferStepMode.TradeRequest;

        public TransferMode _transferMode { get; set; } = TransferMode.Gift;

        public int _fromTile { get; set; } = -1;

        public int _toTile { get; set; } = -1;

        public int _podCount { get; set; } = 1;

        public List<string> _humans { get; set; } = new List<string>();

        public List<string> _animals { get; set; } = new List<string>();

        public List<string> _things { get; set; } = new List<string>();
    }
}
