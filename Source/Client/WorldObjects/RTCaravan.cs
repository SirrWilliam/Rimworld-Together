using RimWorld.Planet;
using UnityEngine;
using Verse;

namespace GameClient.WorldObjects
{
    public class RTCaravan : WorldObject
    {
        public static readonly Color RTOrange = new Color(0.913f, 0.471f, 0.059f);

        public override Color ExpandingIconColor => RTOrange;

        private Material cachedMaterial;

        public override Material Material
        {
            get
            {
                if (cachedMaterial == null)
                {
                    cachedMaterial = MaterialPool.MatFrom(new MaterialRequest
                    {
                        mainTex = (Texture2D)def.Material.mainTexture,
                        shader = def.Material.shader,
                        color = RTOrange
                    });
                }
                return cachedMaterial;
            }
        }

        public override float DrawAltitude
        {
            get
            {
                return 0.035f;
            }
        }
    }
}