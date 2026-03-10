using GameClient.Managers;
using GameClient.Misc;
using GameClient.WorldObjects;
using RimWorld;
using RimWorld.Planet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Verse;
using static TCPNetwork.Packets.TransferData;

namespace GameClient.Actions
{
    public class RTTransportersArrivalAction_TransportPod : TransportersArrivalAction
    {
        private RTSettlement targetSettlement;

        public override bool GeneratesMap => false;

        public RTTransportersArrivalAction_TransportPod(RTSettlement settlement)
        {
            targetSettlement = settlement;
        }

        public override FloatMenuAcceptanceReport StillValid(
    IEnumerable<IThingHolder> transporters, PlanetTile destinationTile)
        {
            return FloatMenuAcceptanceReport.WasAccepted;
        }

        public override void Arrived(
            List<ActiveTransporterInfo> transporters, PlanetTile tile)
        {
            if (SessionHandler.IsInTransfer) return;
            SessionHandler.IsInTransfer = true;

            SessionHandler.ChosenPods = transporters.Cast<IThingHolder>();
            SessionHandler.ChosenSettlement = targetSettlement;

            TransferManager.TakeTransferItemsFromPods(transporters.Cast<IThingHolder>());
            TransferManager.SendTransferRequestToServer(TransferLocation.TransportPod);
        }
    }
}
