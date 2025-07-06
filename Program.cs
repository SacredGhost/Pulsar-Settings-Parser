using System;
using System.Collections.Generic;
using System.IO;
using static Settings.pul_Parser.TrophyData;

namespace Settings.pul_Parser
{
    public class TrophyData
    {
        private readonly byte[] tropMagic = { 0x54, 0x52, 0x4F, 0x50 };

        public (byte[] beforeTrop, byte[] fromTrop) SplitByTropMagic(byte[] data)
        {
            int index = FindMagicIndex(data, tropMagic);

            if (index == -1)
            {
                throw new Exception("Corrupted or invalid file");
            }

            byte[] before = new byte[index];
            byte[] after = new byte[data.Length - index];

            Array.Copy(data, 0, before, 0, index);
            Array.Copy(data, index, after, 0, after.Length);

            return (before, after);
        }

        private int FindMagicIndex(byte[] data, byte[] tropMagic)
        {
            for (int i = 0; i <= data.Length - tropMagic.Length; i++)
            {
                bool match = true;
                for (int j = 0; j < tropMagic.Length; j++)
                {
                    if (data[i + j] != tropMagic[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return i;
            }
            return -1;
        }

        public enum Offset
        {
            Magic = 0,
            Version = Magic + 4,
            Size = Version + 4,
            NormalMushroomCount = Size + 4,
            NormalFeatherCount = NormalMushroomCount + 2,
            FastMushroomCount = NormalFeatherCount + 2,
            FastFeatherCount = FastMushroomCount + 2,
            StartOfTrackData = FastFeatherCount + 2
            // 8 bytes repeating for track data until size value
        }
        public struct TrackEntry
        {
            public string IdHex;
            public string Name;
            public byte[] CompletionFlags;
        }

        private Dictionary<string, string> trackNameLookup = new();

        public void LoadTrackNames(IEnumerable<string> textFilePath)
        {
            foreach (var line in textFilePath)
            {
                var parts = line.Split("=");
                if (parts.Length == 2)
                {
                    string name = parts[0].Trim();
                    string hexId = parts[1].Trim().ToUpper();
                    trackNameLookup[hexId] = name;
                }
            }
        }

        private uint ReadUInt32BigEndian(byte[] data, int offset)
        {
            if (offset + 4 > data.Length)
                throw new ArgumentOutOfRangeException(nameof(offset), "Cannot read 4 bytes from this offset.");

            byte[] bytes = new byte[4];
            Array.Copy(data, offset, bytes, 0, 4);

            if (BitConverter.IsLittleEndian)
                Array.Reverse(bytes);

            return BitConverter.ToUInt32(bytes, 0);
        }

        public List<TrackEntry> ParseTracks(byte[] data)
        {
            List<TrackEntry> tracks = new();
            int index = (int)Offset.StartOfTrackData;

            int sizeOffset = (int)Offset.Size;
            uint totalSize = ReadUInt32BigEndian(data, sizeOffset);
            int endOfTrackData = (int)Offset.Magic + (int)totalSize;

            while (index + 8 <= endOfTrackData && index + 8 <= data.Length)
            {
                byte[] trackBytes = new byte[8];
                Array.Copy(data, index, trackBytes, 0, 8);

                byte[] idBytes = new byte[4];
                byte[] completion = new byte[4];

                Array.Copy(trackBytes, 0, idBytes, 0, 4);
                Array.Copy(trackBytes, 4, completion, 0, 4);

                string idHex = BitConverter.ToString(idBytes.ToArray()).Replace("-", "");
                string name = trackNameLookup.TryGetValue(idHex, out var value) ? value : "Unknown Track";

                tracks.Add(new TrackEntry
                {
                    IdHex = idHex,
                    Name = name,
                    CompletionFlags = completion
                });

                index += 8;
            }
            return tracks;
        }
    }

    internal class Program
    {
        static void WriteUShort(byte[] data, int offset, ushort value)
        {
            byte[] bytes = BitConverter.GetBytes(value);
            Array.Reverse(bytes);

            data[offset] = bytes[0];
            data[offset + 1] = bytes[1];
        }

        static void GiveTrackData(List<TrackEntry> tracks, byte[] trophyData, string version, bool write)
        {
            ushort normalMushroom = 0;
            ushort normalFeather = 0;
            ushort fastMushroom = 0;
            ushort fastFeather = 0;

            List<string> normalMushroomTracks = new();
            List<string> normalFeatherTracks = new();
            List<string> fastMushroomTracks = new();
            List<string> fastFeatherTracks = new();

            foreach (var track in tracks)
            {
                if (track.CompletionFlags[0] == 1)
                {
                    normalMushroom++;
                    normalMushroomTracks.Add(track.Name);
                }
                if (track.CompletionFlags[1] == 1)
                {
                    normalFeather++;
                    normalFeatherTracks.Add(track.Name);
                }
                if (track.CompletionFlags[2] == 1)
                {
                    fastMushroom++;
                    fastFeatherTracks.Add(track.Name);
                }
                if (track.CompletionFlags[3] == 1)
                {
                    fastFeather++;
                    fastFeatherTracks.Add(track.Name);
                }
            }

            Console.WriteLine($"{version}: Tracks completed for 150cc:");
            foreach (var name in normalMushroomTracks)
            {
                Console.WriteLine($"- {name}");
            }
            Console.WriteLine();
            Console.WriteLine($"{version}: Tracks completed for 150cc Feather:");
            foreach (var name in normalFeatherTracks)
            {
                Console.WriteLine($"- {name}");
            }
            Console.WriteLine();
            Console.WriteLine($"{version}: Tracks completed for 200cc:");
            foreach (var name in fastMushroomTracks)
            {
                Console.WriteLine($"- {name}");
            }
            Console.WriteLine();
            Console.WriteLine($"{version}: Tracks completed for 200cc Feather:");
            foreach (var name in fastFeatherTracks)
            {
                Console.WriteLine($"- {name}");
            }

            Console.WriteLine();
            Console.WriteLine("Trophy totals:");
            Console.WriteLine($"150cc: {normalMushroom}");
            Console.WriteLine($"150cc Feather: {normalFeather}");
            Console.WriteLine($"200cc: {fastMushroom}");
            Console.WriteLine($"200cc Feather: {fastFeather}\n");

            if (write)
            {
                int startOfTrackData = (int)TrophyData.Offset.StartOfTrackData;

                for (int i = 0; i < tracks.Count; i++)
                {
                    var track = tracks[i];

                    byte[] newFlags = new byte[4];
                    if (normalMushroomTracks.Contains(track.Name)) newFlags[0] = 1;
                    if (normalFeatherTracks.Contains(track.Name)) newFlags[1] = 1;
                    if (fastMushroomTracks.Contains(track.Name)) newFlags[2] = 1;
                    if (fastFeatherTracks.Contains(track.Name)) newFlags[3] = 1;

                    track.CompletionFlags = newFlags;
                    tracks[i] = track;

                    int offset = startOfTrackData + i * 8 + 4;
                    Array.Copy(newFlags, 0, trophyData, offset, 4);
                }

                WriteUShort(trophyData, (int)TrophyData.Offset.NormalMushroomCount, normalMushroom);
                WriteUShort(trophyData, (int)TrophyData.Offset.NormalFeatherCount, normalFeather);
                WriteUShort(trophyData, (int)TrophyData.Offset.FastMushroomCount, fastMushroom);
                WriteUShort(trophyData, (int)TrophyData.Offset.FastFeatherCount, fastFeather);
            }
        }

        static void Main(string[] args)
        {
            string oldFilePath = "./old.pul";
            string newFilePath = "./new.pul";
            string normalTrackNames = "./normal.txt";
            string oldTrackNames = "./old.txt";
            string newTrackNames = "./new.txt";

            if (!File.Exists(oldFilePath) || !File.Exists(newFilePath) || !File.Exists(oldTrackNames) || !File.Exists(newTrackNames))
            {
                Console.WriteLine("One of the Settings.pul or track list text files are missing\n(Name them old.pul, new.pul, old.txt, new.txt)");
                return;
            }

            try
            {
                byte[] oldFileData = File.ReadAllBytes(oldFilePath);
                byte[] newFileData = File.ReadAllBytes(newFilePath);

                TrophyData parser = new TrophyData();
                var (oldBeforeTrop, oldTrophyData) = parser.SplitByTropMagic(oldFileData);
                var (newBeforeTrop, newTrophyData) = parser.SplitByTropMagic(newFileData);
                IEnumerable<string> oldCombinedNames;
                IEnumerable<string> newCombinedNames;

                try
                {
                    oldCombinedNames = File.ReadAllLines(oldTrackNames)
                        .Concat(File.ReadAllLines(normalTrackNames));
                }
                catch (Exception)
                {
                    oldCombinedNames = File.ReadAllLines(oldTrackNames);
                }
                try
                {
                    newCombinedNames = File.ReadAllLines(newTrackNames)
                        .Concat(File.ReadAllLines(normalTrackNames));
                }
                catch (Exception)
                {
                    newCombinedNames = File.ReadAllLines(newTrackNames);
                }

                parser.LoadTrackNames(oldCombinedNames);
                var oldTracks = parser.ParseTracks(oldTrophyData);
                parser.LoadTrackNames(newCombinedNames);
                var newTracks = parser.ParseTracks(newTrophyData);

                foreach (var oldTrack in oldTracks)
                {
                    if (oldTrack.CompletionFlags.All(b => b == 0))
                        continue;

                    var matchingNewTrackIndex = newTracks.FindIndex(t => t.IdHex == oldTrack.IdHex);

                    if (matchingNewTrackIndex != -1)
                    {
                        var updatedTrack = newTracks[matchingNewTrackIndex];
                        updatedTrack.CompletionFlags = oldTrack.CompletionFlags;
                        newTracks[matchingNewTrackIndex] = updatedTrack;
                    }
                }

                GiveTrackData(oldTracks, oldTrophyData, "Old", false);
                GiveTrackData(newTracks, newTrophyData, "New", true);

                byte[] combined = new byte[newBeforeTrop.Length + newTrophyData.Length];
                Array.Copy(newBeforeTrop, 0, combined, 0, newBeforeTrop.Length);
                Array.Copy(newTrophyData, 0, combined, newBeforeTrop.Length, newTrophyData.Length);

                File.WriteAllBytes("UpdatedSettings.pul", combined);
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex.Message);
            }
        }
    }
}
