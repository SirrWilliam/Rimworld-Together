using GameClient.Core.Configs;
using GameClient.Dialogs;
using GameClient.Hooks.TCPNetwork;
using GameClient.Misc;
using GameClient.WorldObjects;
using RimWorld;
using RimWorld.Planet;
using Shared;
using Shared.Files;
using Shared.Misc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using TCPNetwork;
using TCPNetwork.Packets;
using UnityEngine;
using Verse;
using Verse.Sound;
using static Shared.CommonEnumerators;
using static TCPNetwork.Packets.TransferData;

namespace GameClient.Managers
{
    //Class that handles all the thing transfers between clients in the mod

    public static class TransferManager
    {
        [HandlesPacket(PacketHeader.TransferManager)]
        private static void ParsePacket(byte[] bytes)
        {
            TransferData data = Serializer.ConvertBytesToObject<TransferData>(bytes);

            switch (data._stepMode)
            {
                case TransferStepMode.TradeRequest:
                    ReceiveTransferRequest(data);
                    break;

                case TransferStepMode.TradeAccept:
                    RT_Dialog_Wait.Instance.Close();
                    RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("MESSAGE", new string[] { "Transfer was a success!" }));
                    FinishTransfer(true);
                    break;
                case TransferStepMode.TransportPod:
                     SendTransportPod(data);
                    break;
                case TransferStepMode.TradeReject:
                    RT_Dialog_Wait.Instance.Close();
                    RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Player rejected the trade!" }));
                    RecoverTradeItems(TransferLocation.Caravan);
                    break;

                case TransferStepMode.TradeReRequest:
                    RT_Dialog_Wait.Instance.Close();
                    ReceiveReboundRequest(data);
                    break;

                case TransferStepMode.TradeReAccept:
                    RT_Dialog_Wait.Instance.Close();
                    GetTransferedItemsToSettlement(TransferManagerHelper.GetAllTransferedItems(SessionHandler.IncomingManifest));
                    break;

                case TransferStepMode.TradeReReject:
                    RT_Dialog_Wait.Instance.Close();
                    RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Player rejected the trade!" }));
                    RecoverTradeItems(TransferLocation.Settlement);
                    break;

                case TransferStepMode.Recover:
                    RT_Dialog_Wait.Instance.Close();
                    RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Player is not currently available!" }));
                    RecoverTradeItems(TransferLocation.Caravan);
                    break;
            }
        }

        public static void SendTransportPod(TransferData transferData)
        {
            if (Find.Maps.Any(x => x.Tile == transferData._fromTile))
            {
                LaunchDropPods();
                FinishTransfer(false);
            }
            else if (Find.Maps.Any(x => x.Tile == transferData._toTile))
            {
                SessionHandler.IncomingManifest = transferData;
                GetTransferedItemsToSettlement(TransferManagerHelper.GetAllTransferedItems(transferData), invokeMessage:false);
            }
        }

        //Takes transferable items from desired location

        public static void TakeTransferItems(TransferLocation transferLocation)
        {
            if (TradeSession.deal.TryExecute(out bool actuallyTraded))
            {
                SoundDefOf.ExecuteTrade.PlayOneShotOnCamera();

                if (transferLocation == TransferLocation.Caravan)
                {
                    TradeSession.playerNegotiator.GetCaravan().RecacheInventory();
                }
            }
        }

        //Takes transferable items from drop pods

        public static void TakeTransferItemsFromPods(IEnumerable<IThingHolder> pods)
        {
            SessionHandler.OutgoingManifest._transferMode = TransferMode.TransportPod;
            SessionHandler.OutgoingManifest._podCount = pods.Count();

            foreach (IThingHolder pod in pods)
            {
                try
                {
                    ThingOwner directlyHeldThings = pod.GetDirectlyHeldThings();
                    List<Thing> things = directlyHeldThings.ToList();
                    foreach (Thing thing in things)
                    {
                        TransferManagerHelper.AddThingToTransferManifest(thing, thing.stackCount);
                    }
                }
                catch { continue; }
            }
        }

        //Sends a transfer request to the server

