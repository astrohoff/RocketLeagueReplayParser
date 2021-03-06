﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RocketLeagueReplayParser.NetworkStream
{
    public class ProductAttribute
    { 
        public bool Unknown1 { get; private set; }
        public UInt32 ClassIndex { get; private set; }
        public string ClassName { get; private set; }
        public bool HasValue { get; private set; } // Only used for UserColor_TA
        public UInt32 Value { get; private set; }

        const int MAX_VALUE = 14; // This may need tweaking, but it works well enough for now. Only used in older replays

        public static ProductAttribute Deserialize(BitReader br, UInt32 engineVersion, UInt32 licenseeVersion, string[] objectNames)
        {
            var pa = new ProductAttribute();
            
            pa.Unknown1 = br.ReadBit();
            pa.ClassIndex = br.ReadUInt32();
            pa.ClassName = objectNames[pa.ClassIndex];

            if (pa.ClassName == "TAGame.ProductAttribute_UserColor_TA")
            {
                if (pa.HasValue = br.ReadBit())
                {
                    pa.Value = br.ReadUInt32FromBits(31);
                }
            }
            else if (pa.ClassName == "TAGame.ProductAttribute_Painted_TA")
            {
                if (engineVersion >= 868 && licenseeVersion >= 18)
                {
                    pa.Value = br.ReadUInt32FromBits(31);
                }
                else
                {
                    pa.Value = br.ReadUInt32Max(MAX_VALUE);
                }
            }
            // I've never encountered this attribute, but Psyonix_Cone mentioned it serialized as below. Leaving it commented out until I can test it.
            /*
            else if (pa.ClassName == "ProductAttribute_Certified_TA")
            {
                var statId = br.ReadUInt32();
                var statValue = br.ReadUInt32();
            }
            */
            else
            {
                throw new Exception("Unknown product attribute class " + pa.ClassName);
            }
            return pa;
        }

        public void Serialize(BitWriter bw, UInt32 engineVersion, UInt32 licenseeVersion)
        {
            bw.Write(Unknown1);
            bw.Write(ClassIndex);

            if (ClassName == "TAGame.ProductAttribute_UserColor_TA")
            {
                // If we ever modify "Value", we should recalc "HasValue"
                bw.Write(HasValue);
                if (HasValue)
                {
                    bw.WriteFixedBitCount(Value, 31);
                }
            }
            else if (ClassName == "TAGame.ProductAttribute_Painted_TA")
            {
                if (engineVersion >= 868 && licenseeVersion >= 18)
                {
                    bw.WriteFixedBitCount(Value, 31);
                }
                else
                {
                    bw.Write(Value, MAX_VALUE);
                }
            }
            else
            {
                throw new Exception("Unknown product attribute class " + ClassName);
            }
        }
    }
}
