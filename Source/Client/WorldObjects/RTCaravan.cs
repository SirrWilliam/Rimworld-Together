using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace GameClient.WorldObjects
{
    public class RTCaravan : WorldObject
    {
        public override float DrawAltitude
        {
            get
            {
                return 0.035f;
            }
        }
    }
}