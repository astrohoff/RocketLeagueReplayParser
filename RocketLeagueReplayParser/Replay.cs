﻿using RocketLeagueReplayParser.NetworkStream;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RocketLeagueReplayParser
{
    public class Replay
    {
        public static Replay Deserialize(string filePath, out string log)
        {
            using(var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using(var br = new BinaryReader(fs))
            {
                return Deserialize(br, out log);
            }
        }

        public static Replay Deserialize(BinaryReader br, out string log)
        {
            var logSb = new StringBuilder();

            var replay = new Replay();
            replay.Unknown1 = br.ReadInt32();
            replay.Unknown2 = br.ReadInt32();
            replay.Unknown3 = br.ReadInt32();
            replay.Unknown4 = br.ReadInt32();

            // This looks almost like an ArrayProperty, but without type and the unknown ints
            replay.Unknown5 = br.ReadString2();
            
            var s = br.BaseStream.Position;
            replay.Properties = new List<Property>();
            Property prop;
            do
            {
                prop = Property.Deserialize(br);
                replay.Properties.Add(prop);
            }
            while (prop.Name != "None");

            replay.LengthOfRemainingData = br.ReadInt32();
            replay.Unknown7 = br.ReadInt32();
            replay.LevelLength = br.ReadInt32();

            // looks like sfx data, not level data. shrug
            replay.Levels = new List<Level>();
            for (int i = 0; i < replay.LevelLength; i++ )
            {
                replay.Levels.Add(Level.Deserialize(br));
            }

            replay.KeyFrameLength = br.ReadInt32();
            replay.KeyFrames = new List<KeyFrame>();
            for (int i = 0; i < replay.KeyFrameLength; i++)
            {
                replay.KeyFrames.Add(KeyFrame.Deserialize(br));
            }

            replay.NetworkStreamLength = br.ReadInt32();
            replay.NetworkStream = new List<byte>();
            for (int i = 0; i < replay.NetworkStreamLength; ++i)
            {
                replay.NetworkStream.Add(br.ReadByte());
            }

            replay.DebugStringLength = br.ReadInt32();
            replay.DebugStrings = new List<DebugString>();
            for (int i = 0; i < replay.DebugStringLength; i++)
            {
                replay.DebugStrings.Add(DebugString.Deserialize(br));
            }

            replay.TickMarkLength = br.ReadInt32();
            replay.TickMarks = new List<TickMark>();
            for (int i = 0; i < replay.TickMarkLength; i++)
            {
                replay.TickMarks.Add(TickMark.Deserialize(br));
            }

            replay.PackagesLength = br.ReadInt32();
            replay.Packages = new List<string>();
            for (int i = 0; i < replay.PackagesLength; i++)
            {
                replay.Packages.Add(br.ReadString2());
            }

            replay.ObjectLength = br.ReadInt32();
            replay.Objects = new string[replay.ObjectLength];
            for (int i = 0; i < replay.ObjectLength; i++)
            {
                replay.Objects[i] = br.ReadString2();
            }

            replay.NamesLength = br.ReadInt32();
            replay.Names = new string[replay.NamesLength];
            for (int i = 0; i < replay.NamesLength; i++)
            {
                replay.Names[i] = br.ReadString2();
            }

            replay.ClassIndexLength = br.ReadInt32();
            replay.ClassIndexes = new List<ClassIndex>();
            for (int i = 0; i < replay.ClassIndexLength; i++)
            {
                replay.ClassIndexes.Add(ClassIndex.Deserialize(br));
            }

            replay.ClassNetCacheLength = br.ReadInt32();
            replay.ClassNetCaches = new ClassNetCache[replay.ClassNetCacheLength];
            for (int i = 0; i < replay.ClassNetCacheLength; i++)
            {
                replay.ClassNetCaches[i] = ClassNetCache.Deserialize(br);

                int j = 0;
                for(j = i-1; j >=0; --j)
                {
                    if ( replay.ClassNetCaches[i].ParentId == replay.ClassNetCaches[j].Id)
                    {
                        replay.ClassNetCaches[i].Parent = replay.ClassNetCaches[j];
                        replay.ClassNetCaches[j].Children.Add(replay.ClassNetCaches[i]);
                        break;
                    }
                }
                if ( j < 0 )
                {
                    replay.ClassNetCaches[i].Root = true;
                }
            }

            // break into frames, using best guesses
            var objectIndexToName = Enumerable.Range(0, replay.Objects.Length).ToDictionary(i => i, i => replay.Objects[i]);
            //Frame lastFrame = null;
            //while ((lastFrame == null || lastFrame.Complete) && br.
            replay.Frames = ExtractFrames(replay.NetworkStream, replay.KeyFrames.Select(x => x.FilePosition), objectIndexToName, replay.ClassNetCaches, logSb);

            //var minSize = replay.Frames.Where(x => !x.Complete /*&& x.BitLength != 163*/ && x.ActorStates.Count > 0 && !string.IsNullOrWhiteSpace(x.ActorStates[0].TypeName)).Min(x => x.BitLength);
            foreach (var f in replay.Frames.Where(x => !x.Complete || x.ActorStates.Any(a=>a.ForcedComplete)))// x.Failed) )//Complete && x.BitLength == minSize))
            {
                //if ( f.ActorStates.Count >= 1 && f.ActorStates.First().State == "New")
                //{
                    logSb.AppendLine(f.ToDebugString(replay.Objects));
                //}
            }
            
            //logSb.AppendLine(replay.Frames.First().ToDebugString(replay.Objects));

            if ( br.BaseStream.Position != br.BaseStream.Length )
            {
                throw new Exception("Extra data somewhere!");
            }

            log = logSb.ToString();
            //Console.WriteLine(log);

            return replay;
        }

        private static List<Frame> ExtractFrames(IEnumerable<byte> networkStream, IEnumerable<Int32> keyFramePositions, IDictionary<int, string> objectIdToName, IEnumerable<ClassNetCache> classNetCache, StringBuilder logSb)
        {
            List<ActorState> actorStates = new List<ActorState>();

            var br = new BitReader(networkStream.ToArray());
            List<Frame> frames = new List<Frame>();

            while (br.Position < (br.Length - 64))
            {
                frames.Add(Frame.Deserialize(ref actorStates, objectIdToName, classNetCache, br));
            }

            return frames;
        }

        public void ToObj()
        {
            foreach (var f in Frames)
            {
                var frame = new { time = f.Time, actors = new List<object>() };
                if (f.ActorStates != null)
                {
                    foreach (var a in f.ActorStates.Where(x => x.TypeName == "Archetypes.Car.Car_Default" || x.TypeName == "Archetypes.Ball.Ball_Default"))
                    {
                        if (a.Properties != null)
                        {
                            var rb = a.Properties.Where(p => p.PropertyName == "TAGame.RBActor_TA:ReplicatedRBState").FirstOrDefault();
                            if (rb != null)
                            {
                                var pos = (Vector3D)rb.Data[1];
                                Console.WriteLine(string.Format("v {0} {1} {2}", pos.X, pos.Y, pos.Z));
                            }
                        }

                    }
                }
            }

           
        }

        public string ToPositionJson()
        {
            List<object> timeData = new List<object>();
            foreach (var f in Frames)
            {
                var frame = new { time = f.Time, actors = new List<object>() };
                if (f.ActorStates != null)
                {
                    foreach (var a in f.ActorStates.Where(x => x.TypeName == "Archetypes.Car.Car_Default" || x.TypeName == "Archetypes.Ball.Ball_Default" || x.TypeName == "Archetypes.Ball.CubeBall"))
                    {
                        string type = a.TypeName == "Archetypes.Car.Car_Default" ? "car" : "ball";
                        if ( a.State == ActorStateState.Deleted)
                        {
                            // Move them far away. yeah, it's cheating.
                            frame.actors.Add(new { id = a.Id, type = type, x = -30000, y = 0, z = 0, pitch = 0, roll = 0, yaw = 0 });
                        }
                        else if (a.Properties != null)
                        {
                            var rbp = a.Properties.Where(p => p.PropertyName == "TAGame.RBActor_TA:ReplicatedRBState").FirstOrDefault();
                            if (rbp != null)
                            {
                                var rb = (RigidBodyState)rbp.Data[0];
                                var pos = rb.Position;
                                var rot = rb.Rotation;
                                frame.actors.Add(new { id = a.Id, type = type, x = pos.X, y = pos.Y, z = pos.Z, pitch = rot.X, roll = rot.Y, yaw = rot.Z });
                            }
                        }

                    }
                }
                if (frame.actors.Count > 0)
                {
                    timeData.Add(frame);
                }
            }
            
            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            serializer.MaxJsonLength = 20*1024*1024;
            return serializer.Serialize(timeData);
        }


        public static Color ColorFromHSV(double hue, double saturation, double value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0)
                return Color.FromArgb(255, v, t, p);
            else if (hi == 1)
                return Color.FromArgb(255, q, v, p);
            else if (hi == 2)
                return Color.FromArgb(255, p, v, t);
            else if (hi == 3)
                return Color.FromArgb(255, p, q, v);
            else if (hi == 4)
                return Color.FromArgb(255, t, p, v);
            else
                return Color.FromArgb(255, v, p, q);
        }

        //public Color HeatMapColor(double value)
        //{
        //    if ( value == 0)
        //    {
        //        return Color.FromArgb(0,0,0);
        //    }
        //    else if ( value < 0.10 )
        //    {
        //        return ColorFromHSV(240 - (120 * (value/.10)), 1, 1);
        //    }
        //    else if (value < 0.20)
        //    {
        //        return ColorFromHSV(120 - (60 * ((value-.20) / .10)), 1, 1);
        //    }
        //    else if (value < 0.30)
        //    {
        //        return ColorFromHSV(60 - (30 * ((value - .30) / .10)), 1, 1);
        //    }
        //    else if (value < 0.40)
        //    {
        //        return ColorFromHSV(30 - (30 * ((value - .30) / .10)), 1, 1);
        //    }
        //    else
        //    {
        //        return ColorFromHSV(0, 1, 1);
        //    }

        //}

        public Color HeatMapColor(double value)
        {
            if (value == 0)
            {
                return Color.FromArgb(0, 0, 0);
            }
            else if (value < 0.40)
            {
                return Color.FromArgb((int)(255* (value/0.40)), 0, 0);
            }
            else
            {
                return Color.FromArgb(255, 0, 0);
            }

        }
        public void ToHeatmap()
        {
            var teams = Frames.First().ActorStates.Where(x => x.ClassName == "TAGame.Team_TA");
            var players = Frames.SelectMany(x => x.ActorStates.Where(a => a.ClassName == "TAGame.PRI_TA" && a.Properties != null && a.Properties.Any()))
                .Select(a => new
                {
                    Id = a.Id,
                    Name = a.Properties.Where(p => p.PropertyName == "Engine.PlayerReplicationInfo:PlayerName").Single().Data[0].ToString(),
                    TeamActorId = (int)a.Properties.Where(p => p.PropertyName == "Engine.PlayerReplicationInfo:Team").Single().Data[1]
                })
                .Distinct();

            var positions = Frames.SelectMany(x => x.ActorStates.Where(a => a.ClassName == "TAGame.Car_TA" && a.Properties != null && a.Properties.Any(p => p.PropertyName == "TAGame.RBActor_TA:ReplicatedRBState")))
                .Select(a => new
                {
                    //PlayerActorId = (int)a.Properties.Where(p => p.PropertyName == "Engine.Pawn:PlayerReplicationInfo").Single().Data[1],
                    Position = ((RigidBodyState)a.Properties.Where(p => p.PropertyName == "TAGame.RBActor_TA:ReplicatedRBState").Single().Data[0]).Position
                });


            var minX = positions.Min(x => x.Position.X);
            var minY = positions.Min(x => x.Position.Y);
            var minZ = positions.Min(x => x.Position.Z);
            var maxX = positions.Max(x => x.Position.X);
            var maxY = positions.Max(x => x.Position.Y);
            var maxZ = positions.Max(x => x.Position.Z);

            var scaleFactor = 10.0;

            var maxValue = 0;
            int heatMapWidth = (int)((maxX - minX) / scaleFactor) + 1;
            int heatMapHeight = (int)((maxY - minY) / scaleFactor) + 1;
            var heatmap = new Int16[heatMapWidth, heatMapHeight];

            //var xPositions = positions.OrderBy(p => p.Position.X);
            var yPositions = positions.OrderBy(p => p.Position.Y).ToList();

            var radius = 15;
            var squaredRadius = Math.Pow(radius, 2);

            var yIndex1 = 0;
            var yIndex2 = 0;

            for (var y = 0; y < heatMapHeight; ++y )
            {
                while (yIndex1 < yPositions.Count && ((yPositions[yIndex1].Position.Y - minY) / scaleFactor) < (y - radius)) ++yIndex1;
                yIndex2 = Math.Max(yIndex1, yIndex2);
                while (yIndex2 < yPositions.Count && ((yPositions[yIndex2].Position.Y - minY) / scaleFactor) <= (y + radius)) ++yIndex2;

                var yCandidates = yPositions.GetRange(yIndex1, yIndex2-yIndex1).OrderBy(p => p.Position.X).ToList(); 

                var xIndex1 = 0;
                var xIndex2 = 0;
                for (var x = Math.Max(0, (int)(((yCandidates[0].Position.X - minX) / scaleFactor) - radius)); x < Math.Min(heatMapWidth, ((yCandidates.Last().Position.X - minX) / scaleFactor) + radius); ++x)
                {
                    while (xIndex1 < yCandidates.Count && ((yCandidates[xIndex1].Position.X - minX)/scaleFactor) < (x - radius)) ++xIndex1;
                    xIndex2 = Math.Max(xIndex1, xIndex2);
                    while (xIndex2 < yCandidates.Count && ((yCandidates[xIndex2].Position.X - minX) / scaleFactor) <= (x + radius)) ++xIndex2;

                    var candidates = yCandidates.GetRange(xIndex1, xIndex2 - xIndex1);
                    var count = candidates.Where(p => Math.Pow(((p.Position.X - minX) / scaleFactor) - x, 2) + Math.Pow(((p.Position.Y - minY) / scaleFactor) - y, 2) <= squaredRadius).Count();
                    heatmap[x, y] = (Int16)count; // may need to change to an int
                    maxValue = Math.Max(count, maxValue); 
                }
            }
            /*
                foreach (var p in positions)
                {
                    int x = (int)(p.Position.X - minX);
                    int y = (int)(p.Position.Y - minY);


                    for (int cy = y - radius; cy <= y + radius; ++cy)
                    {
                        for (int cx = x - radius; cx <= x + radius; ++cx)
                        {
                            var distanceSquared = Math.Pow(cx - x, 2) + Math.Pow(cy - y, 2);

                            if ((cx >= 0) && (cx < heatMapWidth) && (cy >= 0) && (cy < heatMapHeight) && (distanceSquared <= squaredRadius))
                            {
                                heatmap[cx, cy]++;
                                maxValue = Math.Max(maxValue, heatmap[cx, cy]);
                            }
                        }
                    }


                }
            */

            var histogram = new Dictionary<int, int>();
            for (int x = 0; x < heatMapWidth; x++)
            {
                for (int y = 0; y < heatMapHeight; y++)
                {
                    var value = heatmap[x, y];
                    if (histogram.ContainsKey((int)value))
                    {
                        histogram[value]++;
                    }
                    else
                    {
                        histogram[value] = 1;
                    }
                }
            }

            foreach(var k in histogram.Keys)
            {
                Console.WriteLine(k.ToString() + "\t" + histogram[k].ToString());
            }


            System.Drawing.Bitmap bm = new System.Drawing.Bitmap(heatMapWidth, heatMapHeight);
            for (int x = 0; x < heatMapWidth; x++)
            {
                for (int y = 0; y < heatMapHeight; y++)
                {
                    var value = ((double)heatmap[x, y] / (double)maxValue);//(int)(255 * ((double)heatmap[x, y]) / (double)maxValue);
                    bm.SetPixel(x,y, HeatMapColor(value));// System.Drawing.Color.FromArgb(value, value, value));
                }
            }
            bm.Save(@"D:\MyData\CodeProjects\RocketLeagueReplayParser\RocketLeagueReplayParserWeb\test.jpg");
            /*
            var heatMapData = new List<object>();
            foreach(var p in players)
            {
                heatMapData.Add(new {
                    PlayerName = p.Name,
                    Team = teams.Where(x => x.Id == p.TeamActorId).Single().TypeName == "Archetypes.Teams.Team0" ? 0 : 1,
                    Positions = positions.Where(x=>x.PlayerActorId == p.Id).Select(x=>x.Position)
                });
            }
             * *
             */
        }

        public Int32 Unknown1 { get; private set; }
        public Int32 Unknown2 { get; private set; }
        public Int32 Unknown3 { get; private set; }
        public Int32 Unknown4 { get; private set; }
        public string Unknown5 { get; private set; }
        public List<Property> Properties { get; private set; }
        public Int32 LengthOfRemainingData { get; private set; }
        public Int32 Unknown7 { get; private set; } // crc?
        public Int32 LevelLength { get; private set; }
        public List<Level> Levels { get; private set; }
        public Int32 KeyFrameLength { get; private set; }
        public List<KeyFrame> KeyFrames { get; private set; }

        private Int32 NetworkStreamLength { get; set; }
        private List<byte> NetworkStream { get; set; }

        public List<Frame> Frames { get; private set; }

        public Int32 DebugStringLength { get; private set; }
        public List<DebugString> DebugStrings { get; private set; }
        public Int32 TickMarkLength { get; private set; }
        public List<TickMark> TickMarks { get; private set; }
        public Int32 PackagesLength { get; private set; }
        public List<string> Packages { get; private set; }

        public Int32 ObjectLength { get; private set; }
        public string[] Objects { get; private set; } 
        public Int32 NamesLength { get; private set; }
        public string[] Names { get; private set; } 

        public Int32 ClassIndexLength { get; private set; }
        public List<ClassIndex> ClassIndexes { get; private set; } // Dictionary<int,string> might be better, since we'll need to look up by index

        public Int32 ClassNetCacheLength { get; private set; } 
        
        private ClassNetCache[] ClassNetCaches { get; set; } 

        public string ToDebugString()
        {
            var sb = new StringBuilder();

            sb.AppendLine(Unknown5);
            foreach (var prop in Properties)
            {
                sb.AppendLine(prop.ToDebugString());
            }

            foreach (var ds in DebugStrings)
            {
                sb.AppendLine(ds.ToString());
            }

            foreach (var t in TickMarks)
            {
                sb.AppendLine(t.ToDebugString());
            }

            foreach (var kf in KeyFrames)
            {
                sb.AppendLine(kf.ToDebugString());
            }

            for (int i = 0; i < Objects.Length; ++i)
            {
                sb.AppendLine(string.Format("Object: Index {0} Name {1}", i, Objects[i]));
            }

            for (int i = 0; i < Names.Length; ++i)
            {
                sb.AppendLine(string.Format("Name: Index {0} Name {1}", i, Names[i]));
            }

            foreach (var ci in ClassIndexes)
            {
                sb.AppendLine(ci.ToDebugString());
            }

            foreach(var c in ClassNetCaches.Where(x=>x.Root))
            {
                sb.AppendLine(c.ToDebugString(Objects));
            }

            return sb.ToString();
        }
    }
}