        public static void SendTransferRequestToServer(TransferLocation transferLocation)
        {


             
            if (transferLocation == TransferLocation.Caravan)
            {
                RT_Dialog_Base.PushNewDialog(new RT_Dialog_Wait("Waiting for transfer response"));

                SessionHandler.ChosenCaravan = TradeSession.playerNegotiator.GetCaravan();

                SessionHandler.OutgoingManifest._stepMode = TransferStepMode.TradeRequest;
                SessionHandler.OutgoingManifest._fromTile = Find.AnyPlayerHomeMap.Tile;
                SessionHandler.OutgoingManifest._toTile = TradeSession.playerNegotiator.Tile;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.TransferManager, SessionHandler.OutgoingManifest);
            }

            else if (transferLocation == TransferLocation.Settlement)
            {
                RT_Dialog_Base.PushNewDialog(new RT_Dialog_Wait("Waiting for transfer response"));

                RT_Dialog_ItemListing.Instance.Close();

                SessionHandler.OutgoingManifest._stepMode = TransferStepMode.TradeReRequest;
                SessionHandler.OutgoingManifest._fromTile = Find.AnyPlayerHomeMap.Tile;
                SessionHandler.OutgoingManifest._toTile = SessionHandler.IncomingManifest._fromTile;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.TransferManager, SessionHandler.OutgoingManifest);
            }

            else if (transferLocation == TransferLocation.TransportPod)
            {
                SessionHandler.OutgoingManifest._stepMode = TransferStepMode.TransportPod;
                SessionHandler.OutgoingManifest._fromTile = Find.AnyPlayerHomeMap.Tile;
                SessionHandler.OutgoingManifest._toTile = SessionHandler.ChosenSettlement.Tile;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.TransferManager, SessionHandler.OutgoingManifest);
            }
        }

        //Recovers transfered items when trade fails

        public static void RecoverTradeItems(TransferLocation transferLocation)
        {
            try
            {
                Thing[] toRecover = TransferManagerHelper.GetAllTransferedItems(SessionHandler.OutgoingManifest);

                if (transferLocation == TransferLocation.Caravan) GetTransferedItemsToCaravan(toRecover, false);
                else if (transferLocation == TransferLocation.Settlement) GetTransferedItemsToSettlement(toRecover, false);
            }

            catch
            {
                Printer.Warning("Rethrowing transfer items, might be RimWorld's fault");

                Thread.Sleep(100);

                RecoverTradeItems(transferLocation);
            }
        }

        //Receives the transfered items into the settlement

        public static void GetTransferedItemsToSettlement(Thing[] things, bool success = true, bool customMap = true, bool invokeMessage = true)
        {
            Action r1 = delegate
            {
                Map map = null;
                if (customMap) map = Find.Maps.Find(x => x.Tile == SessionHandler.IncomingManifest._toTile);
                else map = Find.AnyPlayerHomeMap;

                foreach (Thing thing in things)
                {
                    if (thing is Corpse) continue;
                    else if (thing.def.CanHaveFaction)
                        thing.SetFactionDirect(Faction.OfPlayer);
                }

                if (SessionHandler.IncomingManifest._transferMode == TransferMode.TransportPod)
                {
                    int podCount = SessionHandler.IncomingManifest._podCount;
                    // Calculate how many items each pod should carry
                    // e.g. 5 items across 3 pods -> ceil(5/3) = 2 items per pod (last pod may carry fewer)
                    int itemsPerPod = Mathf.CeilToInt((float)things.Length / podCount);
                    IntVec3 lastDropCell = map.Center;

                    for (int i = 0; i < podCount; i++)
                    {
                        // Calculate which items belong to this pod
                        Thing[] podThings = things.Skip(i * itemsPerPod).Take(itemsPerPod).ToArray();
                        if (podThings.Length == 0) break;

                        // Pack items into the pod container
                        ActiveTransporterInfo podInfo = new ActiveTransporterInfo();
                        foreach (Thing thing in podThings)
                            podInfo.innerContainer.TryAdd(thing);

                        // Find a valid drop spot near the center of the map
                        bool foundSpot = DropCellFinder.TryFindDropSpotNear(
                            map.Center,
                            map, 
                            out IntVec3 dropCell,
                            allowFogged: false, 
                            canRoofPunch: false
                        );

                        lastDropCell = foundSpot ? dropCell : map.Center;

                        DropPodUtility.MakeDropPodAt(lastDropCell, map, podInfo);
                    }

                    string senderName = "Unknown";
                    RTSettlement senderSettlement = Find.WorldObjects.AllWorldObjects
                        .OfType<RTSettlement>()
                        .FirstOrDefault(s => s.Tile == SessionHandler.IncomingManifest._fromTile);

                    if (senderSettlement != null)
                        senderName = senderSettlement.Name;

                    string senderColored = senderSettlement?.Faction.GetFactionGoodwill() switch
                    {
                        Goodwill.Enemy => senderName.Colorize(ColoredText.FactionColor_Hostile),
                        Goodwill.Ally => senderName.Colorize(ColoredText.FactionColor_Ally),
                        Goodwill.Neutral => senderName.Colorize(ColoredText.FactionColor_Neutral),
                        _ => senderName
                    };

                    StringBuilder contents = new StringBuilder();
                    foreach (Thing thing in things)
                    {
                        if (thing is Corpse c)
                            contents.AppendLine($"  - {c.InnerPawn.LabelCap} (Corpse)");
                        else if (thing is Pawn pawn)
                            contents.AppendLine($"  - {pawn.LabelCap}");
                        else
                            contents.AppendLine($"  - {thing.LabelCapNoCount} x{thing.stackCount}");
                    }

                    Find.LetterStack.ReceiveLetter(
                        "Transport Pod Arrived",
                        $"A transport pod from {senderColored} has arrived at your settlement.\n\nContents:\n{contents}",
                        senderSettlement?.Faction.GetLetterDefFromGoodwill() ?? LetterDefOf.NeutralEvent,
                        new GlobalTargetInfo(lastDropCell, map)
                    );

                    FinishTransfer(false);
                }
                else
                {
                    foreach (Thing thing in things)
                    {
                        RimworldManager.PlaceThingIntoMap(thing, map, TransferManagerHelper.GetTransferLocationInMap(map), true);
                    }
                    FinishTransfer(success);
                }
            };

            if (invokeMessage)
            {
                if (success) RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("MESSAGE", new string[] { "Transfer was a success!" }, r1));
                else RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Transfer was cancelled!" }, r1));
            }
            else r1.Invoke();
        }

        //Receives the transfered items into the caravan

        public static void GetTransferedItemsToCaravan(Thing[] things, bool success = true, bool invokeMessage = true)
        {
            Action r1 = delegate
            {
                foreach (Thing thing in things) RimworldManager.PlaceThingIntoCaravan(thing, SessionHandler.ChosenCaravan);

                FinishTransfer(success);
            };

            if (invokeMessage)
            {
                if (success) RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Transfer was a success!" }, r1));
                else RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("ERROR", new string[] { "Transfer was cancelled!" }, r1));
            }
            else r1.Invoke();
        }

        //Finishes the transfer order

        public static void FinishTransfer(bool success)
        {
            SessionHandler.LastTradeStep = CommonEnumerators.TradeMode.None;

            if (success) SaveManager.ForceSave();

            SessionHandler.IncomingManifest = new TransferData();
            SessionHandler.OutgoingManifest = new TransferData();

            SessionHandler.IsInTransfer = false;
        }

        //Executes when receiving a transfer request

        public static void ReceiveTransferRequest(TransferData transferData)
        {
            try
            {
                SessionHandler.IncomingManifest = transferData;

                if (SessionHandler.IsInTransfer || ModConfigGetter.RejectTransfersBool) RejectRequest(transferData._transferMode, false);
                else
                {
                    Action r1 = delegate
                    {
                        RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(TransferManagerHelper.GetAllTransferedItems(transferData), 
                            transferData._transferMode);

                        RT_Dialog_Base.PushNewDialog(d1);
                    };

                    string description = string.Empty;
                    if (transferData._transferMode == TransferMode.Trade)
                    {
                        description = "You are receiving a trade request";
                    }
                    else if (transferData._transferMode == TransferMode.Gift) {
                        description = "You are receiving a gift request";
                    }

                    RT_Dialog_Base.PushNewDialog(new RT_Dialog_Message("MESSAGE", new string[] { description }, r1));
                }
            }

            catch
            {
                Printer.Warning("Rethrowing transfer items, might be RimWorld's fault");

                Thread.Sleep(100);

                ReceiveTransferRequest(transferData);
            }
        }

        //Executes after receiving a rebound transfer request

        public static void ReceiveReboundRequest(TransferData transferData)
        {
            try
            {
                SessionHandler.IncomingManifest = transferData;

                RT_Dialog_ItemListing d1 = new RT_Dialog_ItemListing(TransferManagerHelper.GetAllTransferedItems(transferData), TransferMode.Rebound);
                RT_Dialog_Base.PushNewDialog(d1);
            }

            catch
            {
                Printer.Warning("Rethrowing transfer items, might be RimWorld's fault");

                Thread.Sleep(100);

                ReceiveReboundRequest(transferData);
            }
        }

        //Executes when rejecting a transfer request

        public static void RejectRequest(TransferMode transferMode, bool finishTransfer = true)
        {
            if (transferMode == TransferMode.Gift)
            {
                //Nothing should happen here
            }

            else if (transferMode == TransferMode.Trade)
            {
                SessionHandler.IncomingManifest._stepMode = TransferStepMode.TradeReject;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.TransferManager, SessionHandler.IncomingManifest);
            }

            else if (transferMode == TransferMode.TransportPod)
            {
                //Nothing should happen here
            }

            else if (transferMode == TransferMode.Rebound)
            {
                SessionHandler.IncomingManifest._stepMode = TransferStepMode.TradeReReject;

                Network.ServerEndpoint.EnqueuePacket(PacketHeader.TransferManager, SessionHandler.IncomingManifest);

                RecoverTradeItems(TransferLocation.Caravan);
            }

            if (finishTransfer) FinishTransfer(false);
        }

        //Launchs the drop pods with the desired transfer request

        public static void LaunchDropPods()
        {
            foreach (IThingHolder holder in SessionHandler.ChosenPods.ToArray())
            {
                holder.GetDirectlyHeldThings().ClearAndDestroyContents();
            }
        }
    }

    //Helper class of the TransferManager class

    public static class TransferManagerHelper
    {
        //Adds desired thing into transfer manifest

        public static void AddThingToTransferManifest(Thing thing, int thingCount)
        {
            if (thing is Corpse corpse)
            {
                SessionHandler.OutgoingManifest._corpses.Add(ScribeManager.SerializeToString(corpse.InnerPawn, ScribeManager.SerializableType.Pawn));
                return;
            }
            if (ScriberH.CheckIfThingIsHuman(thing))
            {
                Pawn pawn = thing as Pawn;

                SessionHandler.OutgoingManifest._humans.Add(ScribeManager.SerializeToString(pawn, ScribeManager.SerializableType.Thing));

                RimworldManager.RemovePawnFromGame(pawn);
            }

            else if (ScriberH.CheckIfThingIsAnimal(thing))
            {
                Pawn pawn = thing as Pawn;

                SessionHandler.OutgoingManifest._animals.Add(ScribeManager.SerializeToString(pawn, ScribeManager.SerializableType.Thing));

                RimworldManager.RemovePawnFromGame(pawn);
            }

            else SessionHandler.OutgoingManifest._things.Add(ScribeManager.SerializeToString(thing, ScribeManager.SerializableType.Thing, thingCount));
        }

        //Gets the transfer location in the desired map

        public static IntVec3 GetTransferLocationInMap(Map map)
        {
            Thing tradingSpot = map.listerThings.AllThings.Find(x => x.def.defName == "RTTransferSpot");
            if (tradingSpot != null) return tradingSpot.Position;
            else
            {
                string title = "Missing transfer spot";
                string description = "Received things will appear in the center of the map";
                RimworldManager.GenerateLetter(title, description, LetterDefOf.NeutralEvent);

                return new IntVec3(map.Center.x, map.Center.y, map.Center.z);
            }
        }

        //Gets all the transfered items from the transfer into usable objects

        public static Thing[] GetAllTransferedItems(TransferData transferData)
        {
            List<Thing> allTransferedItems = new List<Thing>();

            foreach (string data in transferData._corpses)
            {
                Pawn deadPawn = ScribeManager.SerializeFromString<Pawn>(data);
                deadPawn.SetFactionDirect(null);
                Corpse corpse = (Corpse)ThingMaker.MakeThing(deadPawn.RaceProps.corpseDef);
                corpse.InnerPawn = deadPawn;
                allTransferedItems.Add(corpse);
            }

            foreach (string file in transferData._humans)
            {
                allTransferedItems.Add(ScribeManager.SerializeFromString<Pawn>(file));
            }

            foreach (string data in transferData._animals)
            {
                allTransferedItems.Add((Pawn)ScribeManager.SerializeFromString<Pawn>(data));
            }

            foreach (string data in transferData._things)
            {
                allTransferedItems.Add((Thing)ScribeManager.SerializeFromString<Thing>(data));
            }

            return allTransferedItems.ToArray();
        }
    }
}
